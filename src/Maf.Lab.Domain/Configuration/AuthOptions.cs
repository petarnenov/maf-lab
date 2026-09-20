namespace Maf.Lab.Domain.Configuration;

/// <summary>
/// The dev issuer's settings. It lives here, rather than beside the retrieval options, because every service that
/// validates or mints a token needs it — including ones that know nothing about retrieval.
/// </summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "maf-lab-dev-issuer";
    public string Audience { get; set; } = "maf-lab";
    /// <summary>HMAC key, at least 32 bytes. Dev only; override via environment in compose.</summary>
    public string SigningKey { get; set; } = "maf-lab-dev-signing-key-change-me-0123456789";
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);
}
