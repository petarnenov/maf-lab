using System.Diagnostics;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Hosting.Cli;
using Maf.Lab.Retrieval.Graph;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Eval.Suites;

/// <summary>One domain's two searches over the same copied chunks: Qdrant's, and the spike's Neo4j one.</summary>
public sealed record BackendDomain(string Domain, IServiceProvider Services);

/// <summary>Times each call of the store's chunk search alone, so latency compares the stores, not the embedder or the judge.</summary>
public sealed class TimedChunkSearch(IChunkSearch inner) : IChunkSearch
{
    public List<double> Milliseconds { get; } = [];

    public async Task<IReadOnlyList<ScoredChunk>> QueryAsync(Principal principal, SearchRequest request, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var result = await inner.QueryAsync(principal, request, ct);
        Milliseconds.Add(watch.Elapsed.TotalMilliseconds);
        return result;
    }
}

/// <summary>
/// The retrieval spike's comparison (neo4j-retrieval-spike): every retrieval case on Qdrant and on Neo4j, over the same
/// chunks, the same encoders, the same production settings. Reports per backend and domain recall@5/@20, MRR, off-domain
/// silence and recall@5 per language, store latency p50/p95, and for Neo4j the share of its top five Qdrant also ranks
/// there. A comparison: never gated, never baselined. It refuses to score over a copy that does not match its collection.
/// </summary>
public sealed class RetrievalBackendsSuite
{
    public const string Name = "retrieval-backends";

    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, IReadOnlyList<BackendDomain> domains, CancellationToken ct)
    {
        foreach (var d in domains)
        {
            var qdrant = await d.Services.GetRequiredService<TenantScopedMaintenance>().CountAsync(ct);
            var maintenance = d.Services.GetRequiredService<TenantScopedMaintenance>();
            var neo4j = await d.Services.GetRequiredService<TenantScopedGraphMaintenance>().CountRetrievalChunksAsync(maintenance.Collection, ct);
            if (CopyMismatch(maintenance.Collection, qdrant, neo4j) is { } reason)
            {
                throw new InvalidOperationException(reason);
            }
        }
        var dataset = ctx.Take(DatasetLoader.Retrieval(ctx.DatasetRoot)).ToList();
        var total = domains.Sum(d => dataset.Count(c => c.Domain == d.Domain)) * 2;
        using var bar = new ConsoleProgress(Name);
        bar.SetTotal(total);
        var results = new List<EvalVariantResult>();
        try
        {
            foreach (var d in domains)
            {
                var cases = dataset.Where(c => c.Domain == d.Domain).ToList();
                var collection = d.Services.GetRequiredService<TenantScopedMaintenance>().Collection;
                var qdrantSearch = new TimedChunkSearch(d.Services.GetRequiredService<TenantScopedSearch>());
                var graphSearch = new TimedChunkSearch(new GraphChunkSearch(d.Services.GetRequiredService<IGraphReader>(), collection));
                var qdrant = ActivatorUtilities.CreateInstance<DocumentSearchService>(d.Services, (IChunkSearch)qdrantSearch);
                var graph = ActivatorUtilities.CreateInstance<DocumentSearchService>(d.Services, (IChunkSearch)graphSearch);
                var settings = qdrant.DefaultSettings;

                bar.Step($"{d.Domain} qdrant");
                var qdrantRanked = await RankAllAsync(qdrant, cases, settings, bar, ct);
                bar.Step($"{d.Domain} neo4j");
                var graphRanked = await RankAllAsync(graph, cases, settings, bar, ct);

                var qm = Score(cases, qdrantRanked, ctx.CorpusLanguage, out var qFailures);
                Latency(qm, qdrantSearch.Milliseconds);
                var gm = Score(cases, graphRanked, ctx.CorpusLanguage, out var gFailures);
                Latency(gm, graphSearch.Milliseconds);
                if (Overlap(qdrantRanked, graphRanked, 5) is { } overlap)
                {
                    gm["overlap@5"] = overlap;
                }
                ctx.Progress($"{Name} {d.Domain}: qdrant recall@5={qm["recall@5"]:0.###} mrr={qm["mrr"]:0.###} p95={qm["latencyP95Ms"]:0.#}ms · " +
                    $"neo4j recall@5={gm["recall@5"]:0.###} mrr={gm["mrr"]:0.###} p95={gm["latencyP95Ms"]:0.#}ms overlap@5={gm.GetValueOrDefault("overlap@5"):0.###}");
                // No thresholds: a comparison suite reports side by side and never gates.
                results.Add(SuiteContext.Variant($"qdrant-{d.Domain}", qm, new Dictionary<string, double>(), cases.Count, qFailures));
                results.Add(SuiteContext.Variant($"neo4j-{d.Domain}", gm, new Dictionary<string, double>(), cases.Count, gFailures));
            }
            bar.Succeed($"{total} case runs");
            return results;
        }
        catch (OperationCanceledException)
        {
            bar.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            bar.Fail(ex.GetType().Name);
            throw;
        }
    }

    private static async Task<IReadOnlyList<IReadOnlyList<string>>> RankAllAsync(DocumentSearchService search, IReadOnlyList<RetrievalCase> cases,
        SearchSettings settings, ConsoleProgress bar, CancellationToken ct)
    {
        var ranked = new List<IReadOnlyList<string>>();
        foreach (var c in cases)
        {
            bar.Working(c.Id);
            var diagnostics = new SearchDiagnostics { Branches = false };
            var hits = await search.RankAsync(EvalAgentHost.EvalPrincipal(c.FirmId), c.Query, null, 20, settings, ct, diagnostics);
            ranked.Add([.. hits.Select(h => h.Chunk.ChunkId)]);
            bar.Advance();
        }
        return ranked;
    }

    /// <summary>Why a copy cannot be compared with its collection, or null when the counts agree.</summary>
    public static string? CopyMismatch(string collection, long qdrant, long neo4j) =>
        qdrant == neo4j ? null
        : $"retrieval-backends: the Neo4j copy of {collection} holds {neo4j} chunks and Qdrant {qdrant}; run make neo4j-chunks first.";

    /// <summary>The same metrics the retrieval suite reports, over one backend's ranked lists.</summary>
    public static Dictionary<string, double> Score(IReadOnlyList<RetrievalCase> cases, IReadOnlyList<IReadOnlyList<string>> ranked, string corpusLanguage,
        out List<EvalCaseFailure> failures)
    {
        failures = [];
        double r5 = 0, r20 = 0, mrr = 0, silent = 0;
        int answerable = 0, offDomain = 0;
        var byLanguage = new Dictionary<string, (double Recall5, int Count)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            if (c.OffDomain)
            {
                offDomain++;
                if (ranked[i].Count == 0)
                {
                    silent++;
                }
                else
                {
                    failures.Add(new EvalCaseFailure(c.Id, $"off-domain but retrieved {ranked[i].Count}"));
                }
                continue;
            }
            answerable++;
            var relevant = c.RelevantChunkIds.ToHashSet();
            var caseR5 = Metrics.RecallAtK(ranked[i], relevant, 5);
            r5 += caseR5;
            r20 += Metrics.RecallAtK(ranked[i], relevant, 20);
            mrr += Metrics.ReciprocalRank(ranked[i], relevant);
            var language = string.IsNullOrWhiteSpace(c.Language) ? corpusLanguage : c.Language;
            var seen = byLanguage.GetValueOrDefault(language);
            byLanguage[language] = (seen.Recall5 + caseR5, seen.Count + 1);
            if (caseR5 < 1)
            {
                failures.Add(new EvalCaseFailure(c.Id, $"recall@5={caseR5:0.##}; top: {string.Join(", ", ranked[i].Take(3))}"));
            }
        }
        var n = Math.Max(1, answerable);
        var metrics = new Dictionary<string, double> { ["recall@5"] = r5 / n, ["recall@20"] = r20 / n, ["mrr"] = mrr / n };
        if (offDomain > 0)
        {
            metrics["offDomainSilence"] = silent / offDomain;
        }
        foreach (var (language, totals) in byLanguage)
        {
            metrics[$"recall@5:{language}"] = totals.Recall5 / Math.Max(1, totals.Count);
        }
        return metrics;
    }

    /// <summary>Mean share of a case's top <paramref name="k"/> on Neo4j that Qdrant also ranks in its top k; null with no comparable case.</summary>
    public static double? Overlap(IReadOnlyList<IReadOnlyList<string>> qdrant, IReadOnlyList<IReadOnlyList<string>> neo4j, int k)
    {
        var shares = new List<double>();
        for (var i = 0; i < Math.Min(qdrant.Count, neo4j.Count); i++)
        {
            var mine = neo4j[i].Take(k).ToList();
            if (mine.Count == 0)
            {
                continue;
            }
            var theirs = qdrant[i].Take(k).ToHashSet(StringComparer.Ordinal);
            shares.Add((double)mine.Count(theirs.Contains) / mine.Count);
        }
        return shares.Count == 0 ? null : shares.Average();
    }

    private static void Latency(Dictionary<string, double> metrics, IReadOnlyList<double> ms)
    {
        metrics["latencyP50Ms"] = GraphDepthSuite.Percentile(ms, 0.50);
        metrics["latencyP95Ms"] = GraphDepthSuite.Percentile(ms, 0.95);
    }
}
