using Maf.Lab.Retrieval.Jev;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Eval.Judging;

/// <summary>
/// The outcome of grading one answer: the grade, or why there is none. A request is made unless the client skipped it
/// (an open circuit).
/// </summary>
public sealed record GradeOutcome(JevGrade? Grade, string? Failure, string Model, double DurationMs, int Questions, int InputTokens,
    bool Truncated, bool Codebase);

/// <summary>
/// Grades a generated answer with one Jev request (adopt-meai-evaluation, design D3): the shared client, its pinned model,
/// its retries and circuit. A failure is returned with its reason and never retried by another judge — two scales in one
/// run would not add up.
/// </summary>
public sealed class JevGrader(JevClient jev, JudgeOptions options, ILogger<JevGrader> logger)
{
    public bool IsConfigured => jev.IsConfigured;

    public string Model => jev.Model;

    public async Task<GradeOutcome> GradeAsync(GradeInput input, CancellationToken ct)
    {
        var request = JevGradeRequest.Build(input, options);
        GradeOutcome Failed(string reason, double ms, string? model = null) =>
            new(null, reason, model ?? jev.Model, ms, request.Questions.Count, 0, request.Truncated, request.Codebase);

        if (!jev.IsConfigured)
        {
            return Failed("no key", 0);
        }
        var outcome = await jev.AskAsync(request.State, request.Questions, options.TimeoutSeconds, ct);
        if (outcome.Response is not { } response)
        {
            // Numbers and reasons only: never a sentence, a point or a source (no message content in logs).
            logger.LogInformation("jev grade failed: {Reason} questions={Questions} ms={Elapsed:F0}",
                outcome.Failure, request.Questions.Count, outcome.DurationMs);
            return Failed(outcome.Failure ?? "no answer", outcome.DurationMs);
        }
        var model = response.Model ?? jev.Model;
        var answers = (response.Answers ?? []).Where(a => a.Value.Noul is not null).ToDictionary(a => a.Key, a => a.Value.Noul!.Value);
        var missing = request.Questions.Keys.Count(id => !answers.ContainsKey(id));
        var tokens = response.Usage?.InputTokens ?? 0;
        logger.LogInformation(
            "jev grade model={Model} questions={Questions} missing={Missing} inputTokens={InputTokens} outputTokens={OutputTokens} ms={Elapsed:F0}",
            model, request.Questions.Count, missing, tokens, response.Usage?.OutputTokens ?? 0, outcome.DurationMs);
        if (missing > 0)
        {
            // A partial response is not a grade: the missing answers would read as "no" and pass for a judgment.
            return Failed($"incomplete answer ({missing} of {request.Questions.Count} missing)", outcome.DurationMs, model) with { InputTokens = tokens };
        }
        return new GradeOutcome(JevGrade.From(request, answers), null, model, outcome.DurationMs, request.Questions.Count, tokens,
            request.Truncated, request.Codebase);
    }
}
