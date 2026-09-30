using Maf.Lab.Retrieval.Jev;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent.Jev;

/// <summary>
/// Screening requests of their own — for text that has no intent request to ride in: a tool-result item, a reviewer's
/// words, a partner's question. Same endpoint, same pinned model and the same shared <see cref="JevClient"/> as the
/// classifier, so the key is set by <see cref="JevAuthHandler"/> and nowhere else. A timeout, an error status, a failure or a missing key is an
/// answer too: <see cref="GuardScores.Failure"/> says which, and the caller's policy decides what that means.
/// </summary>
public sealed class JevGuard(JevClient client, IOptions<GuardOptions> options)
{
    public Task<GuardScores> ScreenPromptAsync(string text, CancellationToken ct) =>
        AskAsync(new JevState(text), JevGuardQuestions.Prompt, ct);

    public Task<GuardScores> ScreenContentAsync(string text, CancellationToken ct) =>
        ScreenContentAsync(text, JevGuardQuestions.Content, ct);

    /// <summary>One content item with the battery its tool calls for (<see cref="JevGuardQuestions.ContentFor"/>): same state, same ids.</summary>
    public Task<GuardScores> ScreenContentAsync(string text, IReadOnlyDictionary<string, object> battery, CancellationToken ct) =>
        AskAsync(new JevContentState(text), battery, ct);

    private async Task<GuardScores> AskAsync(object state, IReadOnlyDictionary<string, object> questions, CancellationToken ct)
    {
        var model = client.Model;
        if (options.Value.TimeoutSeconds <= 0)
        {
            return GuardScores.Failed("screening disabled", model, 0);
        }
        if (!client.IsConfigured)
        {
            return GuardScores.Failed("no key", model, 0);
        }
        // The shared client owns the race against the budget, the wire shape and the connection (jev-client-reuse).
        var outcome = await client.AskAsync(state, questions, options.Value.TimeoutSeconds, ct);
        if (outcome.Response is not { } body)
        {
            return GuardScores.Failed(outcome.Failure ?? "no answer", model, outcome.DurationMs);
        }
        var scores = JevGuardQuestions.Read(body.Answers, questions.Keys);
        return scores is null
            ? GuardScores.Failed("no answer", body.Model ?? model, outcome.DurationMs)
            : new GuardScores(scores, body.Model ?? model, outcome.DurationMs, null);
    }
}
