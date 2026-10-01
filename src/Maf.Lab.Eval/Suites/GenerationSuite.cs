using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Domain.Feedback;
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
    /// <summary>The rubric passes a case at this score, for faithfulness and relevance alike.</summary>
    public const double RubricPass = 0.75;

    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.Generation(ctx.DatasetRoot)).ToList();
        double faithfulness = 0, relevance = 0, sourceRecall = 0;
        var judged = new List<(TurnResult Turn, JudgeScore Score)>();
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
            judged.Add((turn, score));
            // A codebase source's DocId is its path, so repository paths are expected sources like document ids.
            var docs = turn.Sources.Select(s => s.DocId).ToHashSet();
            var caseSourceRecall = c.ExpectedDocIds.Count == 0 ? 1 : (double)c.ExpectedDocIds.Count(docs.Contains) / c.ExpectedDocIds.Count;
            var jev = Describe(turn.AnswerCheck);
            faithfulness += score.Faithfulness;
            relevance += score.Relevance;
            sourceRecall += caseSourceRecall;
            if (score.Faithfulness < RubricPass || score.Relevance < RubricPass || caseSourceRecall < 1)
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
        };
        foreach (var (key, value) in JevMetrics(judged, cases.Count))
        {
            metrics[key] = value;
        }
        return [SuiteContext.Variant("agent", metrics, ctx.ThresholdsFor("generation"), cases.Count, failures)];
    }

    /// <summary>
    /// Jev's answer check beside the rubric: the share of cases checked, the share of checked cases found uncertain, and —
    /// over the checked cases only, absent when none was, since a zero would read as total disagreement — how often each
    /// Jev question agrees with the rubric. Jev agrees with a rubric pass when it raised no signal for that question
    /// (<c>pass</c> or <c>uncertain</c>): the band is not a flag.
    /// </summary>
    public static Dictionary<string, double> JevMetrics(IReadOnlyList<(TurnResult Turn, JudgeScore Score)> cases, int total)
    {
        var n = Math.Max(1, total);
        var checkedCases = cases.Where(c => c.Turn.AnswerCheck is { Checked: true }).ToList();
        var metrics = new Dictionary<string, double> { ["jevChecked"] = (double)checkedCases.Count / n };
        if (checkedCases.Count == 0)
        {
            return metrics;
        }
        int groundedAgree = 0, relevantAgree = 0;
        foreach (var (turn, score) in checkedCases)
        {
            var signals = turn.AnswerCheck!.Signals.ToHashSet();
            groundedAgree += !signals.Contains(TurnSignal.AnswerNotGrounded) == (score.Faithfulness >= RubricPass) ? 1 : 0;
            relevantAgree += !signals.Contains(TurnSignal.AnswerNotRelevant) == (score.Relevance >= RubricPass) ? 1 : 0;
        }
        metrics["jevUncertain"] = (double)checkedCases.Count(c => c.Turn.AnswerCheck!.Verdict == AnswerVerdict.Uncertain) / checkedCases.Count;
        metrics["jevGroundedAgreement"] = (double)groundedAgree / checkedCases.Count;
        metrics["jevRelevantAgreement"] = (double)relevantAgree / checkedCases.Count;
        return metrics;
    }

    /// <summary>"jev=pass(r=0.93 g=0.88)", or "jev=unchecked(timed out after 3s)", or "jev=none" for a turn with no check.</summary>
    public static string Describe(AnswerCheck? check) => check switch
    {
        null => "jev=none",
        { Checked: false } => $"jev=unchecked({check.Reason})",
        _ => $"jev={check.Verdict}(r={check.Relevant:0.##} g={check.Grounded:0.##})",
    };
}
