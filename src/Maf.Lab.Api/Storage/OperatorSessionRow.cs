namespace Maf.Lab.Api.Storage;

/// <summary>Core deduplication receipt, committed in the same transaction as the operator's chained audit entry.</summary>
public sealed class OperatorSessionRow
{
    public required string SessionKey { get; set; }
    public DateTime EnteredAt { get; set; }
}
