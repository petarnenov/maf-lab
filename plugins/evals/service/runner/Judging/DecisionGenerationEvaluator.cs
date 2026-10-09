using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace Maf.Lab.Eval.Judging;

/// <summary>
/// What the grade needs beyond the question and the answer: what the turn read and the reference points. The evaluator
/// leaves its outcome here too, so the suite reads the typed grade (the sentences and points behind each number)
/// without parsing it back out of the metrics.
/// </summary>
public sealed class DecisionGradeContext(IReadOnlyList<Api.Agent.Decisions.ReadItem> read, IReadOnlyList<string> referencePoints,
    string previousQuestion = "", IReadOnlyList<Api.Agent.Decisions.ReadItem>? previousRead = null)
    : EvaluationContext(ContextName, $"{read.Count} source(s), {referencePoints.Count} reference point(s)")
{
    public const string ContextName = "Jev grade input";

    public IReadOnlyList<Api.Agent.Decisions.ReadItem> Read { get; } = read;

    public IReadOnlyList<string> ReferencePoints { get; } = referencePoints;

    public string PreviousQuestion { get; } = previousQuestion;

    public IReadOnlyList<Api.Agent.Decisions.ReadItem> PreviousRead { get; } = previousRead ?? [];

    public GradeOutcome? Outcome { get; internal set; }
}

/// <summary>
/// The Jev grade as a Microsoft.Extensions.AI.Evaluation evaluator (adopt-meai-evaluation, design D5): one Jev request,
/// one <see cref="NumericMetric"/> per metric, the reason and the sentences behind it as diagnostics, and every
/// probability as metadata. Not AI-based in the library's sense — it needs no <see cref="ChatConfiguration"/>.
/// </summary>
public sealed class DecisionGenerationEvaluator(DecisionGrader grader) : IEvaluator
{
    public const string Faithfulness = "faithfulness";
    public const string Relevance = "relevance";
    public const string Completeness = "completeness";
    public const string ReferenceAgreement = "referenceAgreement";
    public const string RetrievalJudged = "retrievalJudged";
    public const string JudgeUncertain = "judgeUncertain";

    /// <summary>A case passes at this faithfulness, with relevance 1 (spec: Metrics).</summary>
    public const double PassMark = 0.75;

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } =
        [Faithfulness, Relevance, Completeness, ReferenceAgreement, RetrievalJudged, JudgeUncertain];

    public async ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var context = additionalContext?.OfType<DecisionGradeContext>().FirstOrDefault()
            ?? throw new ArgumentException($"The Jev grade needs a {nameof(DecisionGradeContext)}.", nameof(additionalContext));
        var question = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        var outcome = await grader.GradeAsync(new GradeInput(question, modelResponse.Text, context.Read, context.ReferencePoints,
            context.PreviousQuestion, context.PreviousRead), cancellationToken);
        context.Outcome = outcome;
        return ToResult(outcome);
    }

    /// <summary>
    /// A failed request scores 0 on every judged metric and says why, so a broken judge cannot pass (spec: Metrics). A
    /// case with no reference points reports no completeness or agreement rather than a made-up 1.
    /// </summary>
    public static EvaluationResult ToResult(GradeOutcome outcome)
    {
        var result = new EvaluationResult();
        if (outcome.Grade is not { } g)
        {
            var reason = $"judge failed: {outcome.Failure}";
            foreach (var name in new[] { Faithfulness, Relevance, Completeness, ReferenceAgreement, RetrievalJudged })
            {
                var failed = new NumericMetric(name, 0, reason)
                {
                    Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Unacceptable, failed: true, reason),
                };
                failed.AddDiagnostics(EvaluationDiagnostic.Error(reason));
                result.Metrics[name] = failed;
            }
            Describe(result, outcome);
            return result;
        }

        result.Metrics[Faithfulness] = Metric(Faithfulness, g.Faithfulness, g.Faithfulness >= PassMark,
            g.Unsupported.Count == 0 ? "every claim is supported" : $"{g.Unsupported.Count} claim sentence(s) unsupported",
            g.Unsupported.Select(s => $"unsupported: {s}"));
        result.Metrics[Relevance] = Metric(Relevance, g.Relevance, g.Relevance >= 1,
            g.Relevance >= 1 ? "addresses the question" : "does not address the question", []);
        if (g.Completeness is { } c)
        {
            result.Metrics[Completeness] = Metric(Completeness, c, c >= PassMark,
                g.Missed.Count == 0 ? "states every reference point" : $"{g.Missed.Count} reference point(s) not stated",
                g.Missed.Select(p => $"missed: {p}"));
        }
        if (g.ReferenceAgreement is { } a)
        {
            result.Metrics[ReferenceAgreement] = Metric(ReferenceAgreement, a, a >= 1,
                g.Contradicted.Count == 0 ? "contradicts no reference point" : $"{g.Contradicted.Count} reference point(s) contradicted",
                g.Contradicted.Select(p => $"contradicted: {p}"));
        }
        result.Metrics[RetrievalJudged] = Metric(RetrievalJudged, g.RetrievalJudged, g.RetrievalJudged >= PassMark,
            g.OffSubject == 0 ? "every source is on the subject" : $"{g.OffSubject} source(s) off the subject", []);
        // A diagnostic, never a verdict: how much of the grade sat in the review band.
        result.Metrics[JudgeUncertain] = new NumericMetric(JudgeUncertain, g.Uncertain,
            $"{g.Uncertain:P0} of the answers between {DecisionGrade.BandLow} and {DecisionGrade.BandHigh}");
        result.Metrics[Faithfulness].AddOrUpdateMetadata("answers", JsonSerializer.Serialize(g.Answers));
        Describe(result, outcome);
        return result;
    }

    private static NumericMetric Metric(string name, double value, bool passed, string reason, IEnumerable<string> details)
    {
        var metric = new NumericMetric(name, value, reason)
        {
            Interpretation = new EvaluationMetricInterpretation(
                passed ? EvaluationRating.Good : EvaluationRating.Poor, failed: !passed, reason),
        };
        metric.AddDiagnostics([.. details.Select(EvaluationDiagnostic.Informational)]);
        return metric;
    }

    private static void Describe(EvaluationResult result, GradeOutcome outcome) =>
        result.AddOrUpdateMetadataInAllMetrics(new Dictionary<string, string>
        {
            ["model"] = outcome.Model,
            ["questions"] = outcome.Questions.ToString(CultureInfo.InvariantCulture),
            ["inputTokens"] = outcome.InputTokens.ToString(CultureInfo.InvariantCulture),
            ["durationMs"] = outcome.DurationMs.ToString("F0", CultureInfo.InvariantCulture),
            ["truncated"] = outcome.Truncated ? "true" : "false",
            ["context"] = outcome.Codebase ? "codebase" : "billing",
        });
}
