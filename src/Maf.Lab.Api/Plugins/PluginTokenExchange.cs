using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Configuration;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Maf.Lab.Api.Plugins;

public sealed class PluginTokenExchangeOptions
{
    public const string Section = "TokenExchange";
    public string TokenEndpoint { get; set; } = "";
    public string ClientId { get; set; } = "api";
    public string ClientSecret { get; set; } = "";
    public string Scope { get; set; } = "organization domain-claims";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>OAuth RFC 8693 exchange; no passthrough, no user-controlled endpoint, no token logging.</summary>
public sealed class PluginTokenExchange(IPluginAccess access, PluginCatalogue installed, IHttpClientFactory clients,
    IOptions<PluginTokenExchangeOptions> options, IOptionsMonitor<JwtBearerOptions> authentication, TimeProvider clock,
    IOptions<AuthOptions>? identity = null) : IPluginTokens
{
    public const string ClientName = "plugin-token-exchange";
    private const string TokenType = "urn:ietf:params:oauth:token-type:access_token";
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private sealed class CacheEntry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public string? Token;
        public DateTimeOffset ExpiresAt;
    }

    public async Task<string> ForAsync(Principal principal, string plugin, string subjectToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = PluginAccessContext.For(principal) ?? await access.For(principal, ct);
        if (!snapshot.IsInUse(plugin) || !installed.Current.Contains(plugin)) throw new UnauthorizedAccessException("The plugin is not in use.");
        var setting = options.Value;
        if (!Uri.TryCreate(setting.TokenEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback))
            || string.IsNullOrWhiteSpace(setting.ClientId) || string.IsNullOrWhiteSpace(setting.ClientSecret))
            throw new InvalidOperationException("Token exchange requires a trusted token endpoint and confidential requester configuration.");
        var subject = await ValidateAsync(subjectToken, setting.ClientId, ct, singleAudience: false);
        if (subject.Subject != principal.UserId || !subject.Audiences.Contains(setting.ClientId, StringComparer.Ordinal)
            || subject.ValidTo <= clock.GetUtcNow().UtcDateTime || !SamePrincipal(subject, principal))
            throw new UnauthorizedAccessException("The subject token does not belong to this API caller.");
        var scope = ScopeFor(principal);
        var sessionId = principal.IsPlatformAdmin ? SessionId(subject) : null;
        if (principal.IsPlatformAdmin && sessionId is null)
            throw new UnauthorizedAccessException("The operator subject token has no native session.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subjectToken)));
        var key = $"{principal.TenantId.Value}:{principal.UserId}:{plugin}:{fingerprint}";
        // Drop expired credential material. Gates themselves are never disposed while another caller might use them.
        foreach (var cached in _cache.Values)
        {
            if (!cached.Gate.Wait(0)) continue;
            try { if (cached.ExpiresAt <= clock.GetUtcNow()) cached.Token = null; }
            finally { cached.Gate.Release(); }
        }
        var entry = _cache.GetOrAdd(key, _ => new());
        await entry.Gate.WaitAsync(ct);
        try
        {
            if (entry.Token is not null && entry.ExpiresAt > clock.GetUtcNow()) return entry.Token;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(setting.Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{setting.ClientId}:{setting.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["subject_token"] = subjectToken,
                ["subject_token_type"] = TokenType,
                ["requested_token_type"] = TokenType,
                ["audience"] = plugin,
                ["scope"] = scope,
            });
            using var response = await clients.CreateClient(ClientName).SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new UnauthorizedAccessException("The identity provider refused token exchange.");
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var result = payload.RootElement;
            if (!result.TryGetProperty("access_token", out var tokenNode) || tokenNode.ValueKind != JsonValueKind.String
                || !result.TryGetProperty("token_type", out var type) || !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)
                || !result.TryGetProperty("issued_token_type", out var issuedType) || issuedType.GetString() != TokenType
                || !result.TryGetProperty("expires_in", out var lifetime) || !lifetime.TryGetInt32(out var seconds) || seconds <= 0)
                throw new UnauthorizedAccessException("The identity provider returned an invalid token response.");
            var token = tokenNode.GetString()!;
            var exchanged = await ValidateAsync(token, plugin, timeout.Token);
            if (token == subjectToken || !SamePrincipal(exchanged, principal)
                || principal.IsPlatformAdmin && SessionId(exchanged) != sessionId
                || !SameClaims(subject, exchanged, PrincipalClaims.DomainRoles) || !SameClaims(subject, exchanged, PrincipalClaims.AdvisorIds))
                throw new UnauthorizedAccessException("The exchanged token does not preserve the caller's identity and entitlements.");
            var expires = new[] { new DateTimeOffset(exchanged.ValidTo, TimeSpan.Zero), new DateTimeOffset(subject.ValidTo, TimeSpan.Zero), clock.GetUtcNow().AddSeconds(seconds) }.Min();
            if (expires <= clock.GetUtcNow()) throw new UnauthorizedAccessException("The exchanged token is expired.");
            entry.Token = token;
            entry.ExpiresAt = expires;
            return token;
        }
        finally { entry.Gate.Release(); }
    }

    private async Task<JsonWebToken> ValidateAsync(string token, string audience, CancellationToken ct, bool singleAudience = true)
    {
        var configured = authentication.Get(JwtBearerDefaults.AuthenticationScheme);
        var validation = configured.TokenValidationParameters.Clone();
        validation.ValidAudience = audience;
        validation.ValidAudiences = null;
        validation.ValidateAudience = true;
        validation.ValidateIssuerSigningKey = true;
        validation.ValidateIssuer = true;
        validation.ValidateLifetime = true;
        validation.LifetimeValidator = (before, expires, _, _) => expires.HasValue
            && expires.Value > clock.GetUtcNow().UtcDateTime && (!before.HasValue || before.Value <= clock.GetUtcNow().UtcDateTime);
        if (configured.ConfigurationManager is not null)
        {
            var configuration = await configured.ConfigurationManager.GetConfigurationAsync(ct);
            validation.IssuerSigningKeys = configuration.SigningKeys;
        }
        var checkedToken = await new JsonWebTokenHandler().ValidateTokenAsync(token, validation);
        var parsed = ReadToken(token);
        if (!checkedToken.IsValid || !parsed.Audiences.Contains(audience, StringComparer.Ordinal)
            || singleAudience && (parsed.Audiences.Count() != 1 || parsed.Audiences.Single() != audience))
            throw new UnauthorizedAccessException("The exchanged token failed audience or signature validation.");
        return parsed;
    }

    private static string? SessionId(JsonWebToken token)
    {
        using var json = JsonDocument.Parse(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(token.EncodedPayload));
        var fields = json.RootElement.EnumerateObject().Where(property => property.NameEquals("sid")).ToArray();
        return fields.Length == 1 && fields[0].Value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(fields[0].Value.GetString()) ? fields[0].Value.GetString() : null;
    }

    private static JsonWebToken ReadToken(string token)
    {
        try { return new JsonWebToken(token); }
        catch (Exception ex) when (ex is ArgumentException or JsonException) { throw new UnauthorizedAccessException("Invalid access token."); }
    }
    private bool SamePrincipal(JsonWebToken token, Principal principal)
    {
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(token.Claims, "validated"));
        var settings = authentication.Get(JwtBearerDefaults.AuthenticationScheme);
        if (!string.IsNullOrWhiteSpace(settings.Authority))
        {
            if (token.Issuer != settings.Authority.TrimEnd('/') || token.Claims.FirstOrDefault(c => c.Type == "typ")?.Value != "Bearer"
                || token.Claims.Any(c => c.Type == "act")
                || !Maf.Lab.Retrieval.Auth.CompanyIdentityClaims.TryNormalize(user, identity?.Value.CoreRoleClientId ?? options.Value.ClientId, out var normalized)) return false;
            user = normalized;
        }
        return PrincipalClaims.TryCreate(user, out var caller) && caller == principal;
    }

    private string ScopeFor(Principal principal)
    {
        var configured = options.Value.Scope;
        if (string.IsNullOrWhiteSpace(authentication.Get(JwtBearerDefaults.AuthenticationScheme).Authority)) return configured;
        var organization = "organization:" + principal.TenantId.Value;
        var scopes = configured.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        for (var i = 0; i < scopes.Count; i++)
        {
            if (scopes[i] == "organization") scopes[i] = organization;
            else if (scopes[i].StartsWith("organization:", StringComparison.Ordinal) && scopes[i] != organization)
                throw new InvalidOperationException("Token exchange must request the caller's validated organization.");
        }
        if (!scopes.Contains(organization, StringComparer.Ordinal)) scopes.Add(organization);
        return string.Join(' ', scopes.Distinct(StringComparer.Ordinal));
    }
    private static bool SameClaims(JsonWebToken original, JsonWebToken exchanged, string claim) =>
        original.Claims.Where(c => c.Type == claim).Select(c => c.Value).Order(StringComparer.Ordinal)
            .SequenceEqual(exchanged.Claims.Where(c => c.Type == claim).Select(c => c.Value).Order(StringComparer.Ordinal));
}
