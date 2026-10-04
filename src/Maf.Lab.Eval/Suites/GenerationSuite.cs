using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Eval.Judging;
using Maf.Lab.Retrieval.Jev;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace Maf.Lab.Eval.Suites;

/// <summary>A judge's pass reading of one answer: what Jev's answer check is compared with. Scores are 0–1.</summary>
public sealed record JudgeScore(double Faithfulness, double Relevance, string Reason);

/// <summary>
/// End-to-end answers graded by Jev (adopt-meai-evaluation): one request per case, per sentence, reference point and
/// source a yes/no, code counting — through a Microsoft.Extensions.AI.Evaluation evaluator whose results are kept and
/// rendered as an HTML report. Beside the grade, whether the expected sources were cited, and Jev's own answer check
/// read from the turn without a request of its own (add-jev-answer-check), with how often the two agree. The grade
/// replaced a one-request LLM rubric after a side-by-side comparison (DECISIONS.md §78).
/// </summary>
public sealed class GenerationSuite(EvalAgentHost host, ReportingConfiguration reporting)
{
    /// <summary>A case passes at this faithfulness, with relevance 1.</summary>
    public const double PassMark = JevGenerationEvaluator.PassMark;

    public const string ScenarioPrefix = "generation.";

    /// <summary>Input tokens the grade was charged for in the last run (jev-usage §4.6).</summary>
    public int InputTokens { get; private set; }

    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.Generation(ctx.DatasetRoot)).ToList();
        var graded = new List<Graded>();
        var failures = new List<EvalCaseFailure>();
        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var turn = await host.AskAsync(c.FirmId, c.Question, ct);
            var context = new JevGradeContext(turn.Read, c.ReferencePoints);
            await using (var run = await reporting.CreateScenarioRunAsync(ScenarioPrefix + c.Id, cancellationToken: ct))
            {
                await run.EvaluateAsync([new ChatMessage(ChatRole.User, c.Question)], new ChatResponse(new ChatMessage(ChatRole.Assistant, turn.Answer)),
                    [context], ct);
            }
            var outcome = context.Outcome!;
            InputTokens += outcome.InputTokens;
            // A codebase source's DocId is its path, so repository paths are expected sources like document ids.
            var docs = turn.Sources.Select(s => s.DocId).ToHashSet();
            var sourceRecall = c.ExpectedDocIds.Count == 0 ? 1 : (double)c.ExpectedDocIds.Count(docs.Contains) / c.ExpectedDocIds.Count;
            var g = outcome.Grade;
            var score = new JudgeScore(g?.Faithfulness ?? 0, g?.Relevance ?? 0, g?.Reason() ?? $"judge failed: {outcome.Failure}");
            graded.Add(new Graded(turn, outcome, score, sourceRecall));

            var jev = Describe(turn.AnswerCheck);
            var line = g is null
                ? $"judge failed: {outcome.Failure}"
                : $"f={g.Faithfulness:0.##} r={g.Relevance:0.##} c={g.Completeness:0.##} ag={g.ReferenceAgreement:0.##} ret={g.RetrievalJudged:0.##}{(outcome.Truncated ? " truncated" : "")}";
            if (g is null || score.Faithfulness < PassMark || score.Relevance < 1 || sourceRecall < 1)
            {
                failures.Add(new EvalCaseFailure(c.Id,
                    $"{line} src={sourceRecall:0.##} {jev}: {score.Reason}"));
            }
            ctx.Progress($"generation {i + 1}/{cases.Count} {c.Id}: {line} src={sourceRecall:0.##} {jev}");
        }
        return [SuiteContext.Variant("agent", Metrics(graded, cases.Count), ctx.ThresholdsFor("generation"), cases.Count, failures)];
    }

    private sealed record Graded(TurnResult Turn, GradeOutcome Outcome, JudgeScore Score, double SourceRecall);

    /// <summary>
    /// Means over the cases; a failed grade counts 0 on every judged metric. <c>judgeUncertain</c> is over the graded
    /// cases.
    /// </summary>
    private static Dictionary<string, double> Metrics(List<Graded> graded, int total)
    {
        var n = Math.Max(1, total);
        double Mean(Func<Graded, double> f) => graded.Sum(f) / n;
        var ok = graded.Where(x => x.Outcome.Grade is not null).ToList();
        var metrics = new Dictionary<string, double>
        {
            [JevGenerationEvaluator.Faithfulness] = Mean(x => x.Outcome.Grade?.Faithfulness ?? 0),
            [JevGenerationEvaluator.Relevance] = Mean(x => x.Outcome.Grade?.Relevance ?? 0),
            [JevGenerationEvaluator.Completeness] = Mean(x => x.Outcome.Grade?.Completeness ?? 0),
            [JevGenerationEvaluator.ReferenceAgreement] = Mean(x => x.Outcome.Grade?.ReferenceAgreement ?? 0),
            [JevGenerationEvaluator.RetrievalJudged] = Mean(x => x.Outcome.Grade?.RetrievalJudged ?? 0),
            [JevGenerationEvaluator.JudgeUncertain] = ok.Count == 0 ? 0 : ok.Average(x => x.Outcome.Grade!.Uncertain),
            ["sourceRecall"] = Mean(x => x.SourceRecall),
        };
        foreach (var (key, value) in JevMetrics([.. graded.Select(x => (x.Turn, x.Score))], total))
        {
            metrics[key] = value;
        }
        return metrics;
    }

    /// <summary>The suite refuses to run without the key rather than report a judge that graded nothing.</summary>
    public static void RequireKey(JevGrader grader)
    {
        if (!grader.IsConfigured)
        {
            throw new InvalidOperationException($"The generation suite is graded by Jev and needs {JevCredential.EnvironmentVariable} in the environment.");
        }
    }

    /// <summary>
    /// Jev's answer check beside the grade: the share of cases checked, the share of checked cases found uncertain, and —
    /// over the checked cases only, absent when none was, since a zero would read as total disagreement — how often each
    /// check question agrees with the grade. The check agrees with a grade pass when it raised no signal for that
    /// question (<c>pass</c> or <c>uncertain</c>): the band is not a flag.
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
            groundedAgree += !signals.Contains(TurnSignal.AnswerNotGrounded) == (score.Faithfulness >= PassMark) ? 1 : 0;
            relevantAgree += !signals.Contains(TurnSignal.AnswerNotRelevant) == (score.Relevance >= PassMark) ? 1 : 0;
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
