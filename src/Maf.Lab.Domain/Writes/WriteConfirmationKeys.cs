namespace Maf.Lab.Domain.Writes;

/// <summary>
/// The wire keys of a write a person confirms (generalize-write-confirmation), shared by the MCP server that asks and the
/// client that answers, so neither names another's: the summary, state and expiry in the input request's <c>_meta</c>,
/// the caller's idempotency key in the confirmed call's <c>_meta</c>, and the key the tool's request for input declares
/// for the confirmation. MCP leaves a tool's own data to <c>_meta</c> under a vendor prefix.
/// </summary>
public static class WriteConfirmationKeys
{
    public const string Summary = "maf-lab/write-summary";
    public const string State = "maf-lab/write-state";
    public const string ExpiresAt = "maf-lab/write-expires-at";
    public const string IdempotencyKey = "maf-lab/idempotencyKey";
    public const string Confirmation = "confirmation";
}
