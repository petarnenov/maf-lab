using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Plugins.Jev;

/// <summary>
/// TypeSafe Jev as the decision engine: each neutral question becomes the wire shape Jev documents (a Choice, a Noul with
/// or without criteria, a Score), in the order the caller gave, over the caller's state, and Jev's answers come back as
/// <see cref="DecisionAnswer"/>s. The request body is byte-identical to the one the core sent before the engine was a
/// plugin (the golden files in this plugin's tests prove it), so every measured threshold still holds.
/// </summary>
public sealed class JevDecisionEngine(JevClient client) : IDecisionEngine
{
    public string Engine => client.Model;

    public bool IsConfigured => client.IsConfigured;

    public async Task<DecisionOutcome> DecideAsync(object state, IReadOnlyDictionary<string, DecisionQuestion> questions, TimeSpan budget,
        CancellationToken ct)
    {
        var wire = new Dictionary<string, object>(questions.Count, StringComparer.Ordinal);
        foreach (var (id, question) in questions)
        {
            wire[id] = Wire(question);
        }
        var outcome = await client.AskAsync(state, wire, budget.TotalSeconds, ct);
        if (outcome.Response is not { } response)
        {
            return new DecisionOutcome(null, client.Model, null, outcome.Failure, outcome.DurationMs, outcome.Skipped);
        }
        // An answered request with no answers is still an answer: each caller reads the questions it asked and finds none.
        var answers = (response.Answers ?? []).ToDictionary(a => a.Key, a => ToDecision(a.Value), StringComparer.Ordinal);
        var usage = response.Usage is { } u ? new DecisionUsage(u.InputTokens, u.OutputTokens) : null;
        return new DecisionOutcome(answers, response.Model ?? client.Model, usage, null, outcome.DurationMs);
    }

    internal static object Wire(DecisionQuestion question) => question switch
    {
        ChoiceQuestion c => new JevChoiceQuestion(c.Instructions, c.Criteria),
        NoulQuestion { Criteria: { } criteria } n => new JevCriteriaNoul(n.Instructions, new JevNoulCriteria(criteria.Yes, criteria.No)),
        NoulQuestion n => new JevNoulQuestion(n.Instructions),
        ScoreQuestion s => new JevScoreQuestion(s.Instructions, s.Levels),
        _ => throw new ArgumentException($"no Jev shape for {question.GetType().Name}", nameof(question)),
    };

    internal static DecisionAnswer ToDecision(JevAnswer answer) =>
        new(answer.Choice, answer.Confidence, answer.Noul, answer.Probabilities);
}
