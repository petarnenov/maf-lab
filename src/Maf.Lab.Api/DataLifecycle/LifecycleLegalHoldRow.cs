using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Api.DataLifecycle;

internal sealed class LifecycleLegalHoldRow
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
    public required string Name { get; set; }
    public string? RecordType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}

internal sealed record LifecycleLegalHoldSnapshot(string Id, TenantId Tenant, string Name, string? RecordType,
    DateTimeOffset CreatedAt, DateTimeOffset? ReleasedAt);

internal sealed class LifecycleLegalHoldConflictException()
    : InvalidOperationException("A destructive lifecycle job must stop and unwind before a legal hold can take effect.");
