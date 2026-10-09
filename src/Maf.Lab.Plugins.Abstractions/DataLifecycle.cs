using System.Text.Json;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>Lifecycle of stored data, including data of a plugin disabled for this tenant.</summary>
public interface IContributesDataLifecycle
{
    IDataLifecycle CreateDataLifecycle(IServiceProvider services);
}

/// <summary>
/// Store operations invoked by the authorized lifecycle job after legal-hold checks and writer quiescence.
/// Deletes and retention are idempotent; every operation observes the job's cancellation token.
/// Export records are streamed into a private bundle that is published only after every store completes.
/// </summary>
public interface IDataLifecycle
{
    IAsyncEnumerable<DataExportRecord> ExportAsync(DataLifecycleScope scope, CancellationToken ct);
    Task DeleteAsync(DataLifecycleScope scope, CancellationToken ct);
    Task ApplyRetentionAsync(DataRetentionPolicy policy, CancellationToken ct);
}

/// <summary>A stored tenant, optionally narrowed to one of its users; shared data cannot be erased this way.</summary>
public sealed record DataLifecycleScope
{
    public TenantId Tenant { get; }
    public string? UserId { get; }

    public DataLifecycleScope(TenantId owner, string? userId = null)
    {
        if (!TenantId.TryParse(owner.Value, out var parsed) || parsed.IsShared)
            throw new ArgumentException("A lifecycle scope requires a tenant.", nameof(owner));
        if (userId is not null && string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("A user scope requires a nonblank identifier.", nameof(userId));
        Tenant = parsed;
        UserId = userId;
    }
}

/// <summary>
/// A tenant's validated record schedule. Each store applies the cutoff to its own record family;
/// core conversation content is retained together by last activity. The orchestrator checks allowed ranges/holds.
/// </summary>
public sealed record DataRetentionPolicy
{
    public DataLifecycleScope Scope { get; }
    public DateTimeOffset RetainFrom { get; }

    public DataRetentionPolicy(TenantId owner, DateTimeOffset retainFrom)
    {
        Scope = new DataLifecycleScope(owner);
        RetainFrom = retainFrom;
    }
}

/// <summary>One self-contained JSON record; record types belong to the contributing store.</summary>
public sealed record DataExportRecord(string RecordType, JsonElement Data);
