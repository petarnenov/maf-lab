using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Role = Maf.Lab.Domain.Tenancy.Role;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>strip-citation-markers: gpt-oss's inline 【…】 markers never reach the user.</summary>
public class CitationMarkerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChatResponseUpdate Delta(string text) => new(ChatRole.Assistant, text);

    private static async Task<(string Text, int Removed, List<ChatResponseUpdate> Updates)> StreamAsync(params ChatResponseUpdate[] updates)
    {
        var removed = 0;
        using var client = new CitationMarkerChatClient(new ScriptedChatClient((_, _, _) => updates), n => removed += n);
        var seen = new List<ChatResponseUpdate>();
        await foreach (var u in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "q")], cancellationToken: Ct))
        {
            seen.Add(u);
        }
        return (string.Concat(seen.SelectMany(u => u.Contents).OfType<TextContent>().Select(t => t.Text)), removed, seen);
    }

    [Fact]
    public async Task A_marker_at_a_sentence_end_goes_with_the_space_before_it()
    {
        var (text, removed, _) = await StreamAsync(Delta("the failure code is FS-REQUIRED 【tool_data†get_billing_run_status】."));
        Assert.Equal("the failure code is FS-REQUIRED.", text);
        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task A_marker_split_across_deltas_is_removed()
    {
        var (text, removed, _) = await StreamAsync(
            Delta("assign a schedule 【sourcePath: procedures/"), Delta("missing-fee-schedule.txt】"), Delta(" and re-run"));
        Assert.Equal("assign a schedule and re-run", text);
        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task Several_markers_in_one_delta_are_all_removed()
    {
        var (text, removed, _) = await StreamAsync(Delta("Step one 【a】. Step two 【b】【c】.\n\nDone."));
        Assert.Equal("Step one. Step two.\n\nDone.", text);
        Assert.Equal(3, removed);
    }

    [Fact]
    public async Task A_stray_opening_bracket_and_what_follows_it_are_kept()
    {
        var tail = new string('x', CitationMarkerChatClient.MaxMarkerLength + 20);
        var (text, removed, _) = await StreamAsync(Delta("before 【"), Delta(tail), Delta(" after"));
        Assert.Equal("before 【" + tail + " after", text);
        Assert.Equal(0, removed);

        var (unclosed, _, _) = await StreamAsync(Delta("ends with 【unclosed"));
        Assert.Equal("ends with 【unclosed", unclosed);
    }

    [Fact]
    public async Task Text_without_markers_is_unchanged_word_by_word()
    {
        var answer = "Assign the missing fee schedule and re-run,\nper Procedure: Missing fee schedule.  ";
        var (text, removed, _) = await StreamAsync(ScriptedChatClient.Text(answer));
        Assert.Equal(answer, text);
        Assert.Equal(0, removed);
    }

    [Fact]
    public async Task Reasoning_and_calls_pass_through_untouched()
    {
        var call = ScriptedChatClient.Call("search_documents", new() { ["query"] = "q" })[0];
        var (_, _, updates) = await StreamAsync(
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("I see 【x】 in the data")]), call);
        Assert.Equal("I see 【x】 in the data", updates[0].Contents.OfType<TextReasoningContent>().Single().Text);
        Assert.Single(updates[1].Contents.OfType<FunctionCallContent>());
    }

    [Fact]
    public async Task The_non_streaming_path_strips_too()
    {
        var removed = 0;
        using var client = new CitationMarkerChatClient(new ScriptedChatClient((_, _, _) => [Delta("Done 【x】.")]), n => removed += n);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "q")], cancellationToken: Ct);
        Assert.Equal("Done.", response.Text);
        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task A_chat_turn_streams_and_stores_the_answer_without_markers_and_counts_them()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel(
            "Assign the missing fee schedule 【sourcePath: procedures/missing-fee-schedule.txt】 and re-run 【tool_data†search_documents】."));
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "what is the procedure when a fee schedule is missing");

        Assert.Equal("Assign the missing fee schedule and re-run.", ApiFactory.AnswerOf(events));
        var turn = await ChatApiTests.Db(api).Turns.SingleAsync(Ct);
        Assert.Equal("Assign the missing fee schedule and re-run.", turn.Answer);

        var end = ApiFactory.TracesOf(events).Single(t => t.GetProperty("kind").GetString() == TraceKinds.TurnEnd);
        Assert.Equal(2, end.GetProperty("data").GetProperty("citationMarkersRemoved").GetInt32());
    }

    [Fact]
    public async Task An_A2A_reply_carries_no_markers()
    {
        using var api = new ApiFactory(new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("Fees are billed quarterly 【sourcePath: docs/fees.md】.")));

        // The answer a relaying protocol (the a2a plugin) frames: the markers are the chat's, not the partner's.
        var answer = await api.Services.GetRequiredService<Maf.Lab.Plugins.Abstractions.IAssistantAnswer>()
            .AnswerAsync(new Principal("a2a:firm-a", TenantId.Firm("firm-a"), Role.READ_ONLY), "how often are fees billed", Ct);

        Assert.Equal("Fees are billed quarterly.", answer);
    }
}
