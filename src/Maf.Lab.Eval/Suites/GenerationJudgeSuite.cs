using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Judging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace Maf.Lab.Eval.Suites;

/// <summary>
/// The generation grade measured on its own, without the agent (adopt-meai-evaluation, design D6) — as the
/// <c>answer-check</c> suite measures the production check:
/// <list type="bullet">
/// <item><c>grade</c>: over <c>answer-check.jsonl</c>, whether the grade fails faithfulness exactly on the answers a
/// reviewer found unsupported and relevance on the ones found off the question — the answer-check metrics, so the two
/// read side by side;</item>
/// <item><c>check</c>: the production answer check on the same rows;</item>
/// <item><c>points</c>: over <c>generation-judge.jsonl</c>, whether each reference point is graded stated and
/// contradicted as the reviewer labelled it;</item>
/// <item><c>sentences</c>: over <c>generation-sentences.jsonl</c>, whether each sentence is graded a claim and, if one,
/// supported, as the reviewer labelled it — with citations (correct and invented) counted on their own, since the
/// system prompt requires every cited place to be exact.</item>
/// </list>
/// Each rate is also given per domain, language and split.
/// </summary>
public sealed class GenerationJudgeSuite(DecisionAnswerCheck check, ReportingConfiguration reporting)
{
    public const string ScenarioPrefix = "generation-judge.";

    public int InputTokens { get; private set; }

    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        var labelled = ctx.Take(DatasetLoader.AnswerCheck(ctx.DatasetRoot)).ToList();
        var pointCases = ctx.Take(DatasetLoader.GenerationJudge(ctx.DatasetRoot)).ToList();
        var sentenceCases = ctx.Take(DatasetLoader.GenerationSentences(ctx.DatasetRoot)).ToList();
        var total = labelled.Count + pointCases.Count + sentenceCases.Count;
        var done = 0;

        var gradeOutcomes = new List<Metrics.AnswerCheckOutcome>();
        var checkOutcomes = new List<Metrics.AnswerCheckOutcome>();
        var gradeFailures = new List<EvalCaseFailure>();
        var checkFailures = new List<EvalCaseFailure>();
        foreach (var c in labelled)
        {
            var context = new DecisionGradeContext([.. c.Sources.SelectMany(AnswerCheckSuite.Current)], [], c.PreviousQuestion,
                [.. c.PreviousSources.Select(AnswerCheckSuite.Previous)]);
            var outcome = await GradeAsync(c.Id, c.Question, c.Answer, context, ct);
            var g = outcome.Grade;
            // A failed grade counts as nothing flagged, as an unchecked answer does in the check's own suite.
            var graded = new Metrics.AnswerCheckOutcome(c.Unsupported, c.OffTopic, g is not null,
                g is not null && g.Faithfulness < DecisionGenerationEvaluator.PassMark, g is not null && g.Relevance < 1,
                g is not null && g.Uncertain > 0, c.Domain, c.Language, c.Split);
            gradeOutcomes.Add(graded);
            var gradeOk = g is not null && graded.NotGrounded == c.Unsupported && graded.NotRelevant == c.OffTopic;
            var line = g is null ? $"judge failed: {outcome.Failure}" : $"f={g.Faithfulness:0.##} r={g.Relevance:0.##}";
            if (!gradeOk)
            {
                gradeFailures.Add(new EvalCaseFailure(c.Id, $"expected {Expect(c.Unsupported, c.OffTopic)}, got {line} "
                    + $"[{c.Domain} {c.Language} {c.Split}]{(g is null ? "" : $": {g.Reason()} {Probabilities(g, "supported_", "claim_")}")}"));
            }

            var (result, checkOutcome) = await AnswerCheckSuite.CheckAsync(check, c, ct);
            checkOutcomes.Add(checkOutcome);
            var checkOk = checkOutcome.NotGrounded == c.Unsupported && checkOutcome.NotRelevant == c.OffTopic;
            if (!checkOk)
            {
                checkFailures.Add(new EvalCaseFailure(c.Id, $"expected {Expect(c.Unsupported, c.OffTopic)}, got {GenerationSuite.Describe(result)}"));
            }
            ctx.Progress($"generation-judge {++done}/{total} {c.Id}: grade {(gradeOk ? "ok" : "WRONG")} {line} · check {(checkOk ? "ok" : "WRONG")} {GenerationSuite.Describe(result)}");
        }

