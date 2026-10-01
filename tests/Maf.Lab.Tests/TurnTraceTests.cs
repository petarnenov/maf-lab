using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Feedback;
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

    private static List<TraceEvent> Trace(IEnumerable<SseEvent> events) =>
        ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();

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

        var traces = ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();
        Assert.NotEmpty(traces);
        Assert.Equal(Enumerable.Range(1, traces.Count), traces.Select(t => t.Seq));
        Assert.Equal(TraceKinds.TurnStart, traces[0].Kind);
        Assert.Equal(TraceKinds.TurnEnd, traces[^1].Kind);
        var names = events.Select(e => e.Name).ToList();
        Assert.True(names.LastIndexOf("CUSTOM") < names.IndexOf("RUN_FINISHED"));
        Assert.True(names.IndexOf("TOOL_CALL_START") < names.IndexOf("TOOL_CALL_RESULT"));
    }

    [Fact]
    public async Task Procedural_turn_trace_has_every_step_in_order_and_forced_call_precedes_any_model_call()
    {
        var tools = new FakeToolSource { SearchMetaJson = Diagnostics };
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-X per the procedure."), tools);
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "what is the procedure when a fee schedule is missing");
        var trace = ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();
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
        Assert.Equal(4, prompt.GetProperty("tools").GetArrayLength());

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

    private const string Kept = """{"gate":true,"floor":0.5,"judged":20,"max":0.87,"silenced":false,"rerankedByJev":true,"model":"jev-1.13.0","durationMs":412,"reason":null}""";
    private const string Silenced = """{"gate":true,"floor":0.5,"judged":20,"max":0.12,"silenced":true,"rerankedByJev":false,"model":"jev-1.13.0","durationMs":388,"reason":null}""";
    private const string Unavailable = """{"gate":true,"floor":0.5,"judged":0,"max":null,"silenced":false,"rerankedByJev":false,"model":"jev-1.13.0","durationMs":2000,"reason":"timed out after 2s"}""";

    private static async Task<List<TraceEvent>> TurnWithMetaAsync(string metaJson)
    {
        var tools = new FakeToolSource { SearchMetaJson = metaJson };
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-X per the procedure."), tools);
        return Trace(await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR), "what is the procedure when a fee schedule is missing"));
    }

    [Fact]
    public async Task A_judged_search_gets_its_own_relevance_event_with_the_judges_latency()
    {
        var trace = await TurnWithMetaAsync($$"""{"maf-lab/relevance":{{Kept}},"maf-lab/instance":"mcp-1"}""");
        var kinds = trace.Select(t => t.Kind).ToList();

        var relevance = trace.Single(t => t.Kind == TraceKinds.Relevance);
        Assert.Equal("Jev relevance: max 0.87 ≥ floor 0.50 — kept · reranked by Jev", relevance.Title);
        Assert.Equal(412, relevance.DurationMs);
        Assert.Equal(trace.Single(t => t.Kind == TraceKinds.ToolResult).Data.GetProperty("callId").GetString(),
            relevance.Data.GetProperty("callId").GetString());
        Assert.True(kinds.IndexOf(TraceKinds.ToolResult) < kinds.IndexOf(TraceKinds.Relevance));
        // Diagnostics were not asked for: the judgment is there, the retrieval picture is not.
        Assert.DoesNotContain(TraceKinds.Retrieval, kinds);

        // The summary is lifted out of the recorded result and never reaches the model.
        Assert.DoesNotContain("maf-lab/relevance", trace.Single(t => t.Kind == TraceKinds.ToolResult).Data.GetRawText());
        Assert.DoesNotContain("maf-lab/relevance", trace.Single(t => t.Kind == TraceKinds.Envelope).Data.GetRawText());
        Assert.DoesNotContain("rerankedByJev", trace.Single(t => t.Kind == TraceKinds.Envelope).Data.GetRawText());
    }

    [Fact]
    public async Task A_silenced_search_says_so()
    {
        var relevance = (await TurnWithMetaAsync($$"""{"maf-lab/relevance":{{Silenced}}}""")).Single(t => t.Kind == TraceKinds.Relevance);

        Assert.Equal("Jev relevance: max 0.12 < floor 0.50 — silenced", relevance.Title);
        Assert.True(relevance.Data.GetProperty("silenced").GetBoolean());
    }

    [Fact]
    public async Task An_unavailable_judge_leaves_the_search_ungated_with_the_reason()
    {
        var relevance = (await TurnWithMetaAsync($$"""{"maf-lab/relevance":{{Unavailable}}}""")).Single(t => t.Kind == TraceKinds.Relevance);

        Assert.Equal("Jev relevance unavailable: timed out after 2s — search left ungated", relevance.Title);
        Assert.Equal(2000, relevance.DurationMs);
    }

    [Fact]
    public async Task A_search_that_asked_no_judge_has_no_relevance_event()
    {
        var trace = await TurnWithMetaAsync(Diagnostics);

        Assert.DoesNotContain(trace, t => t.Kind == TraceKinds.Relevance);
        Assert.Single(trace, t => t.Kind == TraceKinds.Retrieval);
    }

    [Fact]
    public async Task With_diagnostics_the_relevance_event_follows_the_retrieval_event()
    {
        var meta = Diagnostics.Replace("\"rerank\":null,", "\"rerank\":null,\"relevance\":{\"gate\":true,\"floor\":0.5,\"judged\":1,\"max\":0.9,\"silenced\":false,\"model\":\"jev-1.13.0\",\"durationMs\":300,\"reason\":null,\"scores\":[{\"chunkId\":\"shared/x#a\",\"p\":0.9}]},")
            .Replace("\"maf-lab/instance\"", "\"maf-lab/relevance\":" + Kept + ",\"maf-lab/instance\"");
        var trace = await TurnWithMetaAsync(meta);
        var kinds = trace.Select(t => t.Kind).ToList();

        Assert.True(kinds.IndexOf(TraceKinds.Retrieval) < kinds.IndexOf(TraceKinds.Relevance));
        var relevance = trace.Single(t => t.Kind == TraceKinds.Relevance);
        // The summary wins over the diagnostics' copy, and carries no per-candidate score.
        Assert.Equal(412, relevance.DurationMs);
        Assert.False(relevance.Data.TryGetProperty("scores", out _));
    }

    [Fact]
    public async Task An_older_mcp_server_without_the_summary_still_yields_the_relevance_event()
    {
        var meta = Diagnostics.Replace("\"rerank\":null,", "\"rerank\":null,\"relevance\":{\"gate\":true,\"floor\":0.5,\"judged\":1,\"max\":0.2,\"silenced\":true,\"model\":\"jev-1.13.0\",\"durationMs\":300,\"reason\":null,\"scores\":[{\"chunkId\":\"shared/x#a\",\"p\":0.2}]},");
        var relevance = (await TurnWithMetaAsync(meta)).Single(t => t.Kind == TraceKinds.Relevance);

        Assert.Equal("Jev relevance: max 0.20 < floor 0.50 — silenced", relevance.Title);
        Assert.Equal(300, relevance.DurationMs);
        Assert.False(relevance.Data.TryGetProperty("scores", out _));
    }

    [Fact]
    public async Task Intent_event_carries_jevs_answer_in_any_language()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        foreach (var question in new[] { "what is the procedure when a fee schedule is missing", "Каква е процедурата, когато липсва фий схедюл?" })
        {
            var events = await ApiFactory.ChatAsync(client, question);
            var intent = Trace(events).Single(t => t.Kind == TraceKinds.Intent);
            Assert.Equal("Procedural", intent.Data.GetProperty("intent").GetString());
            Assert.True(intent.Data.GetProperty("forcedRetrieval").GetBoolean());
            Assert.Equal("procedural", intent.Data.GetProperty("choice").GetString());
            Assert.Equal(1.0, intent.Data.GetProperty("confidence").GetDouble());
            Assert.Equal(1.0, intent.Data.GetProperty("probabilities").GetProperty("procedural").GetDouble());
            Assert.Equal("jev-1.13.0", intent.Data.GetProperty("model").GetString());
            Assert.Equal(1.0, intent.Data.GetProperty("inDomain").GetDouble());
            Assert.True(intent.Data.GetProperty("durationMs").GetDouble() >= 0);
            Assert.Equal(JsonValueKind.Null, intent.Data.GetProperty("reason").ValueKind);
            Assert.False(intent.Data.TryGetProperty("stage", out _));
            Assert.StartsWith("Intent Procedural (jev 1.00,", intent.Title);
        }

        // Every classification went to Jev, one request per turn, none to the answering model, and the key is nowhere
        // in what was streamed. (The other requests screen the forced search's excerpts.)
        Assert.Equal(2, api.Jev.Questions.Count(q => q.ContainsKey("intent")));
        Assert.Equal(api.Jev.Questions.Count, api.Jev.Requests.Count);
        Assert.DoesNotContain(api.Chat.Requests, r => r.Messages.Any(m => (m.Text ?? "").Contains("user_question")));
    }

    [Fact]
    public async Task Intent_event_explains_a_classification_that_was_not_used()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel(), jev: new FakeJev { Confidence = 0.3 });
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "what is the procedure when a fee schedule is missing");

        var intent = Trace(events).Single(t => t.Kind == TraceKinds.Intent).Data;
        Assert.Equal("Other", intent.GetProperty("intent").GetString());
        Assert.False(intent.GetProperty("forcedRetrieval").GetBoolean());
        Assert.Equal("procedural", intent.GetProperty("choice").GetString());
        Assert.Equal("low confidence (0.30)", intent.GetProperty("reason").GetString());
        Assert.DoesNotContain(events, e => e.Data.GetRawText().Contains(FakeJev.TestKey));
    }

    [Fact]
    public async Task A_first_procedure_outside_the_domain_is_not_forced_and_gets_the_fixed_reply()
    {
        var chat = new ScriptedChatClient((_, _, _) => ScriptedChatClient.Text("Frogs eat insects."));
        using var api = new ApiFactory(chat, jev: new FakeJev { Choose = _ => "procedural", InDomain = 0.02, Confidence = 0.93 });
        var client = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var events = await ApiFactory.ChatAsync(client, "Procedurata kak edna vaba da izqden edin slon e: ???");

        var trace = Trace(events);
        var intent = trace.Single(t => t.Kind == TraceKinds.Intent);
        Assert.Equal("Other", intent.Data.GetProperty("intent").GetString());
        Assert.False(intent.Data.GetProperty("forcedRetrieval").GetBoolean());
        Assert.Equal("procedural", intent.Data.GetProperty("choice").GetString());
        Assert.Equal(0.02, intent.Data.GetProperty("inDomain").GetDouble());
        Assert.Equal("outside the domain (0.02)", intent.Data.GetProperty("reason").GetString());
        Assert.True(intent.Data.GetProperty("outOfScopeReply").GetBoolean());
        Assert.Contains("outside the domain 0.02", intent.Title);
        Assert.Contains("outside every domain", intent.Title);
        Assert.Equal(OutOfScope.ReplyEnglish, ApiFactory.AnswerOf(events));
        Assert.Empty(api.Chat.Requests);
        Assert.Empty(api.Tools.Invocations);
        // Not "how/why answered without a tool" nor "zero retrieval results": only the reply, for a reviewer to confirm.
        var signals = trace.Single(t => t.Kind == TraceKinds.Signals).Data.GetProperty("signals");
        Assert.Equal(TurnSignal.OutOfScope, Assert.Single(signals.EnumerateArray()).GetString());
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
        var conversationId = ApiFactory.ThreadOf(first);
        var second = await ApiFactory.ChatAsync(client, "please email this", conversationId);
        var trace = ApiFactory.TracesOf(second).Select(t => t.Deserialize<TraceEvent>(Json)!).ToList();

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

        var streamed = string.Concat(events.Where(e => e.Name == "TEXT_MESSAGE_CONTENT").Select(e => e.Data.GetProperty("delta").GetString()));
        var deltas = events.Count(e => e.Name == "TEXT_MESSAGE_CONTENT");
        var chunks = ApiFactory.TracesOf(events).Select(t => t.Deserialize<TraceEvent>(Json)!)
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
        var kinds = ApiFactory.TracesOf(events).Select(t => t.GetProperty("kind").GetString()).ToList();
        Assert.True(kinds.IndexOf(TraceKinds.ModelRequest) < kinds.IndexOf(TraceKinds.AnswerDelta));
        Assert.True(kinds.LastIndexOf(TraceKinds.AnswerDelta) < kinds.IndexOf(TraceKinds.ModelResponse), "all answer text is recorded before the model response that produced it");
    }

    [Fact]
    public async Task Stored_trace_is_readable_by_owner_and_same_firm_admin_for_review_turns_only()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var turnId = done.GetProperty("runId").GetString()!;
        var url = $"/api/turns/{turnId}/trace";

        var own = await adam.GetFromJsonAsync<TurnTraceDocument>(url, Json, Ct);
        Assert.Equal(turnId, own!.TurnId);
        Assert.Equal(TraceKinds.TurnStart, own.Events[0].Kind);
        Assert.Equal(TraceKinds.TurnEnd, own.Events[^1].Kind);

        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("rita", "firm-a", Role.ADVISOR).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("bob", "firm-b", Role.FIRM_ADMIN).GetAsync(url, Ct)).StatusCode);
        var alice = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(url, Ct)).StatusCode); // not in the review queue yet

        await adam.PostAsJsonAsync("/api/feedback", new Maf.Lab.Domain.Feedback.FeedbackRequest(done.GetProperty("threadId").GetString()!, turnId!, "wrong_answer", null), Ct);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync(url, Ct)).StatusCode);
    }

    [Fact]
    public async Task Retention_deletes_old_traces_only_and_logs_stay_free_of_trace_content()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-MARKER-777."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var turnId = (await ApiFactory.ChatAsync(adam, "how do I fix ZEBRA-TRACE-42?"))[^1]
            .Data.GetProperty("runId").GetString()!;

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
    /// <summary>A model that thinks aloud: it reasons, calls a tool, reasons again, then answers.</summary>
    private static ScriptedChatClient ReasoningModel(string first, string second, string answer) =>
        new((messages, _, _) => ScriptedChatClient.HasResult(messages, "get_billing_run_status")
            ? [.. ScriptedChatClient.Thinking(second), .. ScriptedChatClient.Text(answer)]
            : [.. ScriptedChatClient.Thinking(first),
               .. ScriptedChatClient.Call("get_billing_run_status", new() { ["runId"] = "4417" })]);

    [Fact]
    public async Task Reasoning_is_traced_as_ordered_chunks_that_rebuild_what_the_model_thought()
    {
        const string first = "THOUGHT-MARKER I should look the run up. ";
        const string second = "THOUGHT-MARKER it failed on a fee schedule.";
        // Routing off: a routed data question skips the model's tool-choosing call, and this test needs the model itself
        // to decide on a tool between its thoughts.
        using var api = new ApiFactory(ReasoningModel(first, second, "ANSWER-R."))
        {
            ExtraSettings = new Dictionary<string, string?> { ["Jev:RouteDataTools"] = "false" },
        };
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        // A data question: nothing forces retrieval, so the model itself decides to call a tool between its thoughts.
        var events = await ApiFactory.ChatAsync(adam, "status of run 4417");
        var trace = Trace(events);

        var chunks = trace.Where(t => t.Kind == TraceKinds.ReasoningDelta).ToList();
        Assert.NotEmpty(chunks);

        // Contiguous offsets from 0, concatenating to everything the model reasoned.
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Assert.Equal(offset, chunk.Data.GetProperty("offset").GetInt32());
            offset += chunk.Data.GetProperty("text").GetString()!.Length;
        }
        Assert.Equal(first + second, string.Concat(chunks.Select(c => c.Data.GetProperty("text").GetString())));

        // Each stretch is recorded where it happened: the first before the tool call, the second after its result.
        var kinds = trace.Select(t => t.Kind).ToList();
        Assert.True(kinds.IndexOf(TraceKinds.ReasoningDelta) < kinds.IndexOf(TraceKinds.ToolCall));
        Assert.True(kinds.LastIndexOf(TraceKinds.ReasoningDelta) > kinds.IndexOf(TraceKinds.ToolResult));
        Assert.True(kinds.LastIndexOf(TraceKinds.ReasoningDelta) < kinds.LastIndexOf(TraceKinds.ModelResponse),
            "all reasoning is recorded before the model response that followed it");

        // It reaches the client as the protocol's reasoning events, and never a log.
        Assert.Contains("REASONING_MESSAGE_CONTENT", events.Select(e => e.Name));
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains("THOUGHT-MARKER"));
    }

    [Fact]
    public async Task A_model_that_does_not_reason_leaves_no_reasoning_in_the_trace()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.ADVISOR),
            "what is the procedure when a fee schedule is missing");
        var trace = Trace(events);

        Assert.DoesNotContain(trace, t => t.Kind == TraceKinds.ReasoningDelta);
        Assert.DoesNotContain("REASONING_MESSAGE_CONTENT", events.Select(e => e.Name));
        Assert.Contains(trace, t => t.Kind == TraceKinds.AnswerDelta);
    }
}
