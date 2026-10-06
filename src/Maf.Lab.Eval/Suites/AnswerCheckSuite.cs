using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Retrieval.Jev;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Eval.Suites;

/// <summary>
/// Jev's answer check on its own (fit-answer-checks-to-code-questions): each labelled answer of
/// <c>evals/answer-check.jsonl</c> is replayed through the production <see cref="JevAnswerCheck"/> — the same source
/// selection, context choice, band and configuration — with no answering model and no tool. It measures how many
/// unsupported answers the check flags and how many supported ones it leaves alone, per domain, language and split.
/// </summary>
/// <param name="services">The eval host's services; only the Jev credential and the answer check are resolved.</param>
public sealed class AnswerCheckSuite(IServiceProvider services)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        if (!services.GetRequiredService<JevCredential>().IsConfigured)
        {
            // Without the key every answer is unchecked; a check that flagged nothing would read as a perfect pass rate.
            throw new InvalidOperationException($"The answer-check suite needs {JevCredential.EnvironmentVariable} in the environment.");
        }
        var check = services.GetRequiredService<JevAnswerCheck>();
        var cases = ctx.Take(DatasetLoader.AnswerCheck(ctx.DatasetRoot)).ToList();
        var outcomes = new List<Metrics.AnswerCheckOutcome>();
        var failures = new List<EvalCaseFailure>();
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var (result, outcome) = await CheckAsync(check, c, ct);
            outcomes.Add(outcome);
            if (!result.Checked)
            {
                var reason = result.Reason ?? "no answer";
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            }
            var ok = outcome.NotGrounded == c.Unsupported && outcome.NotRelevant == c.OffTopic;
            var described = GenerationSuite.Describe(result);
            if (!ok)
            {
                failures.Add(new EvalCaseFailure(c.Id,
                    $"expected {(c.Unsupported ? "not grounded" : "grounded")}/{(c.OffTopic ? "not relevant" : "relevant")}, got {described} "
                    + $"[{c.Domain} {c.Language} {c.Split}, {result.Context} context]"));
            }
            ctx.Progress($"answer-check {i + 1}/{cases.Count} {c.Id}: {(ok ? "ok" : "WRONG")} {described}");
        }
        var band = outcomes.Count(o => o.Uncertain);
        ctx.Progress($"answer-check: {band} in the band, {outcomes.Count(o => !o.Checked)} unchecked"
            + (reasons.Count == 0 ? "" : $" ({string.Join(", ", reasons.OrderBy(u => u.Key, StringComparer.Ordinal).Select(u => $"{u.Key}: {u.Value}"))})"));
        return [SuiteContext.Variant("jev", Metrics.AnswerCheck(outcomes), ctx.ThresholdsFor("answer-check"), cases.Count, failures)];
    }

    /// <summary>One labelled answer through the production check, and what it made of the labels.</summary>
    internal static async Task<(AnswerCheck Result, Metrics.AnswerCheckOutcome Outcome)> CheckAsync(JevAnswerCheck check, AnswerCheckCase c,
        CancellationToken ct)
    {
        var result = await check.CheckAsync(c.Question, c.Answer, [.. c.Sources.SelectMany(Current)], ct,
            string.IsNullOrEmpty(c.PreviousQuestion) ? null : c.PreviousQuestion, [.. c.PreviousSources.Select(Previous)]);
        var signals = result.Signals.ToHashSet();
        return (result, new Metrics.AnswerCheckOutcome(c.Unsupported, c.OffTopic, result.Checked,
            signals.Contains(Maf.Lab.Domain.Feedback.TurnSignal.AnswerNotGrounded), signals.Contains(Maf.Lab.Domain.Feedback.TurnSignal.AnswerNotRelevant),
            result.Verdict == AnswerVerdict.Uncertain, c.Domain, c.Language, c.Split));
    }

    /// <summary>A source as the turn runner reads it: a search item by its place, any other result whole.</summary>
    internal static IEnumerable<ReadItem> Current(AnswerCheckSource s) =>
        s.Item is { } item
            ? [ReadItem.FromSearchItem(item, Domains.OfTool(s.Tool) ?? Domains.None)]
            : [ReadItem.Whole(s.Tool, s.Text!, Domains.OfTool(s.Tool) ?? Domains.None)];

    /// <summary>A previous source as the stored trace holds it: that call's data envelope.</summary>
    internal static ReadItem Previous(AnswerCheckSource s) =>
        ReadItem.FromEnvelope(ToolDataEnvelope.Wrap(s.Tool, s.Text ?? $"{{\"results\":[{s.Item!.Value.GetRawText()}]}}"));
}
