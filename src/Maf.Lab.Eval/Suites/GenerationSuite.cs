using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;

namespace Maf.Lab.Eval.Suites;

/// <summary>End-to-end answers judged for faithfulness and relevance, plus whether expected sources were cited.</summary>
public sealed class GenerationSuite(EvalAgentHost host, RubricJudge judge)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.Generation(ctx.DatasetRoot)).ToList();
        double faithfulness = 0, relevance = 0, sourceRecall = 0;
        var failures = new List<EvalCaseFailure>();
        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var turn = await host.AskAsync(c.FirmId, c.Question, ct);
            var context = string.Join("\n---\n", turn.Sources.Select(s => $"[{s.DocId} :: {s.SectionPath}]\n{s.Snippet}"));
            JudgeScore score;
            try
            {
                score = await judge.ScoreAsync(c.Question, c.ReferenceAnswer, context.Length == 0 ? "(no documents retrieved)" : context, turn.Answer, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                score = new JudgeScore(0, 0, $"judge failed: {ex.GetType().Name}");
            }
            var docs = turn.Sources.Select(s => s.DocId).ToHashSet();
            var caseSourceRecall = c.ExpectedDocIds.Count == 0 ? 1 : (double)c.ExpectedDocIds.Count(docs.Contains) / c.ExpectedDocIds.Count;
            faithfulness += score.Faithfulness;
            relevance += score.Relevance;
            sourceRecall += caseSourceRecall;
            if (score.Faithfulness < 0.75 || score.Relevance < 0.75 || caseSourceRecall < 1)
            {
                failures.Add(new EvalCaseFailure(c.Id, $"faithfulness={score.Faithfulness:0.##} relevance={score.Relevance:0.##} sourceRecall={caseSourceRecall:0.##}: {score.Reason}"));
            }
            ctx.Progress($"generation {i + 1}/{cases.Count} {c.Id}: f={score.Faithfulness:0.##} r={score.Relevance:0.##} src={caseSourceRecall:0.##}");
        }
        var n = Math.Max(1, cases.Count);
        var metrics = new Dictionary<string, double>
        {
            ["faithfulness"] = faithfulness / n,
            ["relevance"] = relevance / n,
            ["sourceRecall"] = sourceRecall / n,
        };
        return [SuiteContext.Variant("agent", metrics, ctx.ThresholdsFor("generation"), cases.Count, failures)];
    }
}
