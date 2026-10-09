namespace Maf.Lab.Domain.Tenancy;

/// <summary>Own-store content permission, separate from the issuer's identity and ordinary ownership policies.</summary>
public interface IOperatorContentAccess
{
    Task<bool> MayReadAsync(Principal principal, CancellationToken ct);
}

/// <summary>Server endpoint metadata for configuration/counts; it never comes from a token or request.</summary>
public sealed record OperatorConfigurationAccess
{
    public static readonly OperatorConfigurationAccess Instance = new();
}
