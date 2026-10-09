namespace Maf.Lab.Domain.Configuration;

/// <summary>
/// The dev issuer's settings. It lives here, rather than beside the retrieval options, because every service that
/// validates or mints a token needs it — including ones that know nothing about retrieval.
/// </summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "maf-lab-dev-issuer";
    public string Audience { get; set; } = "api";
    /// <summary>Company OIDC issuer/authority. Its published JWKS replaces the development signing key.</summary>
    public string? Authority { get; set; }
    /// <summary>The trusted client whose client roles may contain core roles; realm core roles are also accepted.</summary>
    public string CoreRoleClientId { get; set; } = "api";
    /// <summary>The OIDC public client used by the browser's authorization-code/PKCE sign-in.</summary>
    public string WebClientId { get; set; } = "web";
    /// <summary>Canonical public MCP resource URL, set separately on each company-authenticated resource host.</summary>
    public string? ResourceUri { get; set; }
    /// <summary>Explicit CIDRs of ingress peers allowed to forward the public host/scheme; empty means no forwarding.</summary>
    public string[] TrustedProxyNetworks { get; set; } = [];
    /// <summary>HMAC key, at least 32 bytes. Dev only; override via environment in compose.</summary>
    public string SigningKey { get; set; } = "maf-lab-dev-signing-key-change-me-0123456789";
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);
}
