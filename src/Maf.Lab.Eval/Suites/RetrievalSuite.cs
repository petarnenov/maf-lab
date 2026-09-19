using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Retrieval.Configuration;
using Maf.Lab.Retrieval.Search;

namespace Maf.Lab.Eval.Suites;

public sealed record RetrievalVariant(string Name, SearchSettings Settings, DocumentSearchService Search, bool Primary);

/// <summary>recall@5, recall@20 and MRR per retrieval configuration, over the tenant-scoped search path.</summary>
public sealed class RetrievalSuite
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, IReadOnlyList<RetrievalVariant> variants, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.Retrieval(ctx.DatasetRoot)).ToList();
        var results = new List<EvalVariantResult>();
        foreach (var variant in variants)
        {
            double r5 = 0, r20 = 0, mrr = 0;
            var failures = new List<EvalCaseFailure>();
            // Per language, so a suite passing overall cannot hide a language that retrieves nothing useful.
            var byLanguage = new Dictionary<string, (double Recall5, int Count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cases)
            {
                var ranked = (await variant.Search.RankAsync(EvalAgentHost.EvalPrincipal(c.FirmId), c.Query, null, 20, variant.Settings, ct))
                    .Select(r => r.Chunk.ChunkId).ToList();
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
            var n = Math.Max(1, cases.Count);
            var metrics = new Dictionary<string, double> { ["recall@5"] = r5 / n, ["recall@20"] = r20 / n, ["mrr"] = mrr / n };
            if (byLanguage.Count > 1)
            {
                foreach (var (language, totals) in byLanguage.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    metrics[$"recall@5:{language}"] = totals.Recall5 / Math.Max(1, totals.Count);
                }
            }
            // Thresholds gate the configured production variant; the others are for comparison.
            var thresholds = variant.Primary ? ctx.ThresholdsFor("retrieval") : new Dictionary<string, double>();
            results.Add(SuiteContext.Variant(variant.Name, metrics, thresholds, cases.Count, failures));
            var perLanguage = string.Join(" ", metrics.Where(m => m.Key.StartsWith("recall@5:", StringComparison.Ordinal))
                .Select(m => $"{m.Key}={m.Value:0.###}"));
            ctx.Progress($"retrieval {variant.Name}: recall@5={metrics["recall@5"]:0.###} recall@20={metrics["recall@20"]:0.###} mrr={metrics["mrr"]:0.###} {perLanguage}".TrimEnd());
        }
        return results;
    }

    public static IReadOnlyList<RetrievalVariant> DefaultVariants(DocumentSearchService search, string denseVector, bool rerank)
    {
        var list = new List<RetrievalVariant>
        {
            new("hybrid", new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, denseVector, false), search, true),
            new("hybrid-dbsf", new SearchSettings(RetrievalModes.Hybrid, FusionModes.Dbsf, denseVector, false), search, false),
            new("dense", new SearchSettings(RetrievalModes.Dense, FusionModes.Rrf, denseVector, false), search, false),
            new("sparse", new SearchSettings(RetrievalModes.Sparse, FusionModes.Rrf, denseVector, false), search, false),
        };
        if (rerank)
        {
            list.Add(new("hybrid+rerank", new SearchSettings(RetrievalModes.Hybrid, FusionModes.Rrf, denseVector, true), search, false));
        }
        return list;
    }
}
