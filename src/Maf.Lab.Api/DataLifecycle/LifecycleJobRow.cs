using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Api.DataLifecycle;

internal enum LifecycleOperation { UserDeletion, TenantExport, TenantOffboard, Retention }
internal enum LifecycleJobState { Queued, Running, Stopping, Stopped, Failed, Succeeded }

/// <summary>Durable immutable input plus worker-owned progress. No heartbeat expiry can release a destructive job.</summary>
internal sealed class LifecycleJobRow
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
    public string? UserId { get; set; }
    public LifecycleOperation Operation { get; set; }
    public DateTimeOffset? RetainFrom { get; set; }
    public required string ParticipantsJson { get; set; }
    public int CompletedParticipants { get; set; }
    public LifecycleJobState State { get; set; }
    public string? AttemptId { get; set; }
    public int ExportGeneration { get; set; }
}

internal sealed record LifecycleJobSnapshot(string Id, TenantId Tenant, string? UserId, LifecycleOperation Operation,
    DateTimeOffset? RetainFrom, IReadOnlyList<string> Participants, int CompletedParticipants,
    LifecycleJobState State, string? AttemptId, int ExportGeneration);
