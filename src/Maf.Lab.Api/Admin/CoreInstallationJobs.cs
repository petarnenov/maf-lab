using Maf.Lab.Domain.Admin;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Admin;

public sealed class CoreInstallationJobs(AdminJobRunner runner) : IInstallationJobs
{
    // Preserve the installation's existing job keys across the extraction.
    private const string Scope = "_repository";
    public Task<AdminJob> StartAsync(string kind, Func<CancellationToken, Task<string>> work, CancellationToken ct) =>
        runner.StartAsync(Scope, kind, work, ct);
    public Task<AdminJob?> GetAsync(string jobId, CancellationToken ct) => runner.GetAsync(Scope, jobId, ct);
    public Task<AdminJobCancelResult> CancelAsync(string jobId, CancellationToken ct) => runner.CancelAsync(Scope, jobId, ct);
    public Task<AdminJob?> CurrentAsync(CancellationToken ct) => runner.CurrentAsync(Scope, ct);
}
