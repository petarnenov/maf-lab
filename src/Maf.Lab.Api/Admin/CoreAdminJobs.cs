using Maf.Lab.Domain.Admin;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.Api.Admin;

/// <summary>
/// The core's side of <see cref="IAdminJobs"/> (extract-index-admin-plugin): the job runner, scoped to the request's
/// principal's tenant, so a plugin's jobs are keyed as the core's always were and the plugin never names a tenant.
/// </summary>
public sealed class CoreAdminJobs(AdminJobRunner runner, IPrincipalAccessor principals) : IAdminJobs
{
    private string Tenant => principals.Current.TenantId.Value;

    public Task<AdminJob> StartAsync(string kind, Func<IProgress<string>, CancellationToken, Task<string>> work, CancellationToken ct) =>
        runner.StartAsync(Tenant, kind, work, ct);

    public Task<AdminJob?> GetAsync(string jobId, CancellationToken ct) => runner.GetAsync(Tenant, jobId, ct);

    public Task<AdminJobCancelResult> CancelAsync(string jobId, CancellationToken ct) => runner.CancelAsync(Tenant, jobId, ct);

    public Task<AdminJob?> CurrentAsync(CancellationToken ct) => runner.CurrentAsync(Tenant, ct);

    public Task<IReadOnlyList<AdminJob>> OpenAsync(IReadOnlyCollection<string> kinds, CancellationToken ct) => runner.OpenAsync(kinds, ct);

    public Task CancelOpenAsync(IReadOnlyCollection<string> kinds, CancellationToken ct) => runner.CancelOpenAsync(kinds, ct);
}
