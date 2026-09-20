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

/// <summary>A proposal outlives the page that made it: the run is gone, the thing waiting is not.</summary>
public class PendingProposalApiTests
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
                    ["reason"] = "overcharged in Q2",
                });
            }
            return ScriptedChatClient.Text("Done.");
        });

    private static ApiFactory Api(FakeToolSource tools) =>
        new(ProposingModel(), tools) { ExtraSettings = new Dictionary<string, string?> { ["Compliance:BaseUrl"] = "" } };

    private static async Task<(string ConversationId, string AdjustmentId)> ProposeAsync(HttpClient client)
    {
        var events = await ApiFactory.ChatAsync(client, "adjust the fee on A-1042 down by 200");
        var interrupt = ApiFactory.InterruptOf(events);
        Assert.NotNull(interrupt);
        return (ApiFactory.ThreadOf(events), interrupt.Value.GetProperty("id").GetString()!);
    }

    private static async Task<JsonElement> PendingAsync(HttpClient client, string conversationId)
    {
        var response = await client.GetAsync($"/api/conversations/{conversationId}/pending", Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("pending");
    }

    [Fact]
    public async Task A_waiting_proposal_comes_back_whole()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        var pending = await PendingAsync(client, conversationId);

        Assert.Equal(adjustmentId, pending.GetProperty("adjustmentId").GetString());
        var adjustment = pending.GetProperty("adjustment");
        Assert.Equal("A-1042", adjustment.GetProperty("accountId").GetString());
        Assert.Equal(-200m, adjustment.GetProperty("amount").GetDecimal());
        Assert.Equal(1000m, adjustment.GetProperty("resultingFee").GetDecimal());
        Assert.Contains("A-1042", pending.GetProperty("question").GetString());
        Assert.NotEqual(JsonValueKind.Null, pending.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task Nothing_is_waiting_in_a_conversation_that_proposed_nothing()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var events = await ApiFactory.ChatAsync(client, "hello");

        var pending = await PendingAsync(client, ApiFactory.ThreadOf(events));

        Assert.Equal(JsonValueKind.Null, pending.ValueKind);
    }

    [Fact]
    public async Task An_answered_proposal_is_no_longer_waiting()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, adjustmentId) = await ProposeAsync(client);

        await ApiFactory.ResumeAsync(client, conversationId, adjustmentId, approve: true);

        Assert.Equal(JsonValueKind.Null, (await PendingAsync(client, conversationId)).ValueKind);
    }

    [Fact]
    public async Task An_expired_proposal_is_not_offered()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var (conversationId, _) = await ProposeAsync(client);

        // Move the proposal's expiry into the past, as time would.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
            var row = await db.PendingAdjustments.SingleAsync(Ct);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(JsonValueKind.Null, (await PendingAsync(client, conversationId)).ValueKind);
    }

    [Fact]
    public async Task Another_persons_conversation_is_not_found()
    {
        var tools = new FakeToolSource();
        using var api = Api(tools);
        var (conversationId, _) = await ProposeAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR));

        var response = await api.ClientFor("amy", "firm-a", Role.ADVISOR)
            .GetAsync($"/api/conversations/{conversationId}/pending", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
