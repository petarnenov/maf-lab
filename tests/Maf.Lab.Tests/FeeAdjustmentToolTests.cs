using System.Text.Json;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Billing;
using Maf.Lab.Retrieval.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
// Both libraries have a Role; the caller's role is the one this file means.
using Role = Maf.Lab.Domain.Tenancy.Role;

using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>
/// The write tool: the first call asks, the second applies, and what is applied comes from the state
/// rather than from whatever the model repeated back.
/// </summary>
public class FeeAdjustmentToolTests : IDisposable
{
    private static readonly Principal Adam = new("adam", TenantId.Firm("firm-a"), Role.ADVISOR, []);
    private static readonly Principal Amy = new("amy", TenantId.Firm("firm-a"), Role.ADVISOR, []);
    private static readonly Principal Bianca = new("bianca", TenantId.Firm("firm-b"), Role.ADVISOR, []);
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"maf-lab-tool-{Guid.NewGuid():N}.db");
    private readonly MovableTime _time = new(Noon);

    private FeeAdjustmentLedger Ledger() => new($"Data Source={_path}");

    private AccountFees Fees() => new(BillingAccountTests.Load(), Ledger());

    private ProposalSigner Signer() => new("a-test-signing-key-that-is-long-enough", _time);

    /// <summary>The keys this tool has answered under, so a test can send the same call again.</summary>
    private readonly FakeIdempotencyStore _idempotency = new();

    private FeeAdjustmentTools Tools(Principal principal) => new(
        Fees(), Ledger(), Signer(), new FixedPrincipalAccessor(principal), _idempotency, _time,
        NullLogger<FeeAdjustmentTools>.Instance);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        GC.SuppressFinalize(this);
    }


    [Fact]
    public void A_confirmation_sent_again_under_one_key_is_answered_and_not_applied_twice()
    {
        var state = Propose(Adam).Result.RequestState!;

        var first = Tools(Adam).Propose("A-1042", -200m, "confirmed", Context(state, true, idempotencyKey: "k-1"));
        var again = Tools(Adam).Propose("A-1042", -200m, "confirmed", Context(state, true, idempotencyKey: "k-1"));

        // Byte for byte the first answer: the caller never learns its call was a repeat, which is the point.
        Assert.Equal(Text(first), Text(again));
        Assert.Contains("\"status\":\"applied\"", Text(first));
        Assert.DoesNotContain("already_applied", Text(again));
    }

    [Fact]
    public void A_key_already_used_for_a_different_call_is_refused()
    {
        var first = Propose(Adam).Result.RequestState!;
        Tools(Adam).Propose("A-1042", -200m, "confirmed", Context(first, true, idempotencyKey: "k-2"));

        var other = Propose(Adam, amount: -50m).Result.RequestState!;
        var refused = Tools(Adam).Propose("A-1042", -50m, "confirmed", Context(other, true, amount: -50m, idempotencyKey: "k-2"));

        Assert.True(refused.IsError);
        Assert.Contains("different request", Text(refused));
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>A first call, which never returns: it asks for input.</summary>
    private InputRequiredException Propose(Principal who, string account = "A-1042", decimal amount = -200m, string reason = "client moved to the flat schedule") =>
        Assert.Throws<InputRequiredException>(() => Tools(who).Propose(account, amount, reason, Context(null, null)));

    private static RequestContext<CallToolRequestParams> Context(string? state, bool? approved, string account = "A-1042", decimal amount = -200m, string? action = null, string? idempotencyKey = null)
    {
        var parameters = new CallToolRequestParams
        {
            Name = FeeAdjustmentTools.ProposeName,
            Arguments = new Dictionary<string, JsonElement>
            {
                ["accountId"] = JsonSerializer.SerializeToElement(account),
                ["amount"] = JsonSerializer.SerializeToElement(amount),
                ["reason"] = JsonSerializer.SerializeToElement("client moved to the flat schedule"),
            },
            RequestState = state,
        };
        if (approved is { } answer || action is not null)
        {
            parameters.InputResponses = new Dictionary<string, InputResponse>
            {
                [FeeAdjustmentTools.ConfirmationKey] = InputResponse.FromElicitResult(new ElicitResult
                {
                    Action = action ?? (approved is true ? "accept" : "decline"),
                    Content = approved is true
                        ? new Dictionary<string, JsonElement> { ["approve"] = JsonSerializer.SerializeToElement(true) }
                        : null,
                }),
            };
        }
#pragma warning disable MCPEXP002 // A server the tool never calls; the context only carries the parameters.
        if (idempotencyKey is not null)
        {
            // Where a caller's own key travels: the request's metadata, never an argument a model could invent.
            parameters.Meta = new System.Text.Json.Nodes.JsonObject
            {
                [Maf.Lab.Domain.Billing.FeeAdjustmentTool.IdempotencyMetaKey] = idempotencyKey,
            };
        }
        return new RequestContext<CallToolRequestParams>(
            new FakeMcpServer(),
            new JsonRpcRequest { Method = "tools/call" },
            parameters);
#pragma warning restore MCPEXP002
    }

    private static FeeAdjustmentSummary Summary(InputRequiredException asked)
    {
        var elicit = asked.Result.InputRequests![FeeAdjustmentTools.ConfirmationKey].ElicitationParams;
        Assert.NotNull(elicit);
        var meta = elicit.Meta?[FeeAdjustmentTools.SummaryKey];
        Assert.NotNull(meta);
        return JsonSerializer.Deserialize<FeeAdjustmentSummary>(meta.ToJsonString(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static FeeAdjustmentOutcome Outcome(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        return JsonSerializer.Deserialize<FeeAdjustmentOutcome>(result.StructuredContent.Value.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static string Text(CallToolResult result) => (result.Content.FirstOrDefault() as TextContentBlock)?.Text ?? "";

    // ---- the proposal --------------------------------------------------------------------------------

    [Fact]
    public void A_proposal_asks_for_input_and_writes_nothing()
    {
        var asked = Propose(Adam);

        Assert.NotNull(asked.Result.RequestState);
        Assert.Contains(FeeAdjustmentTools.ConfirmationKey, asked.Result.InputRequests!.Keys);
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void The_summary_says_what_a_person_needs_to_check()
    {
        var summary = Summary(Propose(Adam));

        Assert.Equal("A-1042", summary.AccountId);
        Assert.Equal("Ridgeline Family Trust", summary.AccountName);
        Assert.Equal(1200m, summary.CurrentFee);
        Assert.Equal(-200m, summary.Amount);
        Assert.Equal(1000m, summary.ResultingFee);
        Assert.Equal("USD", summary.Currency);
        Assert.Equal(new DateOnly(2026, 10, 1), summary.PeriodStart);
        Assert.NotEmpty(summary.AdjustmentId);
    }

    [Fact]
    public void The_question_put_to_a_person_reads_as_a_sentence()
    {
        var elicit = Propose(Adam).Result.InputRequests![FeeAdjustmentTools.ConfirmationKey].ElicitationParams;

        Assert.Contains("A-1042", elicit!.Message);
        Assert.Contains("1,200.00 USD", elicit.Message);
        Assert.Contains("1,000.00 USD", elicit.Message);
    }

    [Fact]
    public void The_advisors_reason_is_not_in_what_is_shown_or_in_the_state()
    {
        var asked = Propose(Adam, reason: "SECRET-REASON-TEXT");
        var elicit = asked.Result.InputRequests![FeeAdjustmentTools.ConfirmationKey].ElicitationParams;

        Assert.DoesNotContain("SECRET-REASON-TEXT", elicit!.Message);
        Assert.DoesNotContain("SECRET-REASON-TEXT", asked.Result.RequestState);
    }

    [Theory]
    [InlineData("B-200", -200, "moved schedule", "was not found")]
    [InlineData("A-9999", -200, "moved schedule", "was not found")]
    [InlineData("A-1042", 0, "moved schedule", "would change nothing")]
    [InlineData("A-1042", -200, "  ", "needs a reason")]
    public void A_proposal_that_cannot_stand_is_an_error_that_says_what_is_wrong(string account, decimal amount, string reason, string expected)
    {
        var result = Tools(Adam).Propose(account, amount, reason, Context(null, null));

        Assert.True(result.IsError);
        Assert.Contains(expected, Text(result));
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    // ---- the confirmation ----------------------------------------------------------------------------

    [Fact]
    public void An_approved_proposal_is_applied()
    {
        var asked = Propose(Adam);

        var outcome = Outcome(Tools(Adam).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: true)));

        Assert.Equal("applied", outcome.Status);
        Assert.Equal(1000m, outcome.Adjustment!.CurrentFee);
        Assert.Equal(1000m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void A_declined_proposal_applies_nothing_and_is_not_an_error()
    {
        var asked = Propose(Adam);

        var result = Tools(Adam).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: false));
        var outcome = Outcome(result);

        Assert.Equal("declined", outcome.Status);
        Assert.Null(outcome.Adjustment);
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void A_state_with_no_answer_applies_nothing()
    {
        var asked = Propose(Adam);

        var result = Tools(Adam).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: null));

        Assert.True(result.IsError);
        Assert.Contains("no confirmation was given", Text(result));
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void A_dismissed_question_is_not_a_decision()
    {
        var asked = Propose(Adam);

        // "cancel" means nobody chose — it must not be recorded as the advisor declining.
        var result = Tools(Adam).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: null, action: "cancel"));

        Assert.True(result.IsError);
        Assert.Contains("no confirmation was given", Text(result));
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void What_is_applied_comes_from_the_state_not_from_the_arguments()
    {
        var asked = Propose(Adam, "A-1042", -200m);

        // The model comes back naming another account and a bigger amount.
        var outcome = Outcome(Tools(Adam).Propose("A-1044", -900m, "reason", Context(asked.Result.RequestState, approved: true, account: "A-1044", amount: -900m)));

        Assert.Equal("A-1042", outcome.Adjustment!.AccountId);
        Assert.Equal(-200m, outcome.Adjustment.Amount);
        Assert.Equal(1000m, Fees().Current(Adam, "A-1042")!.Fee);
        Assert.Equal(3120.75m, Fees().Current(Adam, "A-1044")!.Fee);
    }

    [Fact]
    public void An_altered_state_applies_nothing_and_explains_no_mechanism()
    {
        var asked = Propose(Adam);
        var dot = asked.Result.RequestState!.IndexOf('.');
        var tampered = asked.Result.RequestState[..(dot - 1)] + (asked.Result.RequestState[dot - 1] == 'A' ? 'B' : 'A') + asked.Result.RequestState[dot..];

        var result = Tools(Adam).Propose("A-1042", -200m, "reason", Context(tampered, approved: true));

        Assert.True(result.IsError);
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
        foreach (var word in new[] { "signature", "HMAC", "hash", "key" })
        {
            Assert.DoesNotContain(word, Text(result), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void An_expired_state_applies_nothing()
    {
        var asked = Propose(Adam);
        _time.SetUtcNow(Noon.AddHours(2));

        var result = Tools(Adam).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: true));

        Assert.True(result.IsError);
        Assert.Contains("too old", Text(result));
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void Another_firms_confirmation_applies_nothing()
    {
        var asked = Propose(Adam);

        var result = Tools(Bianca).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: true));

        Assert.True(result.IsError);
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void Another_persons_confirmation_applies_nothing()
    {
        var asked = Propose(Adam);

        var result = Tools(Amy).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: true));

        Assert.True(result.IsError);
        Assert.Equal(1200m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void Approving_twice_applies_once()
    {
        var asked = Propose(Adam);

        var first = Outcome(Tools(Adam).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: true)));
        var second = Outcome(Tools(Adam).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: true)));

        Assert.Equal("applied", first.Status);
        Assert.Equal("already_applied", second.Status);
        Assert.Equal(first.Adjustment!.AdjustmentId, second.Adjustment!.AdjustmentId);
        Assert.Equal(first.Adjustment.CurrentFee, second.Adjustment.CurrentFee);
        Assert.Equal(1000m, Fees().Current(Adam, "A-1042")!.Fee);
    }

    [Fact]
    public void A_proposal_made_against_one_instance_is_honoured_by_another()
    {
        // Two tool instances over one signing key and one ledger file are what two replicas look like.
        var asked = Assert.Throws<InputRequiredException>(() =>
            Tools(Adam).Propose("A-1042", -200m, "reason", Context(null, null)));

        var outcome = Outcome(Tools(Adam).Propose("A-1042", -200m, "reason", Context(asked.Result.RequestState, approved: true)));

        Assert.Equal("applied", outcome.Status);
    }
}

/// <summary>A server the tool never talks to: it only needs a request context to read its parameters.</summary>
#pragma warning disable MCPEXP002
internal sealed class FakeMcpServer : McpServer
{
    public override string? SessionId => null;

    public override string NegotiatedProtocolVersion => "2026-07-28";

    public override ClientCapabilities? ClientCapabilities => null;

    public override Implementation? ClientInfo => null;

    public override McpServerOptions ServerOptions { get; } = new();

    public override IServiceProvider? Services => null;

#pragma warning disable MCP9005 // The fake must match the base signature, deprecated or not.
    [Obsolete("Logging is deprecated in 2026-07-28; overridden only to satisfy the base class.")]
    public override LoggingLevel? LoggingLevel => null;
#pragma warning restore MCP9005

    public override Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public override Task SendMessageAsync(ModelContextProtocol.Protocol.JsonRpcMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public override IAsyncDisposable RegisterNotificationHandler(string method, Func<ModelContextProtocol.Protocol.JsonRpcNotification, CancellationToken, ValueTask> handler) =>
        throw new NotSupportedException();

    public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
#pragma warning restore MCPEXP002
