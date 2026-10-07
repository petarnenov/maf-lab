using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Agent.Streaming;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// Data cards (add-activity-cards): which tool results may travel to the client as an AG-UI activity, and what a card
/// carries when one does.
/// </summary>
public class ActivityCardTests
{
    // ---- the card on the wire --------------------------------------------------------------------------------------

    private const string Question = "Препоръчай ребалансиране за A-1043";

    /// <summary>Reads A-1043's portfolio, then answers.</summary>
    private static ScriptedChatClient PortfolioModel() => new((messages, _, _) =>
        ScriptedChatClient.HasResult(messages, PortfolioTools.GetPortfolio)
            ? ScriptedChatClient.Text("Не е необходимо ребалансиране.")
            : ScriptedChatClient.Call(PortfolioTools.GetPortfolio, new() { ["accountId"] = "A-1043" }));

    private static List<SseEvent> Named(IEnumerable<SseEvent> events, string name) => [.. events.Where(e => e.Name == name)];

    [Fact]
    public async Task A_portfolio_read_sends_one_holdings_card_right_after_its_result()
    {
        var tools = new FakeToolSource { WithPortfolio = true };
        using var api = new ApiFactory(PortfolioModel(), tools);

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), Question);

        var card = Assert.Single(Named(events, "ACTIVITY_SNAPSHOT"));
        Assert.Equal("maf-lab/holdings", card.Data.GetProperty("activityType").GetString());
        var content = card.Data.GetProperty("content");
        Assert.Equal("A-1043", content.GetProperty("accountId").GetString());
        Assert.False(content.GetProperty("rebalanceNeeded").GetBoolean());
        Assert.Equal(-8000m, content.GetProperty("holdings")[0].GetProperty("tradeToTarget").GetDecimal());
        // Right after its call's result, keyed by that call, and before the answer's text.
        var result = Named(events, "TOOL_CALL_RESULT").Single();
        var callId = result.Data.GetProperty("toolCallId").GetString();
        Assert.Equal($"card-{callId}", card.Data.GetProperty("messageId").GetString());
        Assert.Equal(events.IndexOf(result) + 1, events.IndexOf(card));
        Assert.True(events.IndexOf(card) < events.FindIndex(e => e.Name == "TEXT_MESSAGE_CONTENT"));
        // The result event itself stays a summary: the numbers travel only in the card.
        Assert.DoesNotContain("tradeToTarget", result.Data.GetProperty("content").GetRawText());
        // And the trace records the card, numbers and all.
        var traced = ApiFactory.TracesOf(events).Single(t => t.GetProperty("kind").GetString() == TraceKinds.Card);
        Assert.Equal("maf-lab/holdings", traced.GetProperty("data").GetProperty("activityType").GetString());
    }

    [Fact]
    public async Task A_documentation_search_sends_no_card()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), new FakeToolSource { WithPortfolio = true });

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "what is the procedure when a fee schedule is missing");

        Assert.NotEmpty(Named(events, "TOOL_CALL_RESULT"));
        Assert.Empty(Named(events, "ACTIVITY_SNAPSHOT"));
    }

    [Fact]
    public async Task A_failed_read_sends_no_card()
    {
        using var api = new ApiFactory(PortfolioModel(), new FakeToolSource { WithPortfolio = true, PortfolioFails = true });

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), Question);

        Assert.Single(Named(events, "TOOL_CALL_RESULT"));
        Assert.Empty(Named(events, "ACTIVITY_SNAPSHOT"));
    }

    [Fact]
    public async Task A_withheld_result_sends_no_card()
    {
        var jev = new FakeJev { Guard = (text, id) => id == "guard_to_ai" && text.Contains("Calder", StringComparison.Ordinal) ? 0.97 : 0.02 };
        using var api = new ApiFactory(PortfolioModel(), new FakeToolSource { WithPortfolio = true }, jev: jev);

        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), Question);

        Assert.Empty(Named(events, "ACTIVITY_SNAPSHOT"));
        Assert.DoesNotContain(ApiFactory.TracesOf(events), t => t.GetProperty("kind").GetString() == TraceKinds.Card);
    }

    [Fact]
    public async Task A_reopened_conversation_shows_the_card_again()
    {
        using var api = new ApiFactory(PortfolioModel(), new FakeToolSource { WithPortfolio = true });
        var client = api.ClientFor("adam", "firm-a", Role.USER);
        var events = await ApiFactory.ChatAsync(client, Question);
        var conversationId = events.Single(e => e.Name == "RUN_FINISHED").Data.GetProperty("threadId").GetString();

        var detail = await client.GetFromJsonAsync<ConversationDetail>($"/api/conversations/{conversationId}", new JsonSerializerOptions(JsonSerializerDefaults.Web), TestContext.Current.CancellationToken);

        var activity = Assert.Single(detail!.Turns.Single().Activities!);
        Assert.Equal("maf-lab/holdings", activity.ActivityType);
        Assert.Equal("A-1043", activity.Content.GetProperty("accountId").GetString());
    }

    [Fact]
    public async Task A_turn_stored_before_cards_opens_with_none()
    {
        using var api = new ApiFactory(PortfolioModel(), new FakeToolSource { WithPortfolio = true });
        var client = api.ClientFor("adam", "firm-a", Role.USER);
        var events = await ApiFactory.ChatAsync(client, Question);
        var conversationId = events.Single(e => e.Name == "RUN_FINISHED").Data.GetProperty("threadId").GetString();
        // What the added column holds for a row written before it existed.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
            await db.Turns.Where(t => t.ConversationId == conversationId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.ActivitiesJson, ""), TestContext.Current.CancellationToken);
        }

        var detail = await client.GetFromJsonAsync<ConversationDetail>($"/api/conversations/{conversationId}", new JsonSerializerOptions(JsonSerializerDefaults.Web), TestContext.Current.CancellationToken);

        Assert.Empty(detail!.Turns.Single().Activities!);
    }
}
