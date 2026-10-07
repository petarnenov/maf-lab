using Maf.Lab.Domain.Admin;
using Microsoft.AspNetCore.Http;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>
/// The core's admin job store, as a plugin starts, reads and stops its long work there (extract-index-admin-plugin):
/// a job's state lives in the shared store, so any replica reports it and takes its stop (stop-anything), and at most one
/// job per tenant and kind runs at a time. The tenant is the request's principal's, read by the core; no method takes one.
/// </summary>
public interface IAdminJobs
{
    /// <summary>
    /// Starts a job of this kind for the caller's tenant, or returns the one already running. The work reports how far it
    /// has got, so a stopped job can say so; the summary it returns is shown to the administrator.
    /// </summary>
    Task<AdminJob> StartAsync(string kind, Func<IProgress<string>, CancellationToken, Task<string>> work, CancellationToken ct);

    /// <summary>A job of the caller's tenant, or null.</summary>
    Task<AdminJob?> GetAsync(string jobId, CancellationToken ct);

    /// <summary>Stops a running job of the caller's tenant through the store, whichever replica runs it.</summary>
    Task<AdminJobCancelResult> CancelAsync(string jobId, CancellationToken ct);

    /// <summary>The caller's tenant's latest job, of any kind, or null.</summary>
    Task<AdminJob?> CurrentAsync(CancellationToken ct);

    /// <summary>
    /// The running jobs of these kinds across every tenant. Installation-wide, for <see cref="IContributesOpenWork"/>
    /// only: plugin-off has no request and so no principal. Never call it from a request path.
    /// </summary>
    Task<IReadOnlyList<AdminJob>> OpenAsync(IReadOnlyCollection<string> kinds, CancellationToken ct);

    /// <summary>
    /// Cancels the running jobs of these kinds across every tenant, through the store. Installation-wide, for
    /// <see cref="IContributesOpenWork"/> only; never call it from a request path.
    /// </summary>
    Task CancelOpenAsync(IReadOnlyCollection<string> kinds, CancellationToken ct);
}

/// <summary>What a cancel found: the job stopping, a job that had already ended, or none at all.</summary>
public enum AdminJobCancel
{
    Canceling,
    AlreadyEnded,
    NotFound,
}

/// <summary>A cancel's outcome and the job, and what a route says back for it: 202 while it stops, 409 once ended, 404.</summary>
public sealed record AdminJobCancelResult(AdminJobCancel Outcome, AdminJob? Job)
{
    public IResult ToResult() => Outcome switch
    {
        AdminJobCancel.Canceling => Results.Accepted(null, Job),
        AdminJobCancel.AlreadyEnded => Results.Conflict(Job),
        _ => Results.NotFound(),
    };
}