        var points = new List<PointOutcome>();
        var pointFailures = new List<EvalCaseFailure>();
        foreach (var c in pointCases)
        {
            var outcome = await GradeAsync(c.Id, c.Question, c.Answer, new DecisionGradeContext([], c.ReferencePoints), ct);
            var wrong = new List<string>();
            for (var j = 0; j < c.ReferencePoints.Count; j++)
            {
                var g = outcome.Grade;
                var stated = g?.Stated[j] ?? false;
                var contradicted = g?.PointContradicted[j] ?? false;
                points.Add(new PointOutcome(c.Stated[j] == stated, c.Contradicted[j] == contradicted, g is not null, c.Domain, c.Language, c.Split));
                if (g is not null && (c.Stated[j] != stated || c.Contradicted[j] != contradicted))
                {
                    wrong.Add($"point {j} expected {(c.Stated[j] ? "stated" : "not stated")}/{(c.Contradicted[j] ? "contradicted" : "not contradicted")}, "
                        + $"got stated={g.Answers[DecisionGradeRequest.StatedId(j)]:0.##} contradicts={g.Answers[DecisionGradeRequest.ContradictsId(j)]:0.##}");
                }
            }
            if (outcome.Grade is null)
            {
                pointFailures.Add(new EvalCaseFailure(c.Id, $"judge failed: {outcome.Failure}"));
            }
            else if (wrong.Count > 0)
            {
                pointFailures.Add(new EvalCaseFailure(c.Id, $"[{c.Domain} {c.Language} {c.Split}] {string.Join("; ", wrong)}"));
            }
            var status = outcome.Grade is null ? $"judge failed: {outcome.Failure}" : wrong.Count == 0 ? "ok" : $"WRONG {wrong.Count}/{c.ReferencePoints.Count}";
            ctx.Progress($"generation-judge {++done}/{total} {c.Id}: points {status}");
        }

        var sentences = new List<SentenceOutcome>();
        var sentenceFailures = new List<EvalCaseFailure>();
        foreach (var c in sentenceCases)
        {
            var outcome = await GradeAsync(c.Id, c.Question, c.Answer, new DecisionGradeContext([.. c.Sources.SelectMany(AnswerCheckSuite.Current)], []), ct);
            var g = outcome.Grade;
            var wrong = new List<string>();
            for (var i = 0; i < c.Sentences.Count; i++)
            {
                var label = c.Sentences[i];
                if (label.Ambiguous)
                {
                    continue;
                }
                double? claimP = g?.Answers.GetValueOrDefault(DecisionGradeRequest.ClaimId(i));
                double? supportedP = g?.Answers.GetValueOrDefault(DecisionGradeRequest.SupportedId(i));
                // Read as the grade reads it, citation check included.
                var claim = g?.SentenceClaims[i] ?? false;
                var supported = g?.SentenceSupported[i] ?? false;
                var o = new SentenceOutcome(g is not null, label.Claim, claim, label.Supported, supported, label.Citation, c.Domain, c.Language, c.Split);
                sentences.Add(o);
                if (g is not null && !o.Correct)
                {
                    wrong.Add($"[{i}] expected {(label.Claim ? (label.Supported == true ? "supported" : "unsupported") : "no claim")}"
                        + $"{(label.Citation ? " citation" : "")}, got claim={claimP:0.##} supported={supportedP:0.##}");
                }
            }
            if (g is null)
            {
                sentenceFailures.Add(new EvalCaseFailure(c.Id, $"judge failed: {outcome.Failure}"));
            }
            else if (wrong.Count > 0)
            {
                sentenceFailures.Add(new EvalCaseFailure(c.Id, $"[{c.Domain} {c.Language} {c.Split}] {string.Join("; ", wrong)}"));
            }
            var status = g is null ? $"judge failed: {outcome.Failure}" : wrong.Count == 0 ? "ok" : $"WRONG {wrong.Count}/{c.Sentences.Count(l => !l.Ambiguous)}";
            ctx.Progress($"generation-judge {++done}/{total} {c.Id}: sentences {status}");
        }

