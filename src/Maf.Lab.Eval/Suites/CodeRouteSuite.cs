using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;
using Maf.Lab.Hosting.Cli;
using Maf.Lab.Retrieval.Jev;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Eval.Suites;

/// <summary>What the classification and the router did with one labelled row; <paramref name="Failed"/> when Jev gave no answer.</summary>
public sealed record CodeRouteOutcome(CodeRouteCase Case, string? Choice, double? Confidence, ToolRoute? Route, string? Reason, bool Failed);

/// <summary>
/// The code-route Choice on its own (route-structural-code-questions): for each labelled question, the one Jev request a
/// turn sends, then what code would route. No answering model and no tool runs. A row is scored on what code would do,
/// not only on Jev's raw choice: a structural row counts only when the right tool and direction would be called.
/// </summary>
public sealed class CodeRouteSuite(EvalAgentHost host)
{
    public const string Name = "code-route";

    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        if (!host.Services.GetRequiredService<JevCredential>().IsConfigured)
        {
            throw new InvalidOperationException($"The code-route suite needs {JevCredential.EnvironmentVariable} in the environment.");
        }
        var options = host.Services.GetRequiredService<IOptions<JevOptions>>().Value;
        if (!options.RouteCodeTools)
        {
            throw new InvalidOperationException("The code-route suite measures Jev:RouteCodeTools, which is off.");
        }
        host.RequireDomain("codebase", "code");
        using var domains = host.UseDomains();
        var classifier = host.Services.GetRequiredService<IIntentClassifier>();
        var cases = ctx.Take(DatasetLoader.CodeRoute(ctx.DatasetRoot)).ToList();
        using var bar = new ConsoleProgress(Name);
        bar.SetTotal(cases.Count);
        var outcomes = new List<CodeRouteOutcome>();
        try
        {
            foreach (var c in cases)
            {
                bar.Working(c.Id);
                var decision = await classifier.ClassifyAsync(c.Question, ct);
                // The router saw every codebase tool offered, as the api and the eval host offer them.
                outcomes.Add(new CodeRouteOutcome(c, decision.CodeRouting?.Choice, decision.CodeRouting?.Confidence, decision.CodeRoute,
                    decision.CodeRouteReason, decision.CodeRouting is null));
                bar.Advance();
            }
            bar.Succeed($"{cases.Count} row(s), {outcomes.Count(o => o.Failed)} failed");
        }
        catch (OperationCanceledException)
        {
            bar.Cancel();
            throw;
        }
        var failures = new List<EvalCaseFailure>();
        var metrics = Score(outcomes, failures);
        ctx.Progress($"{Name}: accuracy={metrics["accuracy"]:0.###} structuralRecall={metrics["structuralRecall"]:0.###} textKept={metrics["textKept"]:0.###} " +
            $"floor={options.MinCodeRouteConfidence:0.##}");
        // Apart from the scores, and not a metric: more failures is worse, which the baseline gate would read as better.
        if (outcomes.Count(o => o.Failed) is var failed and > 0)
        {
            ctx.Progress($"{Name}: {failed} request(s) failed, left out of the scores and named in the report");
        }
        return [SuiteContext.Variant("jev", metrics, ctx.ThresholdsFor(Name), cases.Count, failures)];
    }

    private static bool Structural(string option) => option is "callers" or "callees" or "impact";

    /// <summary>Whether code routed the row as its label says: the expected tool and direction, or, for text and none, nothing.</summary>
    public static bool Correct(CodeRouteOutcome o) =>
        Structural(o.Case.Expected) && o.Case.HasArgument
            ? o.Route is { } r && r.Tool == (o.Case.Expected == "impact" ? GraphTools.ChangeImpact : GraphTools.TraceCodeSymbol)
              && (o.Case.Expected == "impact" || Equals(r.Arguments.GetValueOrDefault("direction"), o.Case.Expected))
            : o.Route is null;

    /// <summary>
    /// accuracy (Jev's choice), structuralRecall (structural rows with an argument routed right), textKept (text and none
    /// rows not routed), noArgumentKept (structural rows without an argument left to the search), each also per language
    /// and split. Failed rows are left out of every score and named among the failures.
    /// </summary>
    public static Dictionary<string, double> Score(IReadOnlyList<CodeRouteOutcome> outcomes, List<EvalCaseFailure> failures)
    {
        var answered = outcomes.Where(o => !o.Failed).ToList();
        foreach (var o in outcomes)
        {
            if (o.Failed)
            {
                failures.Add(new EvalCaseFailure(o.Case.Id, $"failed: {o.Reason ?? "no answer"}"));
            }
            else if (!Correct(o) || o.Choice != o.Case.Expected)
            {
                failures.Add(new EvalCaseFailure(o.Case.Id, $"expected {o.Case.Expected}{(o.Case.HasArgument ? "" : " (no argument)")}, " +
                    $"got {o.Choice} {o.Confidence:0.00} → {o.Route?.Tool ?? "not routed"}{(o.Reason is null ? "" : $" ({o.Reason})")}"));
            }
        }
        var metrics = Group(answered, "");
        foreach (var language in answered.Select(o => o.Case.Language).Distinct().Order())
        {
            foreach (var (k, v) in Group([.. answered.Where(o => o.Case.Language == language)], $":{language}"))
            {
                metrics[k] = v;
            }
        }
        foreach (var split in answered.Select(o => o.Case.Split).Distinct().Order())
        {
            foreach (var (k, v) in Group([.. answered.Where(o => o.Case.Split == split)], $":{split}"))
            {
                metrics[k] = v;
            }
        }
        return metrics;
    }

    private static Dictionary<string, double> Group(IReadOnlyList<CodeRouteOutcome> rows, string suffix)
    {
        var metrics = new Dictionary<string, double>();
        if (rows.Count == 0)
        {
            return metrics;
        }
        metrics[$"accuracy{suffix}"] = (double)rows.Count(o => o.Choice == o.Case.Expected) / rows.Count;
        Share(metrics, $"structuralRecall{suffix}", rows.Where(o => Structural(o.Case.Expected) && o.Case.HasArgument));
        Share(metrics, $"textKept{suffix}", rows.Where(o => !Structural(o.Case.Expected)));
        Share(metrics, $"noArgumentKept{suffix}", rows.Where(o => Structural(o.Case.Expected) && !o.Case.HasArgument));
        return metrics;
    }

    private static void Share(Dictionary<string, double> metrics, string key, IEnumerable<CodeRouteOutcome> rows)
    {
        var list = rows.ToList();
        if (list.Count > 0)
        {
            metrics[key] = (double)list.Count(Correct) / list.Count;
        }
    }
}
