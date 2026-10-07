using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent.Decisions;

/// <summary>
/// Screening requests of their own — for text that has no intent request to ride in: a tool-result item, a reviewer's
/// words, a partner's question. The same installed decision engine as the classifier, which owns the credential and the
/// connection. A timeout, an error status, a failure or a missing key is an answer too: <see cref="GuardScores.Failure"/>
/// says which, and the caller's policy decides what that means.
/// </summary>
public sealed class DecisionGuard(IDecisionEngine engine, IOptions<GuardOptions> options)
{
    public Task<GuardScores> ScreenPromptAsync(string text, CancellationToken ct) =>
        AskAsync("guard.prompt", new QuestionState(text), GuardQuestions.Prompt, ct);

    public Task<GuardScores> ScreenContentAsync(string text, CancellationToken ct) =>
        ScreenContentAsync(text, GuardQuestions.Content, ct);

    /// <summary>One content item with the battery its tool calls for (<see cref="GuardQuestions.ContentFor"/>): same state, same ids.</summary>
    public Task<GuardScores> ScreenContentAsync(string text, IReadOnlyDictionary<string, DecisionQuestion> battery, CancellationToken ct) =>
        AskAsync("guard.content", new ContentState(text), battery, ct);

    private async Task<GuardScores> AskAsync(string site, object state, IReadOnlyDictionary<string, DecisionQuestion> questions,
        CancellationToken ct)
    {
        var model = engine.Engine;
        if (options.Value.TimeoutSeconds <= 0)
        {
            return GuardScores.Failed("screening disabled", model, 0);
        }
        if (!engine.IsConfigured)
        {
            return GuardScores.Failed(DecisionFailures.NoKey, model, 0);
        }
        // The engine owns the race against the budget, the wire shape and the connection.
        var outcome = await engine.DecideAsync(state, questions, TimeSpan.FromSeconds(options.Value.TimeoutSeconds), ct);
        Maf.Lab.Hosting.LabTelemetry.Instruments.RecordDecision(site, outcome.Usage?.InputTokens, DomainCatalogue.Current.All.Count);
        if (outcome.Answers is not { } answers)
        {
            return GuardScores.Failed(outcome.Failure ?? "no answer", model, outcome.DurationMs);
        }
        var scores = GuardQuestions.Read(answers, questions.Keys);
        return scores is null
            ? GuardScores.Failed("no answer", outcome.Engine, outcome.DurationMs)
            : new GuardScores(scores, outcome.Engine, outcome.DurationMs, null);
    }
}
