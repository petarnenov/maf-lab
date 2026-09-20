using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>A person's answer to a proposal: it applies once, it applies nothing when they say no, and it is theirs alone.</summary>
public class ConfirmationApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ScriptedChatClient ProposingModel() =>
        new((messages, options, _) =>
        {
            var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            if (last.Contains("adjust", StringComparison.OrdinalIgnoreCase) && !ScriptedChatClient.HasResult(messages, FeeAdjustmentTool.Name))
            {
                return ScriptedChatClient.Call(FeeAdjustmentTool.Name, new()
                {
                    ["accountId"] = "A-1042",
                    ["amount"] = -200m,
                    ["reason"] = "the client was overcharged in Q2",
                });
            }
            return ScriptedChatClient.Text("Done.");
        });

    private static ApiFactory Api(FakeToolSource tools) =>
        new(ProposingModel(), tools) { ExtraSettings = new Dictionary<string, string?> { ["Compliance:BaseUrl"] = "" } };

    /// <summary>A run that pauses on a proposal. The interrupt's id is what an answer names.</summary>
    private static async Task<(string ConversationId, string AdjustmentId)> ProposeAsync(HttpClient client)
    {
        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by 200");
        var interrupt = ApiFactory.InterruptOf(events);
        Assert.NotNull(interrupt);
        return (ApiFactory.ThreadOf(events), interrupt.Value.GetProperty("id").GetString()!);
    }

    /// <summary>The answer: a run that resumes the interrupt.</summary>
    private static Task<List<SseEvent>> AnswerAsync(HttpClient client, string conversationId, string adjustmentId, bool approve) =>
        ApiFactory.ResumeAsync(client, conversationId, adjustmentId, approve);

    [Fact]
    public async Task Approving_applies_the_adjustment()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        var events = await AnswerAsync(client, conversationId, adjustmentId, approve: true);

        Assert.Equal("RUN_FINISHED", events[^1].Name);
        Assert.Contains("Applied", ApiFactory.AnswerOf(events));
        Assert.Single(tools.Applied);
    }

    [Fact]
    public async Task Approving_twice_applies_once()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        var first = await AnswerAsync(client, conversationId, adjustmentId, approve: true);
        var second = await AnswerAsync(client, conversationId, adjustmentId, approve: true);

        Assert.Contains("Applied", ApiFactory.AnswerOf(first));
        // The proposal is no longer waiting, so the second answer finds nothing to answer.
        Assert.Contains("no longer waiting", ApiFactory.AnswerOf(second));
        Assert.Single(tools.Applied);
    }

    [Fact]
    public async Task Rejecting_applies_nothing()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        var events = await AnswerAsync(client, conversationId, adjustmentId, approve: false);

        Assert.Contains("declined", ApiFactory.AnswerOf(events));
        Assert.Empty(tools.Applied);
    }

    [Fact]
    public async Task Someone_elses_proposal_is_not_theirs_to_answer()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, adjustmentId) = await ProposeAsync(adam);

        // The proposal was put to adam, in adam's conversation. Amy cannot even reach the thread.
        var amy = api.ClientFor("amy", "firm-a", Role.ADVISOR);
        var response = await amy.PostAsJsonAsync("/api/chat", new
        {
            threadId = conversationId,
            runId = "r_amy",
            messages = Array.Empty<object>(),
            resume = new[] { new { interruptId = adjustmentId, payload = new { approve = true } } },
        }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(tools.Applied);
    }

    [Fact]
    public async Task An_unknown_proposal_is_not_found()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, _) = await ProposeAsync(client);

        var events = await AnswerAsync(client, conversationId, "adj_nothing", approve: true);

        Assert.Contains("no longer waiting", ApiFactory.AnswerOf(events));
    }

    [Fact]
    public async Task Both_the_answer_and_what_came_of_it_are_recorded_against_the_person()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        await AnswerAsync(client, conversationId, adjustmentId, approve: true);

        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var steps = await db.Audit.Where(a => a.Kind == "fee.adjustment").OrderBy(a => a.Id).ToListAsync(Ct);

        Assert.Equal(
            ["fee.adjustment.proposed", "fee.adjustment.confirmed", "fee.adjustment.applied"],
            steps.Select(s => s.ToolName));
        Assert.All(steps, s => Assert.Equal("adam", s.PrincipalId));
        Assert.All(steps, s => Assert.DoesNotContain("overcharged", s.Arguments, StringComparison.OrdinalIgnoreCase));

        var report = Maf.Lab.Api.Compliance.AuditChain.Verify(await db.Audit.OrderBy(a => a.Id).ToListAsync(Ct));
        Assert.True(report.Intact);
    }

    [Fact]
    public async Task A_rejection_is_recorded_as_a_rejection()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        await AnswerAsync(client, conversationId, adjustmentId, approve: false);

        await using var scope = api.Services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        var steps = await db.Audit.Where(a => a.Kind == "fee.adjustment").OrderBy(a => a.Id).ToListAsync(Ct);

        Assert.Equal(["fee.adjustment.proposed", "fee.adjustment.rejected"], steps.Select(s => s.ToolName));
        Assert.DoesNotContain(steps, s => s.ToolName == "fee.adjustment.applied");
    }
}