        var thresholds = ctx.ThresholdsFor("generation-judge");
        return
        [
            SuiteContext.Variant("grade", Metrics.AnswerCheck(gradeOutcomes), Prefixed(thresholds, "grade."), labelled.Count, gradeFailures),
            SuiteContext.Variant("check", Metrics.AnswerCheck(checkOutcomes), Prefixed(thresholds, "check."), labelled.Count, checkFailures),
            SuiteContext.Variant("points", PointMetrics(points), Prefixed(thresholds, "points."), pointCases.Count, pointFailures),
            SuiteContext.Variant("sentences", SentenceMetrics(sentences), Prefixed(thresholds, "sentences."), sentenceCases.Count, sentenceFailures),
        ];
    }

    private async Task<GradeOutcome> GradeAsync(string id, string question, string answer, DecisionGradeContext context, CancellationToken ct)
    {
        await using (var run = await reporting.CreateScenarioRunAsync(ScenarioPrefix + id, cancellationToken: ct))
        {
            await run.EvaluateAsync([new ChatMessage(ChatRole.User, question)], new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)),
                [context], ct);
        }
        InputTokens += context.Outcome!.InputTokens;
        return context.Outcome;
    }

    /// <summary>One labelled reference point and whether the grade matched each of its two labels.</summary>
    public readonly record struct PointOutcome(bool StatedOk, bool ContradictedOk, bool Graded, string Domain, string Language, string Split);

    /// <summary>
    /// <c>pointAccuracy</c>: points whose two labels both match; <c>statedAccuracy</c> and <c>contradictedAccuracy</c>
    /// each alone; <c>graded</c>: points that got a grade (a failed request counts as wrong). Per domain, language and split.
    /// </summary>
    public static Dictionary<string, double> PointMetrics(IReadOnlyList<PointOutcome> points)
    {
        var metrics = new Dictionary<string, double>();
        static double Share(IEnumerable<bool> hits)
        {
            var list = hits.ToList();
            return list.Count == 0 ? 1 : (double)list.Count(h => h) / list.Count;
        }
        void Rates(string suffix, IReadOnlyCollection<PointOutcome> group)
        {
            metrics[$"pointAccuracy{suffix}"] = Share(group.Select(p => p.Graded && p.StatedOk && p.ContradictedOk));
            metrics[$"statedAccuracy{suffix}"] = Share(group.Select(p => p.Graded && p.StatedOk));
            metrics[$"contradictedAccuracy{suffix}"] = Share(group.Select(p => p.Graded && p.ContradictedOk));
        }
        Rates("", [.. points]);
        metrics["graded"] = Share(points.Select(p => p.Graded));
        foreach (var key in new Func<PointOutcome, string>[] { p => p.Domain, p => p.Language, p => p.Split })
        {
            foreach (var group in points.GroupBy(key).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Rates($":{group.Key}", group.ToList());
            }
        }
        return metrics;
    }

    /// <summary>One labelled sentence and what the grade made of it. A sentence that is no claim is right when graded none.</summary>
    public readonly record struct SentenceOutcome(bool Graded, bool Claim, bool GradedClaim, bool? Supported, bool GradedSupported, bool Citation,
        string Domain, string Language, string Split)
    {
        /// <summary>A claim is right when graded a claim with the labelled support; a non-claim, when graded none.</summary>
        public bool Correct => Graded && (Claim ? GradedClaim && GradedSupported == Supported : !GradedClaim);
    }

    /// <summary>
    /// <c>sentenceAccuracy</c>: sentences graded as labelled. Over the labelled claims, <c>supportedPass</c> (supported
    /// claims graded supported) and <c>unsupportedDetection</c> (unsupported claims caught) apart, so a grade that flags
    /// everything cannot hide behind one that flags nothing; the same for citations: <c>citationPass</c> (a correct
    /// citation graded supported) and <c>citationDetection</c> (an invented one caught). A claim graded as no claim is not
    /// checked for support, so it counts as passed when supported and missed when not — the grade's own reading.
    /// <c>claimAccuracy</c>: claims and non-claims told apart. Per domain, language and split.
    /// </summary>
    public static Dictionary<string, double> SentenceMetrics(IReadOnlyList<SentenceOutcome> sentences)
    {
        var metrics = new Dictionary<string, double>();
        static double Share(IEnumerable<bool> hits)
        {
            var list = hits.ToList();
            return list.Count == 0 ? 1 : (double)list.Count(h => h) / list.Count;
        }
        // A sentence graded as no claim is never asked about support: for faithfulness it is as good as supported.
        static bool Passes(SentenceOutcome o) => o.Graded && (!o.GradedClaim || o.GradedSupported);
        void Rates(string suffix, IReadOnlyCollection<SentenceOutcome> group)
        {
            metrics[$"sentenceAccuracy{suffix}"] = Share(group.Select(o => o.Correct));
            metrics[$"claimAccuracy{suffix}"] = Share(group.Select(o => o.Graded && o.GradedClaim == o.Claim));
            var claims = group.Where(o => o.Claim && !o.Citation).ToList();
            if (claims.Any(o => o.Supported == true))
            {
                metrics[$"supportedPass{suffix}"] = Share(claims.Where(o => o.Supported == true).Select(Passes));
            }
            if (claims.Any(o => o.Supported == false))
            {
                metrics[$"unsupportedDetection{suffix}"] = Share(claims.Where(o => o.Supported == false).Select(o => !Passes(o)));
            }
            var citations = group.Where(o => o.Citation).ToList();
            if (citations.Any(o => o.Supported == true))
            {
                metrics[$"citationPass{suffix}"] = Share(citations.Where(o => o.Supported == true).Select(Passes));
            }
            if (citations.Any(o => o.Supported == false))
            {
                metrics[$"citationDetection{suffix}"] = Share(citations.Where(o => o.Supported == false).Select(o => !Passes(o)));
            }
        }
        Rates("", [.. sentences]);
        foreach (var key in new Func<SentenceOutcome, string>[] { o => o.Domain, o => o.Language, o => o.Split })
        {
            foreach (var group in sentences.GroupBy(key).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Rates($":{group.Key}", group.ToList());
            }
        }
        return metrics;
    }

    /// <summary>One suite's thresholds, split by variant: <c>grade.accuracy</c> gates the grade variant's accuracy.</summary>
    private static IReadOnlyDictionary<string, double> Prefixed(IReadOnlyDictionary<string, double> thresholds, string prefix) =>
        thresholds.Where(t => t.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(t => t.Key[prefix.Length..], t => t.Value);

    private static string Expect(bool unsupported, bool offTopic) =>
        $"{(unsupported ? "not grounded" : "grounded")}/{(offTopic ? "not relevant" : "relevant")}";

    /// <summary>The probabilities behind a faithfulness verdict, in sentence order: claim and support per sentence.</summary>
    private static string Probabilities(DecisionGrade g, string supported, string claim) =>
        "[" + string.Join(" ", g.Answers.Keys.Where(k => k.StartsWith(supported, StringComparison.Ordinal))
            .Select(k => k[supported.Length..])
            .Select(i => $"{i}:c{g.Answers[claim + i]:0.##}/s{g.Answers[supported + i]:0.##}")) + "]";
}
