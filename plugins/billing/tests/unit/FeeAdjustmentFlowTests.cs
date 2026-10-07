using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// Between a proposal and a person. Whether the reviewer is troubled, what each of its answers means, and
/// the fact that none of them writes anything.
/// </summary>
public class FeeAdjustmentFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A model that proposes when asked to adjust a fee, and otherwise answers.</summary>
    private static ScriptedChatClient ProposingModel() =>
        new((messages, options, _) =>
        {
            var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            if (last.Contains("adjust", StringComparison.OrdinalIgnoreCase) && !ScriptedChatClient.HasResult(messages, FeeAdjustmentTool.Name))
            {
                var increase = last.Contains("increase", StringComparison.OrdinalIgnoreCase);
                var large = last.Contains("large", StringComparison.OrdinalIgnoreCase) || increase;
                return ScriptedChatClient.Call(FeeAdjustmentTool.Name, new()
                {
                    ["accountId"] = "A-1042",
                    ["amount"] = increase ? 1200m : large ? -900m : -200m,
                    ["reason"] = "the client was overcharged in Q2",
                });
            }
            return ScriptedChatClient.Text("Done.");
        });

    /// <summary>
    /// The reviewer at the port (<see cref="ScriptedReviewer"/>): it refuses above its own limit, asks first when told to,
    /// or reports the deadline passed — the reviewer agent's behaviours, without the agent (its own tests are the
    /// compliance plugin's).
    /// </summary>
    private static ScriptedReviewer Reviewer(bool askFirst = false, bool late = false, decimal refuseAbove = 1_000m) =>
        new() { AskFirst = askFirst, Late = late, RefuseAbove = refuseAbove };

    /// <summary>Billing's own flow for the fakes' write tool, with the given reviewer at the port (none: no reviewer installed).</summary>
    private static ApiFactory ApiFor(ScriptedReviewer? reviewer, FakeToolSource tools, decimal threshold = 500m)
    {
        var settings = new Dictionary<string, string?>
        {
            ["FeeAdjustments:ReviewAboveAmount"] = threshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return new ApiFactory(ProposingModel(), tools)
        {
            ExtraSettings = settings,
            ConfigureTestServices = services =>
            {
                Maf.Lab.Plugins.Billing.FeeAdjustmentFlow.Install(services);
                if (reviewer is not null)
                {
                    services.AddSingleton<IReviewerConsultation>(reviewer);
                }
            },
        };
    }

    private static async Task<List<AuditRow>> AuditAsync(ApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        return await db.Audit.OrderBy(a => a.Id).ToListAsync(Ct);
    }

    [Fact]
    public async Task An_adjustment_within_the_threshold_goes_straight_to_the_advisor()
    {
        var reviewer = Reviewer();
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by 200");

        var interrupt = ApiFactory.InterruptOf(events);
        Assert.NotNull(interrupt);
        Assert.Equal("A-1042", interrupt.Value.GetProperty("metadata").GetProperty("summary").GetProperty("accountId").GetString());
        Assert.Equal("RUN_FINISHED", events[^1].Name);

        // The reviewer was not troubled for a small one.
        Assert.DoesNotContain(await AuditAsync(api), a => a.Kind == "a2a.consultation");
    }

    [Fact]
    public async Task A_large_adjustment_is_reviewed_before_the_advisor_is_asked()
    {
        var reviewer = Reviewer();
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        Assert.NotNull(ApiFactory.InterruptOf(events));
        var audit = await AuditAsync(api);
        // Consulted once, before the advisor was asked (the consultation's own record is the compliance plugin's).
        Assert.Single(reviewer.Calls);
        Assert.Contains(audit, a => a.ToolName == "fee.adjustment.proposed");
        Assert.Contains(audit, a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "approved");
    }

    [Fact]
    public async Task A_refused_review_is_the_end_of_it()
    {
        var reviewer = Reviewer(refuseAbove: 1_000m);
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        // An increase of 1,200 is over the reviewer's own threshold.
        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042: increase it");

        Assert.Null(ApiFactory.InterruptOf(events));
        Assert.Contains(await AuditAsync(api), a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "refused");
        await AssertNothingPendingAsync(api, PendingWriteStatus.Refused);
    }

    [Fact]
    public async Task A_reviewer_that_is_not_there_is_the_end_of_it_too()
    {
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer: null, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        Assert.Null(ApiFactory.InterruptOf(events));
        Assert.Contains(await AuditAsync(api), a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "unreachable");
    }

    [Fact]
    public async Task A_review_that_outlives_the_turn_is_not_a_verdict()
    {
        var reviewer = Reviewer(late: true);
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        Assert.Null(ApiFactory.InterruptOf(events));
        Assert.Contains(await AuditAsync(api), a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "timeout");
    }

    [Fact]
    public async Task A_question_from_the_reviewer_reaches_the_advisor_and_no_confirmation_is_offered()
    {
        var reviewer = Reviewer(askFirst: true);
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        Assert.Null(ApiFactory.InterruptOf(events));
        Assert.Contains(await AuditAsync(api), a => a.ToolName == "fee.adjustment.reviewed" && a.Outcome == "input-required");

        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var pending = await db.PendingWrites.SingleAsync(Ct);
        Assert.Equal(PendingWriteStatus.AwaitingInput, pending.Status);
        // The shape the store's backfill writes for proposals kept from before the seam: the two must not drift.
        var review = System.Text.Json.Nodes.JsonNode.Parse(pending.FlowJson!)!.AsObject();
        Assert.Equal(["reviewTaskId", "questions"], review.Select(p => p.Key));
        Assert.False(string.IsNullOrEmpty(review["reviewTaskId"]!.GetValue<string>()));
        Assert.Equal(1, review["questions"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_advisors_justification_continues_the_same_review()
    {
        // Asks the first time, answers the second: the review is one review, not two.
        var reviewer = Reviewer(askFirst: true);
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var first = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");
        var conversationId = ApiFactory.ThreadOf(first);

        var second = await ApiFactory.ChatAsync(client,
            "adjust the fee on A-1042 down by a large amount — the client agreed in writing", conversationId);

        // The same review reached a verdict, so the advisor is now asked to confirm.
        Assert.NotNull(ApiFactory.InterruptOf(second));
        var reviews = (await AuditAsync(api)).Where(a => a.ToolName == "fee.adjustment.reviewed").ToList();
        Assert.Equal(["input-required", "approved"], reviews.Select(r => r.Outcome));
        // The advisor's words went to the reviewer as the answer on the review's own task, not as a second review.
        Assert.Equal([null, ScriptedReviewer.TaskId], reviewer.Calls.Select(c => c.TaskId));
        Assert.Contains("agreed in writing", reviewer.Calls.Last().Justification);
    }

    [Fact]
    public async Task No_step_of_a_write_carries_the_advisors_words()
    {
        var reviewer = Reviewer();
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        foreach (var row in await AuditAsync(api))
        {
            Assert.DoesNotContain("overcharged", row.Arguments, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Q2", row.Arguments, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Every_step_is_attributed_to_the_person_who_acted()
    {
        var reviewer = Reviewer();
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by 200");

        var steps = (await AuditAsync(api)).Where(a => a.Kind == "fee.adjustment").ToList();
        Assert.NotEmpty(steps);
        Assert.All(steps, s =>
        {
            Assert.Equal("adam", s.PrincipalId);
            Assert.Equal("firm-a", s.TenantId);
            Assert.Contains("accountId=A-1042", s.Arguments);
        });
    }

    [Fact]
    public async Task A_review_never_produces_a_proposal_of_its_own()
    {
        var reviewer = Reviewer();
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        // Whatever the verdict's words say, only the tool makes proposals — and it was called once.
        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var pending = await db.PendingWrites.ToListAsync(Ct);
        Assert.Single(pending);
        Assert.Contains("A-1042", pending[0].Summary);
        Assert.DoesNotContain("B-200", pending[0].Summary);
        Assert.Equal([FeeAdjustmentTool.Name], tools.Invocations);
    }

    [Fact]
    public async Task Every_step_of_a_write_shows_in_the_trace_in_order()
    {
        var reviewer = Reviewer();
        var tools = new FakeToolSource();
        using var api = ApiFor(reviewer, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by a large amount");

        var trace = ApiFactory.TracesOf(events).ToList();
        var steps = trace
            .Where(t => t.GetProperty("kind").GetString() == "adjustment")
            .Select(t => t.GetProperty("data").GetProperty("step").GetString())
            .ToList();

        Assert.Equal(["proposed", "reviewed", "awaiting_confirmation"], steps);

        var seqs = trace.Select(t => t.GetProperty("seq").GetInt32()).ToList();
        Assert.Equal(seqs.Order(), seqs);

        var reviewed = trace.Single(t => t.GetProperty("kind").GetString() == "adjustment"
            && t.GetProperty("data").GetProperty("step").GetString() == "reviewed").GetProperty("data");
        Assert.NotEqual("", reviewed.GetProperty("taskId").GetString());
        Assert.Equal("approved", reviewed.GetProperty("outcome").GetString());
    }

    private static async Task AssertNothingPendingAsync(ApiFactory api, string expected)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        Assert.All(await db.PendingWrites.ToListAsync(Ct), p => Assert.Equal(expected, p.Status));
    }
}
