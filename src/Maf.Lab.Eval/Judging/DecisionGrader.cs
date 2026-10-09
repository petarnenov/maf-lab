using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Eval.Judging;

/// <summary>
/// The outcome of grading one answer: the grade, or why there is none. A request is made unless the client skipped it
/// (an open circuit).
/// </summary>
public sealed record GradeOutcome(DecisionGrade? Grade, string? Failure, string Model, double DurationMs, int Questions, int InputTokens,
    bool Truncated, bool Codebase);

/// <summary>
/// Grades a generated answer with one decision request (adopt-meai-evaluation, design D3): the installed engine, its
/// pinned model, its retries and circuit. A failure is returned with its reason and never retried by another judge — two scales in one
/// run would not add up.
/// </summary>
public sealed class DecisionGrader(IDecisionEngine engine, JudgeOptions options, ILogger<DecisionGrader> logger)
{
    public bool IsConfigured => engine.IsConfigured;

    public string Model => engine.Engine;

    public async Task<GradeOutcome> GradeAsync(GradeInput input, CancellationToken ct)
    {
        var request = DecisionGradeRequest.Build(input, options);
        GradeOutcome Failed(string reason, double ms, string? model = null) =>
            new(null, reason, model ?? engine.Engine, ms, request.Questions.Count, 0, request.Truncated, request.Codebase);

        if (!engine.IsConfigured)
        {
            return Failed(DecisionFailures.NoKey, 0);
        }
        var outcome = await engine.DecideAsync(request.State, request.Questions, TimeSpan.FromSeconds(options.TimeoutSeconds), ct);
        if (outcome.Answers is not { } given)
        {
            // Numbers and reasons only: never a sentence, a point or a source (no message content in logs).
            logger.LogInformation("decision grade failed: {Reason} questions={Questions} ms={Elapsed:F0}",
                outcome.Failure, request.Questions.Count, outcome.DurationMs);
            return Failed(outcome.Failure ?? "no answer", outcome.DurationMs);
        }
        var model = outcome.Engine;
        var answers = given.Where(a => a.Value.Probability is not null).ToDictionary(a => a.Key, a => a.Value.Probability!.Value);
        var missing = request.Questions.Keys.Count(id => !answers.ContainsKey(id));
        var tokens = outcome.Usage?.InputTokens ?? 0;
        logger.LogInformation(
            "decision grade model={Model} questions={Questions} missing={Missing} inputTokens={InputTokens} outputTokens={OutputTokens} ms={Elapsed:F0}",
            model, request.Questions.Count, missing, tokens, outcome.Usage?.OutputTokens ?? 0, outcome.DurationMs);
        if (missing > 0)
        {
            // A partial response is not a grade: the missing answers would read as "no" and pass for a judgment.
            return Failed($"incomplete answer ({missing} of {request.Questions.Count} missing)", outcome.DurationMs, model) with { InputTokens = tokens };
        }
        return new GradeOutcome(DecisionGrade.From(request, answers), null, model, outcome.DurationMs, request.Questions.Count, tokens,
            request.Truncated, request.Codebase);
    }
}
