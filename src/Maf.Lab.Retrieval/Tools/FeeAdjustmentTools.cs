using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Billing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.Retrieval.Tools;

/// <summary>What came of a confirmed proposal. A declined one is an outcome, not an error.</summary>
public sealed record FeeAdjustmentOutcome(string Status, FeeAdjustmentApplied? Adjustment, string Message);

/// <summary>
/// The one tool in this server that changes something — and it cannot do it alone.
///
/// The first call fixes the proposal and asks for input (MRTR): nothing is written. The proposal travels
/// back as opaque, signed state, so the second call applies what was proposed rather than what the model
/// happens to repeat. Applying is the ledger's business, and it applies once.
/// </summary>
[McpServerToolType]
public sealed class FeeAdjustmentTools(
    AccountFees fees,
    FeeAdjustmentLedger ledger,
    ProposalSigner signer,
    IPrincipalAccessor principals,
    TimeProvider time,
    ILogger<FeeAdjustmentTools> logger)
{
    public const string ProposeName = FeeAdjustmentTool.Name;
    public const string ConfirmationKey = FeeAdjustmentTool.ConfirmationKey;
    public const string SummaryKey = FeeAdjustmentTool.SummaryKey;
    public const string StateKey = FeeAdjustmentTool.StateKey;

    [McpServerTool(
        Name = ProposeName,
        Title = "Propose a fee adjustment",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(FeeAdjustmentOutcome))]
    [Description(
        "Proposes an adjustment to one account's fee and asks for confirmation. It changes nothing on its own: the first call returns the proposal, and only a proposal a person confirmed is applied.\n" +
        "Use when: the user asks to adjust, correct, credit or reduce the fee on an account they name (e.g. 'credit 200 off the fee on A-1042').\n" +
        "Do not use for: how adjustments work, when they are allowed, or who approves them — use search_documents. For a billing run's state, use get_billing_run_status or search_billing_runs.")]
    public CallToolResult Propose(
        [Description("The account id, e.g. 'A-1042'.")] string accountId,
        [Description("The adjustment to the fee in the account's currency: negative to reduce it, positive to increase it.")] decimal amount,
        [Description("Why the adjustment is being made, in the advisor's own words.")] string reason,
        RequestContext<CallToolRequestParams>? context = null)
    {
        try
        {
            return context?.Params?.RequestState is { Length: > 0 } state
                ? Confirmed(state, context)
                : Proposed(accountId, amount, reason);
        }
        catch (InputRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("propose_fee_adjustment failed: {ErrorType}", ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Fee adjustment"));
        }
    }

    /// <summary>First call: check what is being asked, fix it, and ask a person. Nothing is written here.</summary>
    private CallToolResult Proposed(string accountId, decimal amount, string reason)
    {
        var principal = principals.Current;

        if (string.IsNullOrWhiteSpace(accountId) || fees.Current(principal, accountId) is not { } account)
        {
            return ToolErrors.Error($"Account '{accountId}' was not found. Check the account id.");
        }
        if (amount == 0m)
        {
            return ToolErrors.Error("An adjustment of zero would change nothing. Give the amount to credit or add.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ToolErrors.Error("An adjustment needs a reason. Ask the advisor why it is being made.");
        }

        var now = time.GetUtcNow();
        var summary = new FeeAdjustmentSummary(
            AdjustmentId: $"adj_{Guid.NewGuid():N}",
            AccountId: account.AccountId,
            AccountName: account.Name,
            CurrentFee: account.Fee,
            Amount: amount,
            ResultingFee: account.Fee + amount,
            Currency: account.Currency,
            PeriodStart: account.NextPeriodStart,
            PeriodEnd: account.NextPeriodEnd);

        var state = signer.Issue(new FeeAdjustmentProposalState(
            summary.AdjustmentId,
            principal.FirmId.Value,
            principal.UserId,
            account.AccountId,
            amount,
            account.Currency,
            ProposalSigner.DigestOf(reason),
            now,
            now.Add(signer.ValidFor)));

        var elicit = new ElicitRequestParams
        {
            Message = Sentence(summary),
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["approve"] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = "Apply this adjustment",
                        Description = "Yes applies it; anything else leaves the fee as it is.",
                    },
                },
                Required = ["approve"],
            },
            // The structured summary for whatever shows this to a person. Never the advisor's reason.
            Meta = new JsonObject
            {
                [SummaryKey] = JsonSerializer.SerializeToNode(summary, McpJson.Options),
                [StateKey] = state,
            },
        };

        // Not a result: the request is not finished until a person has answered it.
        throw new InputRequiredException(
            new Dictionary<string, InputRequest> { [ConfirmationKey] = InputRequest.ForElicitation(elicit) },
            state);
    }

    /// <summary>Second call: the state says what was proposed, and only an accepted answer applies it.</summary>
    private CallToolResult Confirmed(string state, RequestContext<CallToolRequestParams> context)
    {
        var principal = principals.Current;

        var checkedState = signer.Verify(state);
        if (checkedState is not ProposalCheck.Ok ok)
        {
            return checkedState is ProposalCheck.Expired
                ? ToolErrors.Error("That proposal is too old to apply. Propose the adjustment again.")
                : ToolErrors.Error("That confirmation does not match a proposal this server issued. Propose the adjustment again.");
        }

        var proposal = ok.Proposal;

        // A proposal belongs to the person it was put to, in the firm it was made for.
        if (proposal.FirmId != principal.FirmId.Value || proposal.UserId != principal.UserId)
        {
            logger.LogWarning("propose_fee_adjustment: a confirmation was presented by someone other than the proposer");
            return ToolErrors.Error("That confirmation does not match a proposal this server issued. Propose the adjustment again.");
        }

        if (Answer(context) is not { } approved)
        {
            return ToolErrors.Error("Nothing was applied: no confirmation was given for that proposal.");
        }

        if (!approved)
        {
            return SearchDocumentsTool.Structured(new FeeAdjustmentOutcome(
                "declined",
                null,
                "The advisor declined the adjustment. Nothing was applied."));
        }

        var seededFee = fees.SeededFee(principal, proposal.AccountId);
        if (seededFee is null)
        {
            return ToolErrors.Error($"Account '{proposal.AccountId}' was not found. Check the account id.");
        }

        var applied = ledger.Apply(
            principal,
            proposal.AdjustmentId,
            proposal.AccountId,
            proposal.Amount,
            seededFee.Value,
            proposal.Currency,
            time.GetUtcNow());

        return SearchDocumentsTool.Structured(new FeeAdjustmentOutcome(
            applied.AlreadyApplied ? "already_applied" : "applied",
            applied,
            applied.AlreadyApplied
                ? $"That adjustment was already applied; the fee on {applied.AccountId} is {Amount(applied.CurrentFee, applied.Currency)}."
                : $"Applied. The fee on {applied.AccountId} is now {Amount(applied.CurrentFee, applied.Currency)}."));
    }

    /// <summary>
    /// True when a person accepted, false when they declined, null when nobody was asked — a dismissed or
    /// cancelled elicitation is not a decision, and must not be recorded as one.
    /// </summary>
    private static bool? Answer(RequestContext<CallToolRequestParams> context)
    {
        if (context.Params?.InputResponses is not { } responses
            || !responses.TryGetValue(ConfirmationKey, out var response)
            || response.Deserialize(InputResponse.ElicitResultJsonTypeInfo) is not { } result)
        {
            return null;
        }

        return result.Action switch
        {
            // An accepted elicitation whose own answer says no is still a no.
            "accept" => result.Content is not { } content
                || !content.TryGetValue("approve", out var approve)
                || approve.ValueKind != JsonValueKind.False,
            "decline" => false,
            _ => null,
        };
    }

    private static string Sentence(FeeAdjustmentSummary s) =>
        $"Apply a fee adjustment of {Amount(s.Amount, s.Currency)} to {s.AccountId} ({s.AccountName})? " +
        $"The fee for {s.PeriodStart:yyyy-MM-dd} to {s.PeriodEnd:yyyy-MM-dd} would change from " +
        $"{Amount(s.CurrentFee, s.Currency)} to {Amount(s.ResultingFee, s.Currency)}.";

    private static string Amount(decimal value, string currency) =>
        $"{value.ToString("N2", CultureInfo.InvariantCulture)} {currency}";
}
