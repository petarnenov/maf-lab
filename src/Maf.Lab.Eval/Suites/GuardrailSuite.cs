using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Eval.Suites;

/// <summary>
/// The content guard on its own: for each labelled text, would it be refused (a prompt) or withheld (a tool result or
/// another agent's words)? It uses the production screening and thresholds — the prompt battery sent on its own, which
/// is the same battery that rides in the intent request — with no answering model and no tool.
/// </summary>
public sealed class GuardrailSuite(EvalAgentHost host)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        if (!host.Services.GetRequiredService<JevCredential>().IsConfigured)
        {
            // Without the key nothing is flagged; a 0% detection would read as a guard that got worse.
            throw new InvalidOperationException($"The guardrail suite needs {JevCredential.EnvironmentVariable} in the environment.");
        }
        var guardrail = host.Services.GetRequiredService<Guardrail>();
        var cases = ctx.Take(DatasetLoader.Guardrail(ctx.DatasetRoot)).ToList();
        var outcomes = new List<(bool Malicious, bool Flagged, bool Answered, string Side, string Language, string Split, string Category)>();
        var failures = new List<EvalCaseFailure>();
        var unscreened = 0;
        var latencies = new List<double>();
        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var (flagged, scores) = c.Side == "prompt"
                ? await PromptAsync(guardrail, c.Text, ct)
                : await ContentAsync(guardrail, c.Text, ct);
            var answered = scores.Scores is not null;
            unscreened += answered ? 0 : 1;
            latencies.Add(scores.DurationMs);
            outcomes.Add((c.Malicious, flagged, answered, c.Side, c.Language, c.Split, c.Category));
            if (flagged != c.Malicious)
            {
                var top = scores.Highest;
                failures.Add(new EvalCaseFailure(c.Id,
                    $"expected {(c.Malicious ? "flagged" : "let through")}, got {(flagged ? "flagged" : "let through")} — "
                    + (answered ? $"top {top?.Question} {top?.Top:0.00}" : $"unscreened: {scores.Failure}")));
            }
            ctx.Progress($"guardrail {i + 1}/{cases.Count} {c.Id}: {(flagged == c.Malicious ? "ok" : "WRONG")}{(answered ? "" : " (unscreened)")}");
        }
        latencies.Sort();
        ctx.Progress($"guardrail: {unscreened} unscreened; latency median {Percentile(latencies, 0.5):0} ms, p90 {Percentile(latencies, 0.9):0} ms, max {(latencies.Count == 0 ? 0 : latencies[^1]):0} ms");
        return [SuiteContext.Variant("jev", Metrics.Guardrail(outcomes), ctx.ThresholdsFor("guardrail"), cases.Count, failures)];
    }

    private static async Task<(bool Flagged, GuardScores Scores)> PromptAsync(Guardrail guardrail, string text, CancellationToken ct)
    {
        var screen = await guardrail.ScreenPromptAsync(text, ct);
        return (screen.Blocked, screen.Scores);
    }

    /// <summary>A tool-result item or a reviewer's words: screened as one item, judged against the withhold threshold.</summary>
    private static async Task<(bool Flagged, GuardScores Scores)> ContentAsync(Guardrail guardrail, string text, CancellationToken ct)
    {
        var screened = await guardrail.ScreenToolResultAsync("eval_item", text, null, isError: false, ct);
        var item = screened!.Items.Single();
        return (item.Decision == GuardDecision.Withheld, item.Scores);
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];
}
