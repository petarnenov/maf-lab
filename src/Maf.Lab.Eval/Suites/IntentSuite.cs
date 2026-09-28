using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Eval.Suites;

/// <summary>
/// The intent classifier on its own: for each question, would the turn be forced to search the documentation? No
/// answering model and no tool runs, so the suite is fast, cheap and measures exactly the one decision.
/// </summary>
public sealed class IntentSuite(EvalAgentHost host)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        if (!host.Services.GetRequiredService<JevCredential>().IsConfigured)
        {
            // Without the key every decision is "not forced"; a 47% score would read as a classifier that got worse.
            throw new InvalidOperationException($"The intent suite needs {JevCredential.EnvironmentVariable} in the environment.");
        }
        var classifier = host.Services.GetRequiredService<IIntentClassifier>();
        var cases = ctx.Take(DatasetLoader.Intent(ctx.DatasetRoot)).ToList();
        var outcomes = new List<(IntentCase Case, bool Forced)>();
        var failures = new List<EvalCaseFailure>();
        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var decision = await classifier.ClassifyAsync(c.Question, ct);
            var forced = IntentClassifier.ForcesRetrieval(decision.Intent);
            outcomes.Add((c, forced));
            if (forced != c.Forces)
            {
                failures.Add(new EvalCaseFailure(c.Id,
                    $"expected {(c.Forces ? "forced" : "not forced")}, got {(forced ? "forced" : "not forced")} — choice={decision.Choice} "
                    + $"confidence={decision.Confidence:0.00} inDomain={decision.InDomain:0.00} reason={decision.Reason ?? "-"}"));
            }
            ctx.Progress($"intent {i + 1}/{cases.Count} {c.Id}: {(forced == c.Forces ? "ok" : "WRONG")}");
        }
        return [SuiteContext.Variant("jev", Metrics.Intent(outcomes.Select(o => (o.Case.Forces, o.Forced, o.Case.Language, o.Case.Split))),
            ctx.ThresholdsFor("intent"), cases.Count, failures)];
    }
}
