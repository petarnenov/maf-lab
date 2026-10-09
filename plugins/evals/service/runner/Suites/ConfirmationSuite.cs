using System.Text.Json;
using Maf.Lab.Api.Agent.Writes;
using Maf.Lab.Plugins.Abstractions;
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
            var turn = await host.AskAsync(c.TenantId, c.Question, ct);
            var facts = turn.Proposal is { } write ? host.FactsFor(write.ToolName) : null;
            var (ok, reason) = Judge(turn.Proposal, turn.ProposalQuestion, c, facts);
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

    /// <summary>
    /// The case's account and amount against the proposal's summary, then the facts the write's flow says the question
    /// must state (<see cref="IStatesConfirmationFacts"/>, generalize-write-confirmation) against the question itself.
    /// </summary>
    internal static (bool Ok, string? Reason) Judge(
        PendingWrite? proposal, string? question, ConfirmationCase expected, IStatesConfirmationFacts? facts)
    {
        if (proposal is null || string.IsNullOrWhiteSpace(question))
        {
            return (false, "no adjustment was proposed");
        }
        var account = Str(proposal.Summary, "accountId");
        if (!string.Equals(account, expected.AccountId, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"proposed for {account}, expected {expected.AccountId}");
        }
        var amount = proposal.Summary.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var d) ? d : (decimal?)null;
        if (amount != expected.Amount)
        {
            return (false, $"proposed {amount}, expected {expected.Amount}");
        }
        if (facts is null)
        {
            return (false, $"no flow says what a {proposal.ToolName} question must state");
        }

        // The sentence must state what the server computed, as the write's flow names it.
        foreach (var fact in facts.FactsToState(proposal.Summary))
        {
            if (!question.Contains(fact, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"the summary does not state '{fact}'");
            }
        }
        return (true, null);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
