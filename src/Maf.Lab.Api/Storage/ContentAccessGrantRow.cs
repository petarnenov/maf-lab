namespace Maf.Lab.Api.Storage;

/// <summary>The durable, audited lifecycle of a session-bound operator content permission.</summary>
public sealed class ContentAccessGrantRow
{
    public long Id { get; set; }
    public required string SessionKey { get; set; }
    public required string OperatorId { get; set; }
    public required string TenantId { get; set; }
    public required string Reason { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndRequestedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
