using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Search;

namespace Maf.Lab.Eval.Suites;

public sealed record RetrievalVariant(string Name, SearchSettings Settings, DocumentSearchService Search, bool Primary);

/// <summary>
/// recall@5, recall@20 and MRR per retrieval configuration, over the tenant-scoped search path, plus how often a
/// question this corpus cannot answer correctly retrieved nothing. The two are reported apart and never averaged:
/// a floor that silences everything would look excellent on one and ruinous on the other, and a single number
/// would hide which.
/// </summary>
public sealed class RetrievalSuite
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, IReadOnlyList<RetrievalVariant> variants, CancellationToken ct)
    {
        var all = ctx.Take(DatasetLoader.Retrieval(ctx.DatasetRoot)).ToList();
        var cases = all.Where(c => !c.OffDomain).ToList();
        var offDomain = all.Where(c => c.OffDomain).ToList();
        var results = new List<EvalVariantResult>();
        foreach (var variant in variants)
        {
            double r5 = 0, r20 = 0, mrr = 0, silent = 0;
            var failures = new List<EvalCaseFailure>();
            var judge = new JudgeTally();
            // Per language, so a suite passing overall cannot hide a language that retrieves nothing useful.
            var byLanguage = new Dictionary<string, (double Recall5, int Count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cases)
            {
                var diagnostics = new SearchDiagnostics { Branches = false };
                var ranked = (await variant.Search.RankAsync(EvalAgentHost.EvalPrincipal(c.FirmId), c.Query, null, 20, variant.Settings, ct, diagnostics))
                    .Select(r => r.Chunk.ChunkId).ToList();
                judge.Add(c.Id, diagnostics.Relevance, offDomain: false);
                var relevant = c.RelevantChunkIds.ToHashSet();
                var caseR5 = Metrics.RecallAtK(ranked, relevant, 5);
                r5 += caseR5;
                r20 += Metrics.RecallAtK(ranked, relevant, 20);
                mrr += Metrics.ReciprocalRank(ranked, relevant);
                var language = string.IsNullOrWhiteSpace(c.Language) ? ctx.CorpusLanguage : c.Language;
                var seen = byLanguage.GetValueOrDefault(language);
                byLanguage[language] = (seen.Recall5 + caseR5, seen.Count + 1);
                if (caseR5 < 1)
                {
                    failures.Add(new EvalCaseFailure(c.Id, $"recall@5={caseR5:0.##}; top: {string.Join(", ", ranked.Take(3))}"));
                }
            }
            // A question the corpus cannot answer: retrieving nothing is the right answer, and anything else is a
            // handful of unrelated chunks that will be cited under "we have no documentation on that".
            foreach (var c in offDomain)
            {
                var diagnostics = new SearchDiagnostics { Branches = false };
                var ranked = await variant.Search.RankAsync(EvalAgentHost.EvalPrincipal(c.FirmId), c.Query, null, 20, variant.Settings, ct, diagnostics);
                judge.Add(c.Id, diagnostics.Relevance, offDomain: true);
                if (ranked.Count == 0)
                {
                    silent++;
                }
                else
                {
                    failures.Add(new EvalCaseFailure(c.Id, $"off-domain but retrieved {ranked.Count}; top: {string.Join(", ", ranked.Take(3).Select(r => r.Chunk.ChunkId))}"));
                }
            }
            var n = Math.Max(1, cases.Count);
            var metrics = new Dictionary<string, double> { ["recall@5"] = r5 / n, ["recall@20"] = r20 / n, ["mrr"] = mrr / n };
            if (offDomain.Count > 0)
            {
                metrics["offDomainSilence"] = silent / offDomain.Count;
            }
            if (byLanguage.Count > 1)
            {
                foreach (var (language, totals) in byLanguage.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    metrics[$"recall@5:{language}"] = totals.Recall5 / Math.Max(1, totals.Count);
                }
            }
            // Thresholds gate the configured production variant; the others are for comparison.
            var thresholds = variant.Primary ? ctx.ThresholdsFor("retrieval") : new Dictionary<string, double>();
            results.Add(SuiteContext.Variant(variant.Name, metrics, thresholds, all.Count, failures));
            var perLanguage = string.Join(" ", metrics.Where(m => m.Key.StartsWith("recall@5:", StringComparison.Ordinal))
                .Select(m => $"{m.Key}={m.Value:0.###}"));
            var silence = metrics.TryGetValue("offDomainSilence", out var q) ? $" off-domain-silence={q:0.###} ({offDomain.Count})" : "";
            ctx.Progress($"retrieval {variant.Name}: recall@5={metrics["recall@5"]:0.###} recall@20={metrics["recall@20"]:0.###} mrr={metrics["mrr"]:0.###}{silence} {perLanguage}".TrimEnd());
            foreach (var line in judge.Describe(variant.Name))
            {
                ctx.Progress(line);
            }
        }
        return results;
    }

    /// <summary>
    /// What the relevance judge did over one variant's run, kept out of the metrics on purpose: a judge that timed out
    /// or was rejected leaves the search ungated, so an outage would otherwise read as a quality result. Also the margin
    /// the floor sits in — the highest off-domain maximum against the lowest in-domain ones.
    /// </summary>
    private sealed class JudgeTally
    {
        private readonly List<(string Id, double Max, bool OffDomain)> _answered = [];
        private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);
        private readonly List<double> _durations = [];

        public void Add(string caseId, System.Text.Json.Nodes.JsonObject? relevance, bool offDomain)
        {
            if (relevance is null)
            {
                return;
            }
            _durations.Add(relevance["durationMs"]?.GetValue<double>() ?? 0);
            if (relevance["reason"]?.GetValue<string>() is { } reason)
            {
                // "timed out after 2s" and "rejected (503)" are what an outage looks like; count them by kind.
                var kind = reason.StartsWith("timed out", StringComparison.Ordinal) ? "timeout" : reason;
                _failures[kind] = _failures.GetValueOrDefault(kind) + 1;
                return;
            }
            if (relevance["max"]?.GetValue<double>() is { } max)
            {
                _answered.Add((caseId, max, offDomain));
            }
        }

        public IEnumerable<string> Describe(string variant)
        {
            if (_durations.Count == 0)
            {
                yield break;
            }
            var sorted = _durations.Order().ToList();
            var failed = _failures.Count == 0 ? "none" : string.Join(", ", _failures.Select(f => $"{f.Key} ×{f.Value}"));
            yield return $"retrieval {variant}: relevance judge {sorted.Count} request(s), p50 {sorted[sorted.Count / 2]:0} ms, max {sorted[^1]:0} ms; failures: {failed}";
            var off = _answered.Where(a => a.OffDomain).OrderByDescending(a => a.Max).ToList();
            var inDomain = _answered.Where(a => !a.OffDomain).OrderBy(a => a.Max).ToList();
            if (off.Count > 0 && inDomain.Count > 0)
            {
                yield return $"retrieval {variant}: relevance max — off-domain highest {string.Join(", ", off.Take(3).Select(a => $"{a.Id}={a.Max:0.00}"))}; " +
                    $"in-domain lowest {string.Join(", ", inDomain.Take(6).Select(a => $"{a.Id}={a.Max:0.00}"))}";
            }
        }
    }

    /// <summary>
    /// The variants a plain run compares. The floors come from configuration so an ordinary run measures what
    /// production does; a calibration sweep overrides them per run.
    /// </summary>
    public static IReadOnlyList<RetrievalVariant> DefaultVariants(DocumentSearchService search, string denseVector, bool rerank,
        float? denseFloor = null, float? sparseFloor = null, bool relevanceGate = false, IReadOnlyList<string>? rerankers = null,
        string? productionReranker = null)
    {
        // "hybrid" is what production does: its floors, its gate and, when it reranks, its reranker.
        var hybrid = new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, denseVector, productionReranker is not null, denseFloor, sparseFloor,
            relevanceGate, productionReranker);
        var list = new List<RetrievalVariant>
        {
            new("hybrid", hybrid, search, true),
            // The gate the other way round from production, so every run shows what it changes.
            new(relevanceGate ? "hybrid-nogate" : "hybrid+gate", hybrid with { RelevanceGate = !relevanceGate }, search, false),
            new("hybrid-dbsf", new SearchSettings(RetrievalModes.Hybrid, FusionModes.Dbsf, denseVector, false, denseFloor, sparseFloor), search, false),
            new("dense", new SearchSettings(RetrievalModes.Dense, FusionModes.Rrf, denseVector, false, denseFloor, sparseFloor), search, false),
            new("sparse", new SearchSettings(RetrievalModes.Sparse, FusionModes.Rrf, denseVector, false, denseFloor, sparseFloor), search, false),
        };
        if (productionReranker is not null)
        {
            // What the reranker adds, measured every run.
            list.Add(new("hybrid-norerank", hybrid with { Rerank = false, Reranker = null }, search, false));
        }
        if (rerank)
        {
            // One variant per reranker, gated as production is, so each compares directly with "hybrid".
            foreach (var kind in rerankers ?? [RerankerKinds.Llm, RerankerKinds.Jev])
            {
                list.Add(new($"hybrid+rerank-{kind}", hybrid with { Rerank = true, Reranker = kind }, search, false));
            }
        }
        return list;
    }
}
