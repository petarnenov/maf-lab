using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;

namespace Maf.Lab.Eval.Suites;

/// <summary>Did the agent (prompt + tool descriptions + intent forcing + model) call the expected tools?</summary>
public sealed class SelectionSuite(EvalAgentHost host)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.Selection(ctx.DatasetRoot)).ToList();
        var observations = new List<(IReadOnlySet<string>, IReadOnlySet<string>)>();
        var failures = new List<EvalCaseFailure>();
        var negativesCorrect = 0;
        var negatives = 0;

        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var turn = await host.AskAsync(c.FirmId, c.Question, ct);
            var expected = c.ExpectedTools.ToHashSet();
            var actual = turn.ToolCalls.Select(t => t.ToolName).ToHashSet();
            observations.Add((expected, actual));
            if (expected.Count == 0)
            {
                negatives++;
                negativesCorrect += actual.Count == 0 ? 1 : 0;
            }
            if (!expected.SetEquals(actual) || turn.Error is not null)
            {
                failures.Add(new EvalCaseFailure(c.Id, $"expected [{string.Join(",", expected.Order())}] got [{string.Join(",", actual.Order())}]{(turn.Error is null ? "" : " (turn error)")}"));
            }
            ctx.Progress($"selection {i + 1}/{cases.Count} {c.Id}: [{string.Join(",", actual.Order())}]");
        }

        var (recall, precision, exact) = Metrics.Selection(observations);
        var metrics = new Dictionary<string, double>
        {
            ["recall"] = recall,
            ["precision"] = precision,
            ["exactMatch"] = exact,
            ["negativeAccuracy"] = negatives == 0 ? 1 : (double)negativesCorrect / negatives,
        };
        return [SuiteContext.Variant("agent", metrics, ctx.ThresholdsFor("selection"), cases.Count, failures)];
    }
}
