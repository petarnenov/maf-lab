using Maf.Lab.Domain.Admin;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>Repository-wide jobs in the core's existing installation scope, separate from tenant admin jobs.</summary>
public interface IInstallationJobs
{
    Task<AdminJob> StartAsync(string kind, Func<CancellationToken, Task<string>> work, CancellationToken ct);
    Task<AdminJob?> GetAsync(string jobId, CancellationToken ct);
    Task<AdminJobCancelResult> CancelAsync(string jobId, CancellationToken ct);
    Task<AdminJob?> CurrentAsync(CancellationToken ct);
}

/// <summary>Identifiers from background system work, recorded by the core under its shared system identity.</summary>
public interface ISystemAudit
{
    Task RecordAsync(string kind, string operation, string arguments, string outcome, long durationMs, CancellationToken ct);
}
