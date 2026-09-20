using System.Security.Claims;
using System.Text;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Maf.Lab.A2A;

/// <summary>Claims of a partner token. Deliberately disjoint from the user claims a chat token carries.</summary>
public static class PartnerClaims
{
    public const string PartnerId = "partner_id";
    public const string Scope = "scope";
}

/// <summary>
/// A partner system that talks to the assistant over A2A. It is not a <see cref="Principal"/>: a partner is never
/// a user, never inherits a user's entitlements, and cannot be passed where a user is expected.
/// </summary>
/// <param name="AllowedFirms">Decided on the server from configuration — never from anything in the request.</param>
public sealed record PartnerPrincipal(string PartnerId, IReadOnlySet<TenantId> AllowedFirms, IReadOnlySet<string> Scopes)
{
    public bool MaySee(TenantId firm) => AllowedFirms.Contains(firm);

    public bool Has(string scope) => Scopes.Contains(scope);
}

/// <summary>One partner, as configured. The lab's stand-in for a client-credentials registration.</summary>
public sealed class PartnerRegistration
{
    public string Secret { get; set; } = "";
    public List<string> Firms { get; set; } = [];
    /// <summary>Empty by default: a registration grants only what it names. Configuration binding appends to a
    /// non-empty default, which would quietly widen every partner.</summary>
    public List<string> Scopes { get; set; } = [];
}

public static class A2AScopes
{
    public const string BillingRead = "a2a.billing.read";
    public const string BillingWrite = "a2a.billing.write";
}

public sealed class A2AOptions
{
    public const string Section = "A2A";

    /// <summary>Audience of a partner token. Different from the chat audience, so a chat token fails by construction.</summary>
    public string Audience { get; set; } = "maf-lab-a2a";
    /// <summary>Where the agent answers, as advertised in the card.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:7171";
    /// <summary>The prefix this agent is served under, when more than one agent shares an entry point.</summary>
    public string PathBase { get; set; } = "";
    public string AgentVersion { get; set; } = "1.0.0";
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);
    /// <summary>
    /// The scope a caller must hold to reach this agent's protocol endpoints at all. Empty means any scope the
    /// partner's registration grants, which is what a single-purpose agent needs.
    /// </summary>
    public string RequiredScope { get; set; } = "";
    /// <summary>partner id → what it may see. The token never decides this.</summary>
    public Dictionary<string, PartnerRegistration> Partners { get; set; } = [];
    /// <summary>How many times a push delivery is retried before it is recorded as failed.</summary>
    public int PushRetries { get; set; } = 2;
    /// <summary>How long each step of the simulated billing run takes. Short in tests, lifelike in the stack.</summary>
    public int SimulatedStepMs { get; set; } = 1500;
    /// <summary>How often a resubscription looks at the shared store for the task's next change.</summary>
    public int SubscribePollMs { get; set; } = 250;
    /// <summary>How long a resubscription follows a task before giving the connection back.</summary>
    public TimeSpan SubscribeTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>Issues and reads partner tokens. Mirrors DevJwt, with a different audience and different claims.</summary>
public static class PartnerJwt
{
    public const string Scheme = "a2a-partner";

    public static (string Token, DateTimeOffset ExpiresAt) Issue(AuthOptions auth, A2AOptions a2a, string partnerId,
        IEnumerable<string> scopes, DateTimeOffset? now = null)
    {
        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var expires = issuedAt + a2a.TokenLifetime;
        var claims = new List<Claim> { new(PartnerClaims.PartnerId, partnerId) };
        claims.AddRange(scopes.Select(s => new Claim(PartnerClaims.Scope, s)));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = auth.Issuer,
            // The audience is what keeps the two token kinds apart: a chat token cannot be replayed here, and a
            // partner token cannot be replayed at /api/chat, without anyone having to remember a check.
            Audience = a2a.Audience,
            Subject = new ClaimsIdentity(claims, Scheme, PartnerClaims.PartnerId, ClaimTypes.Role),
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(Key(auth), SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }

    public static TokenValidationParameters ValidationParameters(AuthOptions auth, A2AOptions a2a) => new()
    {
        ValidIssuer = auth.Issuer,
        ValidAudience = a2a.Audience,
        IssuerSigningKey = Key(auth),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = PartnerClaims.PartnerId,
    };

    /// <summary>
    /// The partner as the server sees it: the id comes from the token, everything it may do comes from
    /// configuration. A token may claim what it likes about firms; the answer is not taken from it.
    /// </summary>
    public static PartnerPrincipal? Resolve(ClaimsPrincipal? user, A2AOptions options)
    {
        var partnerId = user?.FindFirst(PartnerClaims.PartnerId)?.Value;
        if (string.IsNullOrWhiteSpace(partnerId) || !options.Partners.TryGetValue(partnerId, out var registration))
        {
            return null;
        }
        var granted = user!.FindAll(PartnerClaims.Scope).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        return new PartnerPrincipal(
            partnerId,
            registration.Firms.Select(TenantId.Firm).ToHashSet(),
            // A scope is only effective when the registration allows it, whatever the token says.
            registration.Scopes.Where(granted.Contains).ToHashSet(StringComparer.Ordinal));
    }

    private static SymmetricSecurityKey Key(AuthOptions auth)
    {
        var bytes = Encoding.UTF8.GetBytes(auth.SigningKey);
        return bytes.Length < 32
            ? throw new InvalidOperationException("Auth:SigningKey must be at least 32 bytes.")
            : new SymmetricSecurityKey(bytes);
    }
}
