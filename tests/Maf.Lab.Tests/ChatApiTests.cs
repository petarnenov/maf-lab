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
    public async Task A_run_streams_its_tool_call_before_the_tool_runs_and_its_sources_before_it_ends()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new FakeToolSource { BeforeSearchExecutes = () => started.Task.WaitAsync(TimeSpan.FromSeconds(10)) };
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing",
            onEvent: e => { if (e.Name == "TOOL_CALL_START") started.TrySetResult(); });

        var names = events.Select(e => e.Name).ToList();
        Assert.Equal("RUN_STARTED", names[0]);
        Assert.Equal("RUN_FINISHED", names[^1]);
        Assert.True(names.IndexOf("TOOL_CALL_END") > names.IndexOf("TOOL_CALL_START"));
        Assert.True(names.IndexOf("TOOL_CALL_RESULT") > names.IndexOf("TOOL_CALL_END"));
        Assert.Contains("TEXT_MESSAGE_CONTENT", names);

        var call = events.Single(e => e.Name == "TOOL_CALL_START").Data;
        Assert.Equal("search_documents", call.GetProperty("toolCallName").GetString());

        // The query the user typed is free text and does not travel; the arguments are identifiers only.
        var args = events.Single(e => e.Name == "TOOL_CALL_ARGS").Data.GetProperty("delta").GetString();
        Assert.DoesNotContain("fee schedule is missing", args);

        var sources = ApiFactory.SourcesOf(events);
        Assert.NotNull(sources);
        Assert.Equal("shared/procedures/missing-fee-schedule.txt", sources.Value[0].GetProperty("docId").GetString());

        var finished = events[^1].Data;
        Assert.StartsWith("c_", finished.GetProperty("threadId").GetString());
        Assert.StartsWith("t_", finished.GetProperty("result").GetProperty("turnId").GetString());
        Assert.Equal("success", finished.GetProperty("outcome").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_procedural_question_in_another_language_forces_search_like_its_english_twin()
    {
        var tools = new FakeToolSource();
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "Каква е процедурата, когато липсва фий схедюл?");

        Assert.Equal(["search_documents"], tools.Invocations);
        var sources = ApiFactory.SourcesOf(events);
        Assert.NotNull(sources);
        Assert.Equal("shared/procedures/missing-fee-schedule.txt", sources.Value[0].GetProperty("docId").GetString());
        var intent = Intent(events);
        Assert.Equal("Procedural", intent.GetProperty("intent").GetString());
        Assert.Equal("model", intent.GetProperty("stage").GetString());
        Assert.True(intent.GetProperty("forcedRetrieval").GetBoolean());
    }

    [Fact]
    public async Task A_greeting_in_another_language_is_not_forced_to_search()
    {
        var tools = new FakeToolSource();
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), tools);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "Здравей");

        Assert.Empty(tools.Invocations);
        var intent = Intent(events);
        Assert.Equal("ChitChat", intent.GetProperty("intent").GetString());
        Assert.Equal("model", intent.GetProperty("stage").GetString());
        Assert.False(intent.GetProperty("forcedRetrieval").GetBoolean());
    }

    /// <summary>The intent event's payload, which the run carries as a custom trace event.</summary>
    private static JsonElement Intent(IEnumerable<SseEvent> events) =>
        ApiFactory.TracesOf(events)
            .Select(t => t.GetProperty("data"))
            .Single(d => d.TryGetProperty("forcedRetrieval", out _));

    [Fact]
    public async Task Procedural_question_forces_search_for_that_turn_only_and_tool_output_is_wrapped_as_data()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var first = await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing");
        var conversationId = ApiFactory.ThreadOf(first);
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

        // The model asked for a tool that does not exist: the call is reported, and its result says so.
        var call = events.Single(e => e.Name == "TOOL_CALL_START").Data;
        Assert.Equal("send_email", call.GetProperty("toolCallName").GetString());
        var result = events.Single(e => e.Name == "TOOL_CALL_RESULT").Data;
        Assert.Contains("does not exist", result.GetProperty("content").GetString());
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
            conversationId = ApiFactory.ThreadOf(first);
        }

        using var restarted = new ApiFactory(ApiFactory.ProceduralModel(), dataDir: dir);
        await ApiFactory.ChatAsync(restarted.ClientFor("adam", "firm-a", Role.ADVISOR), "and what about proration?", conversationId);
        var history = restarted.Chat.Requests[0].Messages.Select(m => m.Text).ToList();
        Assert.Contains("explain FS-REQUIRED", history);
        Assert.Contains("Remember: FS-REQUIRED means a missing schedule.", history);

        foreach (var (user, firm) in new[] { ("bob", "firm-b"), ("rita", "firm-a") })
        {
            var response = await restarted.ClientFor(user, firm, Role.ADVISOR).PostAsJsonAsync("/api/chat", new
            {
                threadId = conversationId,
                runId = "r_probe",
                messages = new[] { new { id = "u_probe", role = "user", content = "hi" } },
            }, Ct);
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
        // The audit line now covers every kind of action, not only tools, so it is prefixed "audit kind=".
        Assert.Contains(api.Logs.Messages, m => m.Contains("audit kind=tool"));
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
