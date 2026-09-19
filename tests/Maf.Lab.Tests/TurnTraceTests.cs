using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

public class TurnTraceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Diagnostics = """
        {"maf-lab/trace":{"instance":"mcp-1","tenantScope":["firm-a","shared"],"settings":{"mode":"hybrid","fusion":"rrf"},
         "query":{"text":"q","terms":[{"term":"fee","idf":1.2}]},"dense":[],"sparse":[],"fused":[{"rank":1,"chunkId":"shared/x#a","docId":"shared/x","tenantId":"shared","score":0.5}],
         "rerank":null,"timings":{"qdrantMs":3}},
         "maf-lab/instance":"mcp-1"}
        """;

    [Fact]
    public void Collector_orders_events_and_caps_fields_and_total_size()
    {
        var trace = new TurnTrace(null);
        var a = trace.Add("k1", "first", new { text = "short" });
        var b = trace.Add("k2", "long", new { text = new string('x', TurnTrace.MaxFieldChars + 50) });
        Assert.Equal([1, 2], trace.Events.Select(e => e.Seq));
        Assert.False(a.Truncated);
        Assert.True(b.Truncated);
        Assert.EndsWith("…[truncated]", b.Data.GetProperty("text").GetString());
        Assert.True(b.AtMs >= a.AtMs);

        for (var i = 0; i < 80; i++)
        {
            trace.Add("bulk", "bulk", new { text = new string('y', 19_000) });
        }
        var last = trace.Events[^1];
        Assert.True(last.Truncated);
        Assert.True(last.Data.GetProperty("truncated").GetBoolean());
        Assert.True(trace.Events.Sum(e => e.Data.GetRawText().Length) <= TurnTrace.MaxTraceBytes);
    }

    [Fact]
    public async Task Stream_carries_ordered_trace_events_from_turn_start_to_turn_end_before_done()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "what is the procedure when a fee schedule is missing");

        var traces = events.Where(e => e.Name == "trace").Select(e => e.Data.Deserialize<TraceEvent>(Json)!).ToList();
        Assert.NotEmpty(traces);
        Assert.Equal(Enumerable.Range(1, traces.Count), traces.Select(t => t.Seq));
        Assert.Equal(TraceKinds.TurnStart, traces[0].Kind);
        Assert.Equal(TraceKinds.TurnEnd, traces[^1].Kind);
        var names = events.Select(e => e.Name).ToList();
        Assert.True(names.LastIndexOf("trace") < names.IndexOf("done"));
        Assert.True(names.IndexOf("tool_call_started") < names.IndexOf("tool_call_finished"));
    }

    [Fact]
    public async Task Procedural_turn_trace_has_every_step_in_order_and_forced_call_precedes_any_model_call()
    {
        var tools = new FakeToolSource { SearchMetaJson = Diagnostics };
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-X per the procedure."), tools);
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "what is the procedure when a fee schedule is missing");
        var trace = events.Where(e => e.Name == "trace").Select(e => e.Data.Deserialize<TraceEvent>(Json)!).ToList();
        var kinds = trace.Select(t => t.Kind).ToList();

        string[] expected =
        [
            TraceKinds.TurnStart, TraceKinds.Intent, TraceKinds.Prompt, TraceKinds.History, TraceKinds.ToolForced, TraceKinds.ToolCall,
            TraceKinds.ToolResult, TraceKinds.Retrieval, TraceKinds.Audit, TraceKinds.Envelope, TraceKinds.ModelRequest, TraceKinds.ModelResponse,
            TraceKinds.Memory, TraceKinds.Sources, TraceKinds.Signals, TraceKinds.TurnEnd,
        ];
        var positions = expected.Select(k => kinds.IndexOf(k)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0, $"missing kind: {string.Join(",", expected.Where(k => !kinds.Contains(k)))}"));
        Assert.Equal(positions.Order(), positions);
        Assert.True(kinds.IndexOf(TraceKinds.ToolForced) < kinds.IndexOf(TraceKinds.ModelRequest));

        var intent = trace.Single(t => t.Kind == TraceKinds.Intent).Data;
        Assert.True(intent.GetProperty("forcedRetrieval").GetBoolean());
        var prompt = trace.Single(t => t.Kind == TraceKinds.Prompt).Data;
        Assert.Contains("<tool_data>", prompt.GetProperty("systemPrompt").GetString());
        Assert.Equal(3, prompt.GetProperty("tools").GetArrayLength());

        var request = trace.First(t => t.Kind == TraceKinds.ModelRequest).Data;
        Assert.Contains("functionResult", request.GetProperty("messages").GetRawText());
        var response = trace.First(t => t.Kind == TraceKinds.ModelResponse).Data;
        Assert.Contains("ANSWER-X", response.GetProperty("text").GetString());

        // Diagnostics reach the monitor but never the model.
        var retrieval = trace.Single(t => t.Kind == TraceKinds.Retrieval).Data;
        Assert.Equal("mcp-1", retrieval.GetProperty("instance").GetString());
        var result = trace.Single(t => t.Kind == TraceKinds.ToolResult).Data;
        Assert.Equal("mcp-1", result.GetProperty("mcpInstance").GetString());
        Assert.DoesNotContain("tenantScope", result.GetProperty("result").GetRawText());
        var envelope = trace.Single(t => t.Kind == TraceKinds.Envelope).Data.GetProperty("text").GetString()!;
        Assert.StartsWith("<tool_data tool=\"search_documents\">", envelope);
        Assert.DoesNotContain("tenantScope", envelope);
        Assert.DoesNotContain("maf-lab/trace", envelope);
        var modelSaw = string.Join("\n", api.Chat.Requests.SelectMany(r => r.Messages).SelectMany(m => m.Contents).OfType<FunctionResultContent>().Select(r => r.Result?.ToString()));
        Assert.DoesNotContain("tenantScope", modelSaw);
    }

    [Fact]
    public async Task Second_turn_history_event_contains_the_first_turn_and_unknown_tools_are_traced()
    {
        var chat = new ScriptedChatClient((messages, _, n) =>
        {
            var last = messages.Last(m => m.Role == ChatRole.User).Text;
            return last.Contains("email") && !messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any()
                ? ScriptedChatClient.Call("send_email", new() { ["to"] = "x" })
                : ScriptedChatClient.Text("ok");
        });
        using var api = new ApiFactory(chat);
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var first = await ApiFactory.ChatAsync(client, "hello there");
        var conversationId = first[^1].Data.GetProperty("conversationId").GetString();
        var second = await ApiFactory.ChatAsync(client, "please email this", conversationId);
        var trace = second.Where(e => e.Name == "trace").Select(e => e.Data.Deserialize<TraceEvent>(Json)!).ToList();

        var history = trace.Single(t => t.Kind == TraceKinds.History).Data;
        Assert.Contains("hello there", history.GetProperty("included").GetRawText());
        Assert.True(history.GetProperty("usedTokens").GetInt32() > 0);
        Assert.Equal("send_email", trace.Single(t => t.Kind == TraceKinds.ToolUnknown).Data.GetProperty("tool").GetString());
        Assert.Contains(trace, t => t.Kind == TraceKinds.Audit && t.Data.GetProperty("outcome").GetString() == "unknown_tool");
    }

    [Fact]
    public async Task Answer_is_recorded_as_coalesced_contiguous_chunks_that_rebuild_the_answer()
    {
        var longAnswer = string.Join(" ", Enumerable.Range(1, 220).Select(i => $"word{i}"));
        using var api = new ApiFactory(ApiFactory.ProceduralModel(longAnswer));
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "what is the procedure when a fee schedule is missing");

        var streamed = string.Concat(events.Where(e => e.Name == "text_delta").Select(e => e.Data.GetProperty("text").GetString()));
        var deltas = events.Count(e => e.Name == "text_delta");
        var chunks = events.Where(e => e.Name == "trace").Select(e => e.Data.Deserialize<TraceEvent>(Json)!)
            .Where(t => t.Kind == TraceKinds.AnswerDelta).ToList();

        Assert.NotEmpty(chunks);
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Assert.Equal(offset, chunk.Data.GetProperty("offset").GetInt32());
            offset += chunk.Data.GetProperty("text").GetString()!.Length;
        }
        Assert.Equal(streamed, string.Concat(chunks.Select(c => c.Data.GetProperty("text").GetString())));
        Assert.True(chunks.Count * 5 < deltas, $"{chunks.Count} chunks for {deltas} deltas");
        Assert.All(chunks.SkipLast(1), c => Assert.True(c.Data.GetProperty("text").GetString()!.Length >= 1));

        // Chunks sit between the model request and its response; the trace still ends with turn.end.
        var kinds = events.Where(e => e.Name == "trace").Select(e => e.Data.GetProperty("kind").GetString()).ToList();
        Assert.True(kinds.IndexOf(TraceKinds.ModelRequest) < kinds.IndexOf(TraceKinds.AnswerDelta));
        Assert.True(kinds.LastIndexOf(TraceKinds.AnswerDelta) < kinds.IndexOf(TraceKinds.ModelResponse), "all answer text is recorded before the model response that produced it");
    }

    [Fact]
    public async Task Stored_trace_is_readable_by_owner_and_same_firm_admin_for_review_turns_only()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var turnId = done.GetProperty("turnId").GetString();
        var url = $"/api/turns/{turnId}/trace";

        var own = await adam.GetFromJsonAsync<TurnTraceDocument>(url, Json, Ct);
        Assert.Equal(turnId, own!.TurnId);
        Assert.Equal(TraceKinds.TurnStart, own.Events[0].Kind);
        Assert.Equal(TraceKinds.TurnEnd, own.Events[^1].Kind);

        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("rita", "firm-a", Role.ADVISOR).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("bob", "firm-b", Role.FIRM_ADMIN).GetAsync(url, Ct)).StatusCode);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(url, Ct)).StatusCode); // not in the review queue yet

        await adam.PostAsJsonAsync("/api/feedback", new Maf.Lab.Domain.Feedback.FeedbackRequest(done.GetProperty("conversationId").GetString()!, turnId!, "wrong_answer", null), Ct);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync(url, Ct)).StatusCode);
    }

    [Fact]
    public async Task Retention_deletes_old_traces_only_and_logs_stay_free_of_trace_content()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-MARKER-777."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var turnId = (await ApiFactory.ChatAsync(adam, "how do I fix ZEBRA-TRACE-42?"))[^1].Data.GetProperty("turnId").GetString()!;

        await using (var ctx = ChatApiTests.Db(api))
        {
            var stored = await ctx.TurnTraces.SingleAsync(t => t.TurnId == turnId, Ct);
            Assert.Contains("ZEBRA-TRACE-42", stored.Json);
            Assert.Contains("ANSWER-MARKER-777", stored.Json);
            ctx.TurnTraces.Add(new TurnTraceRow { TurnId = "t_old", ConversationId = "c", UserId = "adam", FirmId = "firm-a", CreatedAt = DateTime.UtcNow.AddDays(-8), Json = "[]" });
            await ctx.SaveChangesAsync(Ct);
        }
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains("ZEBRA-TRACE-42") || m.Contains("ANSWER-MARKER-777"));

        var removed = await api.Services.GetRequiredService<TraceRetentionService>().PurgeAsync(Ct);
        Assert.Equal(1, removed);
        await using var check = ChatApiTests.Db(api);
        Assert.False(await check.TurnTraces.AnyAsync(t => t.TurnId == "t_old", Ct));
        Assert.True(await check.TurnTraces.AnyAsync(t => t.TurnId == turnId, Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await adam.GetAsync("/api/turns/t_old/trace", Ct)).StatusCode);
    }
}
