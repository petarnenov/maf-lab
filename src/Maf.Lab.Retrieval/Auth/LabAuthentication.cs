using Maf.Lab.Domain.Configuration;
using Maf.Lab.Domain.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Text.Json;
using Maf.Lab.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Lab.Retrieval.Auth;

/// <summary>Standard JWT bearer authentication, with the company issuer's discovery/JWKS or dev/qa-only HMAC.</summary>
public static class LabAuthentication
{
    public static IServiceCollection AddLabAuthentication(this IServiceCollection services, IConfiguration configuration, string? requiredAudience = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IOperatorContentAccess, SharedOperatorContentAccess>();
        var auth = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new();
        var environment = configuration["MAF_ENV"] ?? "dev";
        var development = environment is "dev" or "qa";
        if (string.IsNullOrWhiteSpace(auth.Authority))
        {
            if (!development) throw new InvalidOperationException("Company authentication requires Auth:Authority outside dev and qa.");
            return services.AddDevJwtAuthentication(configuration, requiredAudience);
        }
        if (!Uri.TryCreate(auth.Authority, UriKind.Absolute, out var authority)
            || !(authority.Scheme == "https" || development && authority.Scheme == "http" && authority.IsLoopback)
            || !string.IsNullOrEmpty(authority.UserInfo) || !string.IsNullOrEmpty(authority.Query) || !string.IsNullOrEmpty(authority.Fragment)
            || string.IsNullOrWhiteSpace(auth.CoreRoleClientId))
            throw new InvalidOperationException("Auth:Authority must name a trusted HTTPS issuer (loopback HTTP is permitted for dev/qa fixtures).");
        var issuer = authority.AbsoluteUri.TrimEnd('/');
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));
        if (requiredAudience is not null) services.PostConfigure<AuthOptions>(o => o.Audience = requiredAudience);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = issuer;
            options.RequireHttpsMetadata = authority.Scheme == "https";
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudience = requiredAudience ?? auth.Audience,
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                RequireSignedTokens = true, RequireExpirationTime = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.EcdsaSha256],
                ValidTypes = ["JWT", "at+jwt"],
                NameClaimType = PrincipalClaims.UserId, RoleClaimType = PrincipalClaims.Role,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    // A discovery response cannot widen issuer trust, and an ID token is never an API credential.
                    if (context.SecurityToken.Issuer != issuer || context.Principal?.FindFirst("typ")?.Value != "Bearer"
                        || context.Principal.HasClaim(c => c.Type == "act")
                        || !CompanyIdentityClaims.TryNormalize(context.Principal, auth.CoreRoleClientId, out var principal))
                        context.Fail("The company access token does not identify one organization and core role.");
                    else if (PrincipalClaims.TryCreate(principal, out var caller) && caller.IsPlatformAdmin
                        && (context.SecurityToken is not JsonWebToken token || !OperatorClaims(token, caller.TenantId.Value)))
                        // The handler expands arrays into individual claims. A one-element array must not masquerade
                        // as a native string session/scope before the API records an operator's session.
                        context.Fail("Company operator access tokens require a native session and the selected organization scope.");
                    else context.Principal = principal;
                    return Task.CompletedTask;
                },
            };
        });
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<IPrincipalAccessor, HttpPrincipalAccessor>();
        return services;
    }

    private static bool OperatorClaims(JsonWebToken token, string tenant)
    {
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.EncodedPayload));
        string? scope = null;
        foreach (var name in new[] { "sid", "scope" })
        {
            var values = payload.RootElement.EnumerateObject().Where(property => property.NameEquals(name)).ToArray();
            if (values.Length != 1 || values[0].Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(values[0].Value.GetString())) return false;
            if (name == "scope") scope = values[0].Value.GetString();
        }
        var organizations = scope!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(value => value == "organization" || value.StartsWith("organization:", StringComparison.Ordinal)).ToArray();
        return organizations.Length == 1 && organizations[0] == $"organization:{tenant}";
    }
}
