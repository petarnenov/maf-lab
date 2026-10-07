using Maf.Lab.Domain.Admin;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Indexing.Pipeline;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.IndexAdmin;

/// <summary>A corpus the screen offers: its plugin's name, and whether it is built into a graph.</summary>
public sealed record CorpusView(string Name, bool HasGraph);

public sealed record IndexRunRequest(string? Corpus);

public sealed record MigrateRequest(string? Corpus, string? TargetModel);

/// <summary>
/// Index administration for a TENANT_ADMIN, scoped to the admin's tenant plus the shared corpus, over one of the corpora
/// the installed plugins declare (design D5, D9).
/// </summary>
public static class IndexAdminEndpoints
{
    /// <summary>Progress reported as it happens, on the reporting thread: the job keeps only the latest.</summary>
    private sealed class Reported<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(PolicyNames.TenantAdmin);

        admin.MapGet("/index/corpora", (OfferedCorpora corpora) =>
            Results.Ok(corpora.All().Select(c => new CorpusView(c.Plugin, !string.IsNullOrEmpty(c.Graph))).ToList()));

        admin.MapGet("/index/status", async (string? corpus, IPrincipalAccessor principals, OfferedCorpora corpora, CorpusIndexing indexing,
            IOptions<RetrievalOptions> retrieval, IAdminJobs jobs, CancellationToken ct) =>
        {
            var (chosen, error) = Pick(corpora, corpus);
            if (error is not null)
            {
                return error;
            }
            var services = indexing.For(chosen!);
            await services.Bootstrapper.EnsureAsync(ct);
            var counts = new Dictionary<string, long>();
            foreach (var tenant in principals.Current.ReadableTenants)
            {
                foreach (var v in await services.Store.ModelVersionsAsync(tenant, ct))
                {
                    counts[v.ModelVersion] = counts.GetValueOrDefault(v.ModelVersion) + v.Chunks;
                }
            }
            return Results.Ok(new IndexStatus(counts.Select(c => new ModelVersionCount(c.Key, c.Value)).OrderBy(c => c.ModelVersion).ToList(),
                retrieval.Value.DenseVector, await jobs.CurrentAsync(ct)));
        });

        admin.MapGet("/index/drift", async (string? corpus, IPrincipalAccessor principals, OfferedCorpora corpora, CorpusIndexing indexing,
            CancellationToken ct) =>
        {
            var (chosen, error) = Pick(corpora, corpus);
            return error ?? Results.Ok(await indexing.For(chosen!).Drift.ComputeAsync(Scope(principals.Current), ct));
        });

        admin.MapPost("/index/run", async (HttpContext http, IPrincipalAccessor principals, OfferedCorpora corpora, CorpusIndexing indexing,
            IAdminJobs jobs, CancellationToken requestCt) =>
        {
            var (chosen, error) = Pick(corpora, (await ReadAsync<IndexRunRequest>(http))?.Corpus);
            if (error is not null)
            {
                return error;
            }
            var corpus = chosen!;
            var pipeline = indexing.For(corpus).Pipeline;
            var scope = Scope(principals.Current);
            var job = await jobs.StartAsync(IndexAdminPlugin.IndexKind, async (progress, ct) =>
            {
                var summary = await pipeline.RunAsync(new IndexRequest
                {
                    Tenants = scope,
                    // How far it got, for a job that is stopped part-way.
                    Progress = new Reported<IndexProgress>(p => progress.Report(p.Total is { } total
                        ? $"{corpus.Plugin}: indexed {p.Done} of {total} documents"
                        : $"{corpus.Plugin}: {p.Stage}")),
                }, ct);
                return $"{corpus.Plugin}: indexed {summary.DocumentsIndexed}, unchanged {summary.DocumentsUnchanged}, " +
                       $"chunks written {summary.ChunksWritten}, deleted {summary.ChunksDeleted}, rejected {summary.Rejected.Count}";
            }, requestCt);
            return Results.Accepted($"/api/admin/jobs/{job.JobId}", job);
        });

        admin.MapPost("/index/migrate", async (HttpContext http, IPrincipalAccessor principals, OfferedCorpora corpora, CorpusIndexing indexing,
            ModelProviders models, IOptions<RetrievalOptions> retrieval, IAdminJobs jobs, CancellationToken requestCt) =>
        {
            var body = await ReadAsync<MigrateRequest>(http);
            var (chosen, error) = Pick(corpora, body?.Corpus);
            if (error is not null)
            {
                return error;
            }
            var target = ResolveTarget(body?.TargetModel, models, retrieval.Value.DenseVector);
            if (target is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["targetModel"] = ["Unknown target; use a configured dense vector name or model."] });
            }
            var corpus = chosen!;
            var migration = indexing.For(corpus).Migration;
            var scope = Scope(principals.Current);
            var job = await jobs.StartAsync(IndexAdminPlugin.MigrateKind, async (progress, ct) =>
            {
                var summary = await migration.RunAsync(scope, target, 64, ct, batch =>
                {
                    progress.Report($"{corpus.Plugin}: migrated {batch} batch(es)");
                    return Task.CompletedTask;
                });
                return $"{corpus.Plugin}: migrated {summary.Migrated} chunk(s) to {summary.TargetModelVersion}; {summary.AlreadyCurrent} already current";
            }, requestCt);
            return Results.Accepted($"/api/admin/jobs/{job.JobId}", job);
        });

        admin.MapGet("/jobs/{jobId}", async (string jobId, IAdminJobs jobs, CancellationToken ct) =>
            await jobs.GetAsync(jobId, ct) is { } job ? Results.Ok(job) : Results.NotFound());

        // Stops a running job of the admin's tenant (stop-anything), whichever replica runs it: 202 with the job while it
        // stops, 409 when it had already ended.
        admin.MapPost("/jobs/{jobId}/cancel", async (string jobId, IAdminJobs jobs, CancellationToken ct) =>
            (await jobs.CancelAsync(jobId, ct)).ToResult());
    }

    /// <summary>
    /// The corpus a request names, or the answer for one it cannot have, before anything runs: 404 for one no installed
    /// plugin offers, 400 naming the choices when none is named and several are offered.
    /// </summary>
    private static (PluginCorpus? Corpus, IResult? Error) Pick(OfferedCorpora corpora, string? name)
    {
        var (corpus, choice) = corpora.Resolve(name);
        return choice switch
        {
            CorpusChoice.Chosen => (corpus, null),
            CorpusChoice.Ambiguous => (null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["corpus"] = [$"Name a corpus: {string.Join(", ", corpora.All().Select(c => c.Plugin))}."],
            })),
            _ => (null, Results.NotFound()),
        };
    }

    private static HashSet<TenantId> Scope(Principal principal) => principal.ReadableTenants.ToHashSet();

    private static async Task<T?> ReadAsync<T>(HttpContext http) where T : class
    {
        if (http.Request.ContentLength is null or 0)
        {
            return null;
        }
        try
        {
            return await System.Text.Json.JsonSerializer.DeserializeAsync<T>(http.Request.Body,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
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
