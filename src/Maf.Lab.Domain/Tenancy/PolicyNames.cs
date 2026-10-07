namespace Maf.Lab.Domain.Tenancy;

/// <summary>
/// The names of the authorization policies the core defines, so a plugin's routes can require them without
/// referencing the api (extract-compliance-plugin). What each policy checks is the core's alone.
/// </summary>
public static class PolicyNames
{
    /// <summary>A tenant administrator of the caller's own tenant.</summary>
    public const string TenantAdmin = "tenant-admin";
}
