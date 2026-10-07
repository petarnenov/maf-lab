using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

/// <summary>
/// The account in focus (add-focus-state): set by a read, sent as AG-UI state, changeable by the client only to an
/// account the conversation's cards showed, and told to the model.
/// </summary>
public class FocusStateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Lists accounts when asked for them, reads the portfolio of an id the question names, else just answers.</summary>
    private static ScriptedChatClient Model() => new((messages, _, _) =>
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        if (messages.Any(m => m.Role == ChatRole.Tool))
        {
            return ScriptedChatClient.Text("Done.");
        }
        if (last.Contains("accounts", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptedChatClient.Call(PortfolioTools.ListAccounts, []);
        }
        var id = Regex.Match(last, @"[A-Z]-\d+").Value;
        return id.Length > 0 ? ScriptedChatClient.Call(PortfolioTools.GetPortfolio, new() { ["accountId"] = id }) : ScriptedChatClient.Text("Noted.");
    });

    private static JsonElement? Focus(SseEvent snapshot) =>
        snapshot.Data.GetProperty("snapshot").GetProperty("focus") is { ValueKind: JsonValueKind.Object } f ? f : null;

    private static string? FocusId(SseEvent snapshot) => Focus(snapshot)?.GetProperty("accountId").GetString();

    private static List<SseEvent> Snapshots(IEnumerable<SseEvent> events) => [.. events.Where(e => e.Name == "STATE_SNAPSHOT")];

    private static (ApiFactory Api, HttpClient Client) Start()
    {
        var api = new ApiFactory(Model(), new FakeToolSource { WithPortfolio = true });
        return (api, api.ClientFor("adam", "firm-a", Role.USER));
    }

    [Fact]
    public void A_focus_note_names_the_id_and_nothing_else()
    {
        // The focus owner's notes, read outside a turn: the stand-in portfolio domain owns the focus.
        using var domains = Maf.Lab.Api.Agent.DomainCatalogue.Use(StandInDomains.WithBilling);
        Assert.Equal("", Maf.Lab.Api.Agent.ChatTurnRunner.FocusNote(null));
        Assert.Contains("ask which account they mean", Maf.Lab.Api.Agent.ChatTurnRunner.ClearedFocusNote);
        Assert.Equal("\n\n## Conversation focus\nIf the question names no account, it is about account A-1043.",
            Maf.Lab.Api.Agent.ChatTurnRunner.FocusNote("A-1043"));
    }

    [Fact]
    public async Task A_read_puts_its_account_in_focus_right_after_its_card()
    {
        var (api, client) = Start();
        using var _ = api;

        var events = await ApiFactory.ChatAsync(client, "Rebalance A-1043");

        var snapshots = Snapshots(events);
        Assert.Equal(2, snapshots.Count);
        // The run starts with no focus, right after RUN_STARTED.
        Assert.Equal("RUN_STARTED", events[events.IndexOf(snapshots[0]) - 1].Name);
        Assert.Null(FocusId(snapshots[0]));
        // The read moves it, and says so right after its card.
        Assert.Equal("A-1043", FocusId(snapshots[1]));
        Assert.Equal("ACTIVITY_SNAPSHOT", events[events.IndexOf(snapshots[1]) - 1].Name);
    }

    [Fact]
    public async Task The_next_turn_starts_with_the_stored_focus_and_the_model_is_told()
    {
        var (api, client) = Start();
        using var _ = api;
        var first = await ApiFactory.ChatAsync(client, "Rebalance A-1043");
        var conversationId = ApiFactory.ThreadOf(first);

        var next = await ApiFactory.ChatAsync(client, "and its model?", conversationId);

        Assert.Equal("A-1043", FocusId(Snapshots(next)[0]));
        Assert.Contains("it is about account A-1043", api.Chat.Requests[^1].Options!.Instructions);
        var traced = ApiFactory.TracesOf(next).First(t => t.GetProperty("kind").GetString() == TraceKinds.Focus).GetProperty("data");
        Assert.Equal("stored", traced.GetProperty("source").GetString());
        // And the conversation detail restores it.
        var detail = await client.GetFromJsonAsync<ConversationDetail>($"/api/conversations/{conversationId}", new JsonSerializerOptions(JsonSerializerDefaults.Web), Ct);
        Assert.Equal("A-1043", detail!.Focus!.AccountId);
    }

    [Fact]
    public async Task Listing_accounts_does_not_move_the_focus()
    {
        var (api, client) = Start();
        using var _ = api;
        var conversationId = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(client, "Rebalance A-1043"));

        var events = await ApiFactory.ChatAsync(client, "Which accounts do I have?", conversationId);

        Assert.Equal(["A-1043"], Snapshots(events).Select(FocusId));
    }

    [Fact]
    public async Task The_client_may_pick_an_account_a_card_showed()
    {
        var (api, client) = Start();
        using var _ = api;
        var conversationId = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(client, "Which accounts do I have?"));

        // A-1042 was listed in the accounts card.
        var events = await ApiFactory.ChatAsync(client, "what does it hold?", conversationId, state: new { focus = new { accountId = "A-1042" } });

        Assert.Equal("A-1042", FocusId(Snapshots(events)[0]));
        Assert.Contains("it is about account A-1042", api.Chat.Requests[^1].Options!.Instructions);
    }

    [Fact]
    public async Task An_account_no_card_showed_is_refused_and_not_traced()
    {
        var (api, client) = Start();
        using var _ = api;
        var conversationId = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(client, "Rebalance A-1043"));

        var events = await ApiFactory.ChatAsync(client, "what does it hold?", conversationId, state: new { focus = new { accountId = "B-200" } });

        Assert.Equal("A-1043", FocusId(Snapshots(events)[0]));
        var traced = ApiFactory.TracesOf(events).First(t => t.GetProperty("kind").GetString() == TraceKinds.Focus);
        Assert.False(traced.GetProperty("data").GetProperty("accepted").GetBoolean());
        Assert.DoesNotContain("B-200", traced.GetRawText());
    }

    [Fact]
    public async Task The_client_may_clear_the_focus()
    {
        var (api, client) = Start();
        using var _ = api;
        var conversationId = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(client, "Rebalance A-1043"));

        var events = await ApiFactory.ChatAsync(client, "hello", conversationId, state: new { focus = (object?)null });

        Assert.Null(FocusId(Snapshots(events)[0]));
        Assert.DoesNotContain("Conversation focus", api.Chat.Requests[^1].Options!.Instructions);
        // Clearing is an instruction for that turn, said right before the question: ask, do not take an account back
        // from history.
        var sent = api.Chat.Requests[^1].Messages;
        Assert.Equal(ChatRole.System, sent[^2].Role);
        Assert.Contains("do not call a per-account tool", sent[^2].Text);
        Assert.Equal("hello", sent[^1].Text);
        // It is not kept: the next turn, with no focus to clear, has no such note in its request.
        await ApiFactory.ChatAsync(client, "hello again", conversationId, state: new { focus = (object?)null });
        Assert.DoesNotContain(api.Chat.Requests[^1].Messages, m => m.Text.Contains("cleared the account in focus"));
        var detail = await client.GetFromJsonAsync<ConversationDetail>($"/api/conversations/{conversationId}", new JsonSerializerOptions(JsonSerializerDefaults.Web), Ct);
        Assert.Null(detail!.Focus);
    }

    [Fact]
    public async Task A_conversation_that_never_read_an_account_has_no_focus()
    {
        var (api, client) = Start();
        using var _ = api;
        // What an older conversation looks like: the column added for focus holds null.
        var conversationId = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(client, "hello"));

        var detail = await client.GetFromJsonAsync<ConversationDetail>($"/api/conversations/{conversationId}", new JsonSerializerOptions(JsonSerializerDefaults.Web), Ct);

        Assert.Null(detail!.Focus);
    }

    [Fact]
    public async Task After_the_focus_is_cleared_an_account_less_read_is_not_made()
    {
        // A model that, like a real one could, takes A-1043 back from the history.
        var model = new ScriptedChatClient((messages, _, _) =>
            ScriptedChatClient.HasResult(messages, PortfolioTools.GetPortfolio)
                ? ScriptedChatClient.Text("Which account?")
                : ScriptedChatClient.Call(PortfolioTools.GetPortfolio, new() { ["accountId"] = "A-1043" }));
        var tools = new FakeToolSource { WithPortfolio = true };
        using var api = new ApiFactory(model, tools);
        var client = api.ClientFor("adam", "firm-a", Role.USER);
        var conversationId = ApiFactory.ThreadOf(await ApiFactory.ChatAsync(client, "Rebalance A-1043"));
        int Reads() => tools.Invocations.Count(i => i == PortfolioTools.GetPortfolio);
        var before = Reads();

        var events = await ApiFactory.ChatAsync(client, "show its holdings", conversationId, state: new { focus = (object?)null });

        Assert.Equal(before, Reads());
        Assert.Null(FocusId(Snapshots(events)[^1]));
        Assert.Contains(ApiFactory.TracesOf(events), t => t.GetProperty("kind").GetString() == TraceKinds.Focus
            && t.GetProperty("data").GetProperty("source").GetString() == "cleared");
        // A question that names the account is read as usual.
        await ApiFactory.ChatAsync(client, "what does A-1043 hold?", conversationId, state: new { focus = (object?)null });
        Assert.Equal(before + 1, Reads());
    }
}
