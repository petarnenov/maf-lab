using Maf.Lab.Api.Admin;
using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Endpoints;

/// <summary>Index administration for a FIRM_ADMIN, scoped to the admin's firm plus the shared corpus.</summary>
public static class AdminIndexEndpoints
{
    public sealed record MigrateRequest(string? TargetModel);

    public static IEndpointRouteBuilder MapAdminIndex(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(AuthPolicies.FirmAdmin);

        admin.MapGet("/index/status", async (IPrincipalAccessor principals, TenantScopedMaintenance store, CollectionBootstrapper bootstrapper,
            IOptions<RetrievalOptions> retrieval, AdminJobRunner jobs, CancellationToken ct) =>
        {
            var principal = principals.Current;
            await bootstrapper.EnsureAsync(ct);
            var counts = new Dictionary<string, long>();
            foreach (var tenant in principal.ReadableTenants)
            {
                foreach (var v in await store.ModelVersionsAsync(tenant, ct))
                {
                    counts[v.ModelVersion] = counts.GetValueOrDefault(v.ModelVersion) + v.Chunks;
                }
            }
            return Results.Ok(new IndexStatus(counts.Select(c => new ModelVersionCount(c.Key, c.Value)).OrderBy(c => c.ModelVersion).ToList(),
                retrieval.Value.DenseVector, await jobs.CurrentAsync(principal.FirmId.Value, ct)));
        });

        admin.MapGet("/index/drift", async (IPrincipalAccessor principals, DriftService drift, CancellationToken ct) =>
            Results.Ok(await drift.ComputeAsync(Scope(principals.Current), ct)));

        admin.MapPost("/index/run", async (IPrincipalAccessor principals, IndexingPipeline pipeline, AdminJobRunner jobs, CancellationToken requestCt) =>
        {
            var principal = principals.Current;
            var job = await jobs.StartAsync(principal.FirmId.Value, "index", async ct =>
            {
                var summary = await pipeline.RunAsync(new IndexRequest { Tenants = Scope(principal) }, ct);
                return $"indexed {summary.DocumentsIndexed}, unchanged {summary.DocumentsUnchanged}, chunks written {summary.ChunksWritten}, " +
                       $"deleted {summary.ChunksDeleted}, rejected {summary.Rejected.Count}";
            }, requestCt);
            return Results.Accepted($"/api/admin/jobs/{job.JobId}", job);
        });

        admin.MapPost("/index/migrate", async (HttpContext http, IPrincipalAccessor principals, MigrationService migration, ModelProviders models,
            IOptions<RetrievalOptions> retrieval, AdminJobRunner jobs, CancellationToken requestCt) =>
        {
            var principal = principals.Current;
            var requested = await ReadTargetAsync(http);
            var target = ResolveTarget(requested, models, retrieval.Value.DenseVector);
            if (target is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["targetModel"] = ["Unknown target; use a configured dense vector name or model."] });
            }
            var job = await jobs.StartAsync(principal.FirmId.Value, "migrate", async ct =>
            {
                var summary = await migration.RunAsync(Scope(principal), target, 64, ct);
                return $"migrated {summary.Migrated} chunk(s) to {summary.TargetModelVersion}; {summary.AlreadyCurrent} already current";
            }, requestCt);
            return Results.Accepted($"/api/admin/jobs/{job.JobId}", job);
        });

        admin.MapGet("/jobs/{jobId}", async (string jobId, IPrincipalAccessor principals, AdminJobRunner jobs, CancellationToken ct) =>
            await jobs.GetAsync(principals.Current.FirmId.Value, jobId, ct) is { } job ? Results.Ok(job) : Results.NotFound());

        return app;
    }

    private static HashSet<TenantId> Scope(Principal principal) => principal.ReadableTenants.ToHashSet();

    private static async Task<string?> ReadTargetAsync(HttpContext http)
    {
        if (http.Request.ContentLength is null or 0)
        {
            return null;
        }
        try
        {
            var body = await System.Text.Json.JsonSerializer.DeserializeAsync<MigrateRequest>(http.Request.Body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            return body?.TargetModel;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? ResolveTarget(string? requested, ModelProviders models, string activeVector)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return models.Profiles.Keys.FirstOrDefault(k => k != activeVector);
        }
        if (models.Profiles.ContainsKey(requested))
        {
            return requested;
        }
        return models.Profiles.FirstOrDefault(p => string.Equals(p.Value.Model, requested, StringComparison.OrdinalIgnoreCase)).Key;
    }
}
