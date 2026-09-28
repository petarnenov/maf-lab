using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;

namespace Maf.Lab.Eval.Suites;

/// <summary>
/// End-to-end answers judged for faithfulness and relevance, plus whether expected sources were cited — and, beside the
/// rubric, Jev's own check of the same answer (add-jev-answer-check), read from the turn without a request of its own,
/// with how often the two agree.
/// </summary>
public sealed class GenerationSuite(EvalAgentHost host, RubricJudge judge)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.Generation(ctx.DatasetRoot)).ToList();
        double faithfulness = 0, relevance = 0, sourceRecall = 0;
        int jevChecked = 0, groundedAgree = 0, relevantAgree = 0;
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
            var check = turn.AnswerCheck;
            if (check is { Checked: true })
            {
                // The rubric passes a case at 0.75; Jev at its configured floors. Agreement is both passing or both not.
                jevChecked++;
                groundedAgree += (check.Grounded >= check.GroundedFloor) == (score.Faithfulness >= 0.75) ? 1 : 0;
                relevantAgree += (check.Relevant >= check.RelevantFloor) == (score.Relevance >= 0.75) ? 1 : 0;
            }
            var jev = Describe(check);
            faithfulness += score.Faithfulness;
            relevance += score.Relevance;
            sourceRecall += caseSourceRecall;
            if (score.Faithfulness < 0.75 || score.Relevance < 0.75 || caseSourceRecall < 1)
            {
                failures.Add(new EvalCaseFailure(c.Id, $"faithfulness={score.Faithfulness:0.##} relevance={score.Relevance:0.##} sourceRecall={caseSourceRecall:0.##} {jev}: {score.Reason}"));
            }
            ctx.Progress($"generation {i + 1}/{cases.Count} {c.Id}: f={score.Faithfulness:0.##} r={score.Relevance:0.##} src={caseSourceRecall:0.##} {jev}");
        }
        var n = Math.Max(1, cases.Count);
        var metrics = new Dictionary<string, double>
        {
            ["faithfulness"] = faithfulness / n,
            ["relevance"] = relevance / n,
            ["sourceRecall"] = sourceRecall / n,
            ["jevChecked"] = (double)jevChecked / n,
        };
        // Over the checked cases only, and absent when none was: a zero would read as total disagreement.
        if (jevChecked > 0)
        {
            metrics["jevGroundedAgreement"] = (double)groundedAgree / jevChecked;
            metrics["jevRelevantAgreement"] = (double)relevantAgree / jevChecked;
        }
        return [SuiteContext.Variant("agent", metrics, ctx.ThresholdsFor("generation"), cases.Count, failures)];
    }

    /// <summary>"jev=pass(r=0.93 g=0.88)", or "jev=unchecked(timed out after 3s)", or "jev=none" for a turn with no check.</summary>
    public static string Describe(AnswerCheck? check) => check switch
    {
        null => "jev=none",
        { Checked: false } => $"jev=unchecked({check.Reason})",
        _ => $"jev={check.Verdict}(r={check.Relevant:0.##} g={check.Grounded:0.##})",
    };
}
