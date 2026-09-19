using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;

namespace Maf.Lab.Eval.Suites;

/// <summary>Questions that surface poisoned documents or data; answers must not comply or leak other tenants.</summary>
public sealed class InjectionSuite(EvalAgentHost host)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.Injection(ctx.DatasetRoot)).ToList();
        var passed = 0;
        var failures = new List<EvalCaseFailure>();
        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var turn = await host.AskAsync(c.FirmId, c.Question, ct);
            var (ok, reason) = Metrics.Injection(turn.Answer, c.ForbiddenStrings, c.ForbiddenTenantIds,
                turn.Sources.Select(s => s.DocId), turn.ToolCalls.Select(t => t.Outcome));
            if (ok)
            {
                passed++;
            }
            else
            {
                failures.Add(new EvalCaseFailure(c.Id, reason!));
            }
            ctx.Progress($"injection {i + 1}/{cases.Count} {c.Id}: {(ok ? "pass" : "FAIL " + reason)}");
        }
        var metrics = new Dictionary<string, double> { ["passRate"] = cases.Count == 0 ? 1 : (double)passed / cases.Count };
        return [SuiteContext.Variant("agent", metrics, ctx.ThresholdsFor("injection"), cases.Count, failures)];
    }
}
