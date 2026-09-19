using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Retrieval.Hosting;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Feedback;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Models;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent;

public sealed record TurnResult(string ConversationId, string TurnId, Intent Intent, bool ForcedRetrieval, string Answer,
    IReadOnlyList<ToolCallRecord> ToolCalls, IReadOnlyList<SourceRef> Sources, IReadOnlyList<string> Signals, string? Error);

/// <summary>
/// Runs one chat turn through the Microsoft Agent Framework agent and publishes SSE events.
/// Retrieval happens only when the model calls search_documents over MCP; this class never queries the store.
/// </summary>
public sealed class ChatTurnRunner(
    IChatClientFactory models,
    IToolSource toolSource,
    SystemPrompt prompt,
    ToolAudit audit,
    TokenCounter tokens,
    IDbContextFactory<MafDbContext> db,
    IOptions<AgentOptions> options,
    TimeProvider time,
    ILoggerFactory loggers)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger = loggers.CreateLogger<ChatTurnRunner>();

    public async Task<TurnResult> RunAsync(Principal principal, string bearerToken, string conversationId, string message,
        ChannelWriter<ChatEvent> events, CancellationToken ct)
    {
        var turnId = $"t_{Guid.NewGuid():N}";
        var trace = new TurnTrace(events);
        var state = new TurnState(principal, conversationId, turnId, events, trace);
        var chunker = state.Answer;
        var intent = IntentClassifier.Classify(message);
        trace.Add(TraceKinds.TurnStart, $"Turn started on {InstanceIdentity.Name}", new JsonObject
        {
            ["conversationId"] = conversationId,
            ["turnId"] = turnId,
            ["principal"] = new JsonObject { ["userId"] = principal.UserId, ["firmId"] = principal.FirmId.Value, ["role"] = principal.Role.ToString() },
            ["apiInstance"] = InstanceIdentity.Name,
            ["question"] = message,
        });
        var forced = false;
        string? error = null;
        var answer = new StringBuilder();
        var sw = Stopwatch.StartNew();

        try
        {
            await MarkRephraseAsync(conversationId, message, ct);
            await using var tools = await toolSource.GetToolsAsync(bearerToken, ct);
            state.KnownTools = tools.Names;

            var chatOptions = models.BaseChatOptions();
            chatOptions.Instructions = prompt.Text;
            chatOptions.Tools = [.. tools.Tools];
            forced = IntentClassifier.ForcesRetrieval(intent) && tools.Names.Contains("search_documents");
            chatOptions.ToolMode = forced ? ChatToolMode.RequireSpecific("search_documents") : ChatToolMode.Auto;
            trace.Add(TraceKinds.Intent, $"Intent {intent}{(forced ? " → forcing search_documents" : "")}", new JsonObject
            {
                ["intent"] = intent.ToString(),
                ["forcedRetrieval"] = forced,
                ["forcedTool"] = forced ? "search_documents" : null,
            });
            trace.Add(TraceKinds.Prompt, $"System prompt {prompt.Version} + {tools.Tools.Count} tool(s)", new JsonObject
            {
                ["version"] = prompt.Version,
                ["systemPrompt"] = prompt.Text,
                ["toolMode"] = TraceMapping.ToolMode(chatOptions.ToolMode),
                ["tools"] = new JsonArray(tools.Tools.OfType<AIFunctionDeclaration>().Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = TraceMapping.Node(t.JsonSchema),
                }).ToArray()),
            });

            IChatClient chatClient = new TracingChatClient(models.CreateChatClient(), trace, chunker.Flush);
            if (options.Value.EmulateRequiredToolMode)
            {
                chatClient = new RequiredToolModeChatClient(chatClient, call => trace.Add(TraceKinds.ToolForced,
                    $"Forced {call.Name} issued on the model's behalf", new JsonObject
                    {
                        ["callId"] = call.CallId,
                        ["tool"] = call.Name,
                        ["arguments"] = TraceMapping.Node(call.Arguments),
                        ["reason"] = "Procedural intent requires retrieval; the provider ignores tool_choice, so the call is issued without asking the model.",
                    }));
            }

            var agent = new ChatClientAgent(
                    chatClient,
                    new ChatClientAgentOptions
                    {
                        Name = "maf-lab-assistant",
                        ChatOptions = chatOptions,
                        ChatHistoryProvider = new SqliteChatHistoryProvider(db, tokens, conversationId, options.Value.HistoryTokenBudget, time, trace),
                    },
                    loggers)
                .AsBuilder()
                .Use((agent, context, next, token) => InvokeToolAsync(state, context, next, token))
                .Build();

            var session = await agent.CreateSessionAsync(ct);
            await foreach (var update in agent.RunStreamingAsync(message, session, cancellationToken: ct))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent { Text.Length: > 0 } delta:
                            answer.Append(delta.Text);
                            chunker.Append(delta.Text);
                            await events.WriteAsync(new TextDeltaEvent(delta.Text), ct);
                            break;
                        case FunctionCallContent call when !tools.Names.Contains(call.Name):
                            await RecordUnknownToolAsync(state, call, ct);
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("chat turn failed: {ErrorType} turn={TurnId}", ex.GetType().Name, turnId);
            error = "The assistant could not complete this answer. Please try again.";
        }
        finally
        {
            chunker.Flush();
        }

        var sources = state.Sources.DistinctBy(s => (s.DocId, s.SectionPath)).ToList();
        if (sources.Count > 0)
        {
            await events.WriteAsync(new SourcesEvent(sources), ct);
        }

        var text = answer.ToString().Trim();
        var signals = TurnSignals.Compute(intent, state.ToolCalls.Count, state.ZeroResults, text.Length, sources.Count, options.Value.LongAnswerChars);
        trace.Add(TraceKinds.Sources, $"{sources.Count} source(s)", new JsonObject
        {
            ["sources"] = new JsonArray(sources.Select(x => (JsonNode)new JsonObject { ["docId"] = x.DocId, ["sectionPath"] = x.SectionPath }).ToArray()),
        });
        trace.Add(TraceKinds.Signals, signals.Count == 0 ? "No review signals" : $"Signals: {string.Join(", ", signals)}",
            new JsonObject { ["signals"] = new JsonArray(signals.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) });
        trace.Add(TraceKinds.TurnEnd, $"Turn finished in {sw.ElapsedMilliseconds} ms{(error is null ? "" : " with an error")}", new JsonObject
        {
            ["durationMs"] = sw.ElapsedMilliseconds,
            ["error"] = error,
            ["answerChars"] = text.Length,
            ["toolCalls"] = state.ToolCalls.Count,
            ["sourceCount"] = sources.Count,
        }, sw.ElapsedMilliseconds);
        await PersistAsync(principal, conversationId, turnId, message, text, intent, forced, state.ToolCalls, sources, signals, trace, ct);

        _logger.LogInformation("chat turn done turn={TurnId} intent={Intent} forced={Forced} tools={ToolCount} sources={SourceCount} signals={Signals} ms={Elapsed}",
            turnId, intent, forced, state.ToolCalls.Count, sources.Count, string.Join(",", signals), sw.ElapsedMilliseconds);

        await events.WriteAsync(new DoneEvent(conversationId, turnId, error), ct);
        return new TurnResult(conversationId, turnId, intent, forced, text, state.ToolCalls, sources, signals, error);
    }

    /// <summary>Agent function middleware: events before/after execution, audit, data-block wrapping, source capture.</summary>
    private async ValueTask<object?> InvokeToolAsync(TurnState state, FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken ct)
    {
        var name = context.Function.Name;
        var callId = context.CallContent?.CallId ?? Guid.NewGuid().ToString("N");
        var args = ArgumentSummary.From(context.Arguments);
        state.Answer.Flush();
        await state.Events.WriteAsync(new ToolCallStartedEvent(callId, name, args), ct);
        state.Trace.Add(TraceKinds.ToolCall, $"Calling {name} over MCP", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["arguments"] = TraceMapping.Node(context.Arguments),
        });

        var sw = Stopwatch.StartNew();
        object? result;
        try
        {
            result = await next(context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("tool {Tool} threw {ErrorType}", name, ex.GetType().Name);
            await audit.RecordAsync(new AuditEntry(state.Principal, state.ConversationId, state.TurnId, name, args, "error", sw.ElapsedMilliseconds), ct);
            state.Trace.Add(TraceKinds.ToolResult, $"{name} threw {ex.GetType().Name}", new JsonObject
            {
                ["callId"] = callId, ["tool"] = name, ["isError"] = true, ["latencyMs"] = sw.ElapsedMilliseconds, ["result"] = null,
            }, sw.ElapsedMilliseconds);
            state.ToolCalls.Add(new ToolCallRecord(name, args, "error", 0, [], [], callId, "failed"));
            await state.Events.WriteAsync(new ToolCallFinishedEvent(callId, name, "failed", 0, true), ct);
            return ToolDataEnvelope.Wrap(name, "The tool is temporarily unavailable.");
        }

        var latency = sw.ElapsedMilliseconds;
        var (payload, structured, isError) = ToolDataEnvelope.Unpack(result);
        TraceToolResult(state.Trace, callId, name, result, isError, latency);
        var (summary, sources) = Summarise(name, structured, isError);
        state.Sources.AddRange(sources);
        if (name == "search_documents" && !isError && sources.Count == 0)
        {
            state.ZeroResults = true;
        }

        var outcome = isError ? "error" : "ok";
        await audit.RecordAsync(new AuditEntry(state.Principal, state.ConversationId, state.TurnId, name, args, outcome, latency), ct);
        state.Trace.Add(TraceKinds.Audit, $"Audit: {name} {outcome}", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["arguments"] = args, ["outcome"] = outcome, ["durationMs"] = latency,
        });
        state.ToolCalls.Add(new ToolCallRecord(name, args, outcome, sources.Count, sources.Select(s => s.DocId).Distinct().ToList(), [], callId, summary));
        await state.Events.WriteAsync(new ToolCallFinishedEvent(callId, name, summary, sources.Count, isError), ct);
        var envelope = ToolDataEnvelope.Wrap(name, payload);
        state.Trace.Add(TraceKinds.Envelope, $"Data envelope handed to the model ({envelope.Length} chars)", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["text"] = envelope,
        });
        return envelope;
    }

    /// <summary>Raw MCP result (diagnostics removed) and, when the server sent them, the retrieval diagnostics.</summary>
    private static void TraceToolResult(TurnTrace trace, string callId, string tool, object? result, bool isError, long latencyMs)
    {
        var raw = TraceMapping.Node(result) as JsonObject;
        JsonNode? diagnostics = null;
        string? instance = null;
        if (raw?["_meta"] is JsonObject meta)
        {
            diagnostics = meta[TraceMeta.Diagnostics]?.DeepClone();
            instance = meta[TraceMeta.Instance]?.GetValue<string>();
            meta.Remove(TraceMeta.Diagnostics);
            if (meta.Count == 0)
            {
                raw.Remove("_meta");
            }
        }
        trace.Add(TraceKinds.ToolResult, $"{tool} returned{(isError ? " an error" : "")} in {latencyMs} ms{(instance is null ? "" : $" from {instance}")}", new JsonObject
        {
            ["callId"] = callId, ["tool"] = tool, ["isError"] = isError, ["latencyMs"] = latencyMs, ["mcpInstance"] = instance, ["result"] = raw,
        }, latencyMs);
        if (diagnostics is JsonObject d)
        {
            d["callId"] = callId;
            var fused = (d["fused"] as JsonArray)?.Count ?? 0;
            trace.Add(TraceKinds.Retrieval, $"Retrieval: {d["settings"]?["mode"]} search, {fused} fused candidate(s)", d);
        }
    }

    private static (string Summary, List<SourceRef> Sources) Summarise(string tool, JsonElement? structured, bool isError)
    {
        var sources = new List<SourceRef>();
        if (isError || structured is not { } s)
        {
            return (isError ? "error" : "done", sources);
        }
        switch (tool)
        {
            case "search_documents" when s.TryGetProperty("results", out var results):
                foreach (var r in results.EnumerateArray())
                {
                    sources.Add(new SourceRef(Str(r, "docId"), Str(r, "sectionPath"), Str(r, "sourcePath"), Str(r, "snippet")));
                }
                return (sources.Count == 0 ? "no matching documentation" : $"{sources.Count} snippet(s)", sources);
            case "get_billing_run_status":
                return ($"run {Str(s, "runId")}: {Str(s, "status")}", sources);
            case "search_billing_runs" when s.TryGetProperty("runs", out var runs):
                return ($"{runs.GetArrayLength()} run(s)", sources);
            default:
                return ("done", sources);
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private async Task RecordUnknownToolAsync(TurnState state, FunctionCallContent call, CancellationToken ct)
    {
        var name = new string(call.Name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').Take(64).ToArray());
        state.Answer.Flush();
        await state.Events.WriteAsync(new ToolCallStartedEvent(call.CallId, name, ""), ct);
        state.Trace.Add(TraceKinds.ToolUnknown, $"Model asked for unknown tool '{name}' — refused", new JsonObject { ["callId"] = call.CallId, ["tool"] = name });
        await audit.RecordAsync(new AuditEntry(state.Principal, state.ConversationId, state.TurnId, name, "", "unknown_tool", 0), ct);
        state.Trace.Add(TraceKinds.Audit, $"Audit: {name} unknown_tool", new JsonObject
        {
            ["callId"] = call.CallId, ["tool"] = name, ["arguments"] = "", ["outcome"] = "unknown_tool", ["durationMs"] = 0,
        });
        state.ToolCalls.Add(new ToolCallRecord(name, "", "unknown_tool", 0, [], [], call.CallId, "tool does not exist"));
        await state.Events.WriteAsync(new ToolCallFinishedEvent(call.CallId, name, "tool does not exist", 0, true), ct);
    }

    private async Task MarkRephraseAsync(string conversationId, string message, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var previous = await ctx.Turns.Where(t => t.ConversationId == conversationId).OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        if (previous is null || !TurnSignals.IsRephrase(previous.Question, message, time.GetUtcNow().UtcDateTime - previous.CreatedAt))
        {
            return;
        }
        var signals = JsonSerializer.Deserialize<List<string>>(previous.SignalsJson, Json) ?? [];
        if (!signals.Contains(TurnSignal.Rephrased))
        {
            signals.Add(TurnSignal.Rephrased);
            previous.SignalsJson = JsonSerializer.Serialize(signals, Json);
            await ctx.SaveChangesAsync(ct);
        }
    }

    private async Task PersistAsync(Principal principal, string conversationId, string turnId, string question, string answer, Intent intent, bool forced,
        IReadOnlyList<ToolCallRecord> toolCalls, IReadOnlyList<SourceRef> sources, IReadOnlyList<string> signals, TurnTrace trace, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        ctx.TurnTraces.Add(new TurnTraceRow
        {
            TurnId = turnId,
            ConversationId = conversationId,
            UserId = principal.UserId,
            FirmId = principal.FirmId.Value,
            CreatedAt = time.GetUtcNow().UtcDateTime,
            Json = JsonSerializer.Serialize(trace.Events, TurnTrace.Json),
        });
        ctx.Turns.Add(new TurnRow
        {
            Id = turnId,
            ConversationId = conversationId,
            UserId = principal.UserId,
            FirmId = principal.FirmId.Value,
            Question = question,
            Answer = answer,
            Intent = intent.ToString(),
            ForcedRetrieval = forced,
            ToolCallsJson = JsonSerializer.Serialize(toolCalls, Json),
            // Full source references so the conversation can be restored; the review queue reads docId/sectionPath.
            SourcesJson = JsonSerializer.Serialize(sources, Json),
            SignalsJson = JsonSerializer.Serialize(signals, Json),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });
        var conversation = await ctx.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is not null)
        {
            conversation.LastActivityAt = time.GetUtcNow().UtcDateTime;
            // The default title is the conversation's FIRST question, so continuing an older, untitled conversation
            // never renames it to a later question.
            if (conversation.Title is null && !await ctx.Turns.AnyAsync(t => t.ConversationId == conversationId && t.Id != turnId, ct))
            {
                conversation.Title = History.ConversationTitles.FromQuestion(question);
            }
        }
        await ctx.SaveChangesAsync(ct);
    }

    private sealed class TurnState(Principal principal, string conversationId, string turnId, ChannelWriter<ChatEvent> events, TurnTrace trace)
    {
        public TurnTrace Trace { get; } = trace;
        public AnswerChunker Answer { get; } = new(trace);
        public Principal Principal { get; } = principal;
        public string ConversationId { get; } = conversationId;
        public string TurnId { get; } = turnId;
        public ChannelWriter<ChatEvent> Events { get; } = events;
        public IReadOnlySet<string> KnownTools { get; set; } = new HashSet<string>();
        public List<ToolCallRecord> ToolCalls { get; } = [];
        public List<SourceRef> Sources { get; } = [];
        public bool ZeroResults { get; set; }
    }
}
