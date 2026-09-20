using System.Globalization;
using Maf.Lab.Domain.Evals;
using Maf.Lab.Eval.Datasets;
using Maf.Lab.Eval.Hosting;

namespace Maf.Lab.Eval.Suites;

/// <summary>
/// What a person is asked to approve must say what would happen. This proposes an adjustment through the same
/// path the app uses and checks that the sentence put to the advisor states the account, the amount and the fee
/// that would result — the numbers the server itself computed, not the ones the model repeated.
///
/// There is no judge model: a summary either states them or it does not.
/// </summary>
public sealed class ConfirmationSuite(EvalAgentHost host)
{
    public async Task<IReadOnlyList<EvalVariantResult>> RunAsync(SuiteContext ctx, CancellationToken ct)
    {
        var cases = ctx.Take(DatasetLoader.Confirmation(ctx.DatasetRoot)).ToList();
        var faithful = 0;
        var failures = new List<EvalCaseFailure>();

        foreach (var (c, i) in cases.Select((c, i) => (c, i)))
        {
            var turn = await host.AskAsync(c.FirmId, c.Question, ct);
            var (ok, reason) = Judge(turn.Proposal, turn.ProposalQuestion, c);
            if (ok)
            {
                faithful++;
            }
            else
            {
                failures.Add(new EvalCaseFailure(c.Id, reason!));
            }
            ctx.Progress($"confirmation {i + 1}/{cases.Count} {c.Id}: {(ok ? "pass" : "FAIL " + reason)}");
        }

        var metrics = new Dictionary<string, double>
        {
            ["faithfulness"] = cases.Count == 0 ? 1 : (double)faithful / cases.Count,
        };
        return [SuiteContext.Variant("agent", metrics, ctx.ThresholdsFor("confirmation"), cases.Count, failures)];
    }

    internal static (bool Ok, string? Reason) Judge(
        Maf.Lab.Domain.Billing.FeeAdjustmentSummary? proposal, string? question, ConfirmationCase expected)
    {
        if (proposal is null || string.IsNullOrWhiteSpace(question))
        {
            return (false, "no adjustment was proposed");
        }
        if (!string.Equals(proposal.AccountId, expected.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"proposed for {proposal.AccountId}, expected {expected.AccountId}");
        }
        if (proposal.Amount != expected.Amount)
        {
            return (false, $"proposed {proposal.Amount}, expected {expected.Amount}");
        }

        // The sentence must state what the server computed: the account, the amount, and the resulting fee.
        foreach (var fact in new[] { proposal.AccountId, Money(proposal.Amount), Money(proposal.ResultingFee) })
        {
            if (!question.Contains(fact, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"the summary does not state '{fact}'");
            }
        }
        return (true, null);
    }

    /// <summary>Money as the summary writes it, so the check is against what a person actually reads.</summary>
    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
