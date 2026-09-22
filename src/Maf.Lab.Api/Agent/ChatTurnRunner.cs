using System.Diagnostics;
using System.Runtime.CompilerServices;
using AGUI.Abstractions;
using AGUI.Server;
using Maf.Lab.Api.Agent.Streaming;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Hosting;
using Maf.Lab.Domain.Billing;
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
    IReadOnlyList<ToolCallRecord> ToolCalls, IReadOnlyList<SourceRef> Sources, IReadOnlyList<string> Signals, string? Error)
{
    /// <summary>The write this turn put to a person, when it paused for one, and the sentence it asked.</summary>
    public Maf.Lab.Domain.Billing.FeeAdjustmentSummary? Proposal { get; init; }

    public string? ProposalQuestion { get; init; }
}

/// <summary>
/// Runs one chat turn through the Microsoft Agent Framework agent and publishes SSE events.
/// Retrieval happens only when the model calls search_documents over MCP; this class never queries the store.
/// </summary>
public sealed class ChatTurnRunner(
    IChatClientFactory models,
    IIntentClassifier intents,
    IToolSource toolSource,
    SystemPrompt prompt,
    ToolAudit audit,
    TokenCounter tokens,
    FeeAdjustmentFlow adjustments,
    IDbContextFactory<MafDbContext> db,
    IOptions<AgentOptions> options,
    IOptions<Telemetry.TelemetryQueryOptions> telemetry,
    TimeProvider time,
    ILoggerFactory loggers)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger = loggers.CreateLogger<ChatTurnRunner>();

    public async Task<TurnResult> RunAsync(Principal principal, string bearerToken, string conversationId, string message,
        string runId, ChannelWriter<BaseEvent> events, CancellationToken ct)
    {
        var turnId = $"t_{Guid.NewGuid():N}";
        await events.WriteAsync(new RunStartedEvent { ThreadId = conversationId, RunId = runId }, ct);
        var traceId = Activity.Current?.TraceId.ToHexString();
        var trace = new TurnTrace(events);
        var state = new TurnState(principal, conversationId, turnId, events, trace);
        var chunker = state.Answer;
        var reasoning = state.Reasoning;
        var decision = IntentDecision.FromRules(Intent.Other);
        trace.Add(TraceKinds.TurnStart, $"Turn started on {InstanceIdentity.Name}", new JsonObject
        {
            ["conversationId"] = conversationId,
            ["turnId"] = turnId,
            ["principal"] = new JsonObject { ["userId"] = principal.UserId, ["firmId"] = principal.FirmId.Value, ["role"] = principal.Role.ToString() },
            ["apiInstance"] = InstanceIdentity.Name,
            // Where this turn's spans are, so a person reading it can open the whole trace.
            ["traceId"] = traceId,
            ["traceUrl"] = telemetry.Value.TraceUrlFor(traceId),
            ["question"] = message,
        });
        var forced = false;
        string? error = null;
        var answer = new StringBuilder();
        var sw = Stopwatch.StartNew();
        LabTelemetry.Instruments.Turns.Add(1, new KeyValuePair<string, object?>("outcome", "started"));

        try
        {
            await MarkRephraseAsync(conversationId, message, ct);
            state.UserMessage = message;
            await using var tools = await toolSource.GetToolsAsync(bearerToken, state.Confirmations, ct);
            state.KnownTools = tools.Names;

            var chatOptions = models.BaseChatOptions();
            chatOptions.Instructions = prompt.Text;
            chatOptions.Tools = [.. tools.Tools];
            decision = await intents.ClassifyAsync(message, ct);
            forced = IntentClassifier.ForcesRetrieval(decision.Intent) && tools.Names.Contains("search_documents");
            chatOptions.ToolMode = forced ? ChatToolMode.RequireSpecific("search_documents") : ChatToolMode.Auto;
            var stage = decision.Stage == IntentStage.Model
                ? $" (model, {decision.DurationMs:F0} ms)"
                : " (rules)";
            trace.Add(TraceKinds.Intent, $"Intent {decision.Intent}{stage}{(forced ? " → forcing search_documents" : "")}", new JsonObject
            {
                ["intent"] = decision.Intent.ToString(),
                ["forcedRetrieval"] = forced,
                ["forcedTool"] = forced ? "search_documents" : null,
                ["stage"] = decision.Stage.ToString().ToLowerInvariant(),
                ["model"] = decision.Model,
                ["rawAnswer"] = decision.RawAnswer,
                ["durationMs"] = decision.DurationMs,
                ["reason"] = decision.Reason,
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

            // The GenAI span and its duration and token metrics belong to the provider call itself, so the
            // framework's instrumentation sits innermost — below the trace, which is this system's own record.
            // Sensitive data is never enabled: prompts and completions must not leave the process.
            IChatClient chatClient = new TracingChatClient(new OpenTelemetryChatClient(models.CreateChatClient()), trace, () =>
            {
                reasoning.Flush();
                chunker.Flush();
            });
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
                .UseOpenTelemetry()
                .Build();

            var session = await agent.CreateSessionAsync(ct);

            // The adapter maps the model's output to the protocol — the part with the fiddly rules about message
            // ids, ordering and when a text message opens and closes. What it must not carry out is stripped on
            // the way through, and the run's own beginning and end stay this method's business.
            var context = new RunAgentInput
            {
                ThreadId = conversationId,
                RunId = runId,
                ProtocolVersion = AGUIProtocol.Version,
                Messages = [],
            }.ToChatRequestContext(AGUIStream.Json, new AGUIStreamOptions());

            var redaction = new RunRedaction(
                callId => state.Arguments.TryGetValue(callId, out var a) ? a : null,
                callId => state.Summaries.TryGetValue(callId, out var r) ? r : null);

            await foreach (var e in Observed(agent, session, message, state, tools.Names, answer, chunker, reasoning, ct)
                .AsAGUIEventStreamAsync(context, ct))
            {
                if (redaction.Apply(e) is { } send)
                {
                    await events.WriteAsync(send, ct);
                }
            }

            if (redaction.Failed)
            {
                // The adapter turns what the model threw into an event instead of letting it out, so a failure
                // reaches this method only by being read off the stream. A run that was stopped is not a failure.
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The agent run failed.");
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
            reasoning.Flush();
            chunker.Flush();
        }

        var sources = state.Sources.DistinctBy(s => (s.DocId, s.SectionPath)).ToList();
        if (sources.Count > 0)
        {
            await events.WriteAsync(AGUIStream.Sources(sources), ct);
        }

        var text = answer.ToString().Trim();
        var signals = TurnSignals.Compute(decision.Intent, state.ToolCalls.Count, state.ZeroResults, text.Length, sources.Count, options.Value.LongAnswerChars);
        trace.Add(TraceKinds.Sources, $"{sources.Count} source(s)", new JsonObject
        {
            ["sources"] = new JsonArray(sources.Select(x => (JsonNode)new JsonObject { ["docId"] = x.DocId, ["sectionPath"] = x.SectionPath }).ToArray()),
        });
        trace.Add(TraceKinds.Signals, signals.Count == 0 ? "No review signals" : $"Signals: {string.Join(", ", signals)}",
            new JsonObject { ["signals"] = new JsonArray(signals.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) });
        // How the turn ended, as one number an operator can aggregate. A pause is not a failure.
        var outcome = error is not null ? "failed" : state.AwaitingConfirmation ? "awaiting_person" : "answered";
        LabTelemetry.Instruments.Turns.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        LabTelemetry.Instruments.TurnDuration.Record(sw.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("intent", decision.Intent.ToString()));
        trace.Add(TraceKinds.TurnEnd, $"Turn finished in {sw.ElapsedMilliseconds} ms{(error is null ? "" : " with an error")}", new JsonObject
        {
            ["durationMs"] = sw.ElapsedMilliseconds,
            ["error"] = error,
            ["answerChars"] = text.Length,
            ["toolCalls"] = state.ToolCalls.Count,
            ["sourceCount"] = sources.Count,
        }, sw.ElapsedMilliseconds);
        await PersistAsync(principal, conversationId, turnId, message, text, decision.Intent, forced, state.ToolCalls, sources, signals, trace, ct);

        _logger.LogInformation("chat turn done turn={TurnId} intent={Intent} forced={Forced} tools={ToolCount} sources={SourceCount} signals={Signals} ms={Elapsed}",
            turnId, decision.Intent, forced, state.ToolCalls.Count, sources.Count, string.Join(",", signals), sw.ElapsedMilliseconds);

        await events.WriteAsync(Terminal(state, conversationId, runId, error), ct);
        return new TurnResult(conversationId, turnId, decision.Intent, forced, text, state.ToolCalls, sources, signals, error)
        {
            Proposal = state.Proposal,
            ProposalQuestion = state.Interrupt?.Message,
        };
    }

    /// <summary>
    /// The agent's stream, watched on its way to the adapter: the answer is accumulated for persistence and for
    /// the trace, and a call to a tool that does not exist is recorded here because it never reaches middleware.
    /// </summary>
    private async IAsyncEnumerable<ChatResponseUpdate> Observed(
        AIAgent agent, AgentSession session, string message, TurnState state, IReadOnlySet<string> known,
        StringBuilder answer, AnswerChunker chunker, AnswerChunker reasoning,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in agent.RunStreamingAsync(message, session, cancellationToken: ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 } delta:
                        answer.Append(delta.Text);
                        chunker.Append(delta.Text);
                        break;
                    // The adapter turns this into the protocol's reasoning events; the trace keeps its own copy.
                    case TextReasoningContent { Text.Length: > 0 } thought:
                        reasoning.Append(thought.Text);
                        break;
                    case FunctionCallContent call when !known.Contains(call.Name):
                        await RecordUnknownToolAsync(state, call, ct);
                        break;
                }
            }
            // An empty delta is not an answer, and a message opened for one would be a message about nothing.
            var contents = update.Contents.Where(c => c is not TextContent { Text.Length: 0 }).ToList();
            if (contents.Count == 0)
            {
                continue;
            }
            var chat = update.AsChatResponseUpdate();
            chat.Contents = contents;
            yield return chat;
        }
    }

    /// <summary>
    /// A run ends once: as an error, as a pause waiting for a person, or as a success. A pause is the protocol's
    /// own shape for it, so a client that speaks AG-UI needs nothing of ours to understand what is being asked.
    /// </summary>
    private static BaseEvent Terminal(TurnState state, string conversationId, string runId, string? error) =>
        error is not null
            ? new RunErrorEvent { Message = error, Code = "TurnFailed" }
            : new RunFinishedEvent
            {
                ThreadId = conversationId,
                RunId = runId,
                Outcome = state.Interrupt is { } interrupt
                    ? new RunFinishedInterruptOutcome { Interrupts = [interrupt] }
                    : new RunFinishedSuccessOutcome(),
                // The turn this run was, so feedback can name it.
                Result = JsonSerializer.SerializeToElement(new { turnId = state.TurnId }, AGUIStream.Json),
            };

    /// <summary>Agent function middleware: events before/after execution, audit, data-block wrapping, source capture.</summary>
    private async ValueTask<object?> InvokeToolAsync(TurnState state, FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken ct)
    {
        var name = context.Function.Name;
        var callId = context.CallContent?.CallId ?? Guid.NewGuid().ToString("N");
        var args = ArgumentSummary.From(context.Arguments);

        // Once a write is waiting for a person, the turn has said all it can say.
        if (state.AwaitingConfirmation)
        {
            state.Trace.Add(TraceKinds.ToolCall, $"{name} not called: a confirmation is pending", new JsonObject
            {
                ["callId"] = callId, ["tool"] = name,
            });
            return ToolDataEnvelope.Wrap(name, "An adjustment is waiting for the advisor to confirm. Nothing further happens until they answer.");
        }

        state.Answer.Flush();
        state.Reasoning.Flush();
        state.Arguments[callId] = args;
        state.Trace.Add(TraceKinds.ToolCall, $"Calling {name} over MCP", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["arguments"] = TraceMapping.Node(context.Arguments),
        });

        var sw = Stopwatch.StartNew();
        object? result;
        // The call out to MCP: no library writes this span, and without it a slow turn cannot be split between
        // the model and the tools. The tool's name travels; its arguments do not.
        using var span = LabTelemetry.Source.StartActivity("tool.call");
        span?.SetTag("tool.name", name);
        span?.SetTag("tool.call_id", callId);
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
            state.Summaries[callId] = Result(name, "failed", 0, isError: true);
            return ToolDataEnvelope.Wrap(name, "The tool is temporarily unavailable.");
        }

        var latency = sw.ElapsedMilliseconds;

        // A proposal is not a result: the server asked for a person, and the client took the question down
        // rather than answering it. What happens next is the flow's business, not the model's.
        if (state.Confirmations.Captured is { } captured && name == FeeAdjustmentTool.Name)
        {
            return await ProposedAsync(state, captured, callId, name, latency, ct);
        }

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
        // Counted where the audit row is written, so the two can never disagree about what happened.
        LabTelemetry.Instruments.ToolCalls.Add(1,
            new KeyValuePair<string, object?>("tool.name", name),
            new KeyValuePair<string, object?>("outcome", outcome));
        state.Trace.Add(TraceKinds.Audit, $"Audit: {name} {outcome}", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["arguments"] = args, ["outcome"] = outcome, ["durationMs"] = latency,
        });
        state.ToolCalls.Add(new ToolCallRecord(name, args, outcome, sources.Count, sources.Select(s => s.DocId).Distinct().ToList(), [], callId, summary));
        state.Summaries[callId] = Result(name, summary, sources.Count, isError);
        var envelope = ToolDataEnvelope.Wrap(name, payload);
        state.Trace.Add(TraceKinds.Envelope, $"Data envelope handed to the model ({envelope.Length} chars)", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["text"] = envelope,
        });
        return envelope;
    }

    /// <summary>
    /// What a tool call's result says to whoever is watching: which tool, how it went, and how many sources it
    /// found — never the result itself. A tool result is structured content, so this is one too.
    /// </summary>
    private static string Result(string tool, string summary, int sourceCount, bool isError) =>
        JsonSerializer.Serialize(new { tool, summary, sourceCount, isError }, Json);

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

    /// <summary>
    /// A write was proposed. Either it goes to a person — and this turn ends there — or it does not, and the
    /// model is told why in words it can pass on. Nothing has been written either way.
    /// </summary>
    private async ValueTask<object?> ProposedAsync(TurnState state, CapturedConfirmation captured, string callId,
        string name, long latency, CancellationToken ct)
    {
        var outcome = await adjustments.ProposedAsync(
            state.Principal, state.ConversationId, state.TurnId, callId, name, state.UserMessage, captured, state.Trace, ct);

        var summary = $"proposed {captured.Adjustment.Amount:0.##} on {captured.Adjustment.AccountId}";
        state.ToolCalls.Add(new ToolCallRecord(name, $"accountId={captured.Adjustment.AccountId}", "input_required", 0, [], [], callId, summary));
        state.Summaries[callId] = Result(name, summary, 0, isError: false);

        if (outcome is FlowOutcome.AskUser ask)
        {
            state.Trace.Add(TraceKinds.Adjustment, $"Waiting for the advisor to confirm {ask.Interrupt.Id}", new JsonObject
            {
                ["callId"] = callId,
                ["step"] = "awaiting_confirmation",
                ["adjustmentId"] = ask.Interrupt.Id,
                ["accountId"] = captured.Adjustment.AccountId,
            });
            state.Interrupt = ask.Interrupt;
            state.Proposal = captured.Adjustment;
            state.AwaitingConfirmation = true;
            // The model gets nothing more to say this turn: the next word is the advisor's.
            return ToolDataEnvelope.Wrap(name, "Waiting for the advisor to confirm. Nothing has been changed.");
        }

        var told = ((FlowOutcome.TellModel)outcome).Message;
        var envelope = ToolDataEnvelope.Wrap(name, told);
        state.Trace.Add(TraceKinds.Envelope, $"Data envelope handed to the model ({envelope.Length} chars)", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["text"] = envelope,
        });
        return envelope;
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
            case FeeAdjustmentTool.Name:
                return (Str(s, "status") switch
                {
                    "applied" => "applied",
                    "already_applied" => "already applied",
                    "declined" => "declined by the advisor",
                    _ => "nothing applied",
                }, sources);
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
        state.Reasoning.Flush();
        state.Arguments[call.CallId] = "";
        state.Summaries[call.CallId] = Result(name, "tool does not exist", 0, isError: true);
        state.Trace.Add(TraceKinds.ToolUnknown, $"Model asked for unknown tool '{name}' — refused", new JsonObject { ["callId"] = call.CallId, ["tool"] = name });
        await audit.RecordAsync(new AuditEntry(state.Principal, state.ConversationId, state.TurnId, name, "", "unknown_tool", 0), ct);
        LabTelemetry.Instruments.ToolCalls.Add(1,
            new KeyValuePair<string, object?>("tool.name", name),
            new KeyValuePair<string, object?>("outcome", "unknown_tool"));
        state.Trace.Add(TraceKinds.Audit, $"Audit: {name} unknown_tool", new JsonObject
        {
            ["callId"] = call.CallId, ["tool"] = name, ["arguments"] = "", ["outcome"] = "unknown_tool", ["durationMs"] = 0,
        });
        state.ToolCalls.Add(new ToolCallRecord(name, "", "unknown_tool", 0, [], [], call.CallId, "tool does not exist"));
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

    private sealed class TurnState(Principal principal, string conversationId, string turnId, ChannelWriter<BaseEvent> events, TurnTrace trace)
    {
        public TurnTrace Trace { get; } = trace;
        public AnswerChunker Answer { get; } = new(trace);

        /// <summary>What the model thought on its way to the answer, chunked into the trace the same way.</summary>
        public AnswerChunker Reasoning { get; } = new(trace, TraceKinds.ReasoningDelta, "Reasoning");
        public Principal Principal { get; } = principal;
        public string ConversationId { get; } = conversationId;
        public string TurnId { get; } = turnId;
        public ChannelWriter<BaseEvent> Events { get; } = events;
        public IReadOnlySet<string> KnownTools { get; set; } = new HashSet<string>();
        public List<ToolCallRecord> ToolCalls { get; } = [];
        public List<SourceRef> Sources { get; } = [];
        public bool ZeroResults { get; set; }

        /// <summary>The user's words this turn, which is what a reviewer's question gets answered with.</summary>
        public string UserMessage { get; set; } = "";

        /// <summary>Catches a request for a person's approval instead of answering it.</summary>
        public ConfirmationSink Confirmations { get; } = new();

        /// <summary>Set when the turn has ended waiting for a person.</summary>
        public bool AwaitingConfirmation { get; set; }

        /// <summary>What the run is waiting on, when it is waiting.</summary>
        public AGUIInterrupt? Interrupt { get; set; }

        /// <summary>The proposal behind that interrupt, for anything that judges what was put to the person.</summary>
        public Maf.Lab.Domain.Billing.FeeAdjustmentSummary? Proposal { get; set; }

        /// <summary>Identifier-only argument summaries by tool-call id, for what reaches the client.</summary>
        public Dictionary<string, string> Arguments { get; } = [];

        /// <summary>Result summaries by tool-call id, for the same reason.</summary>
        public Dictionary<string, string> Summaries { get; } = [];
    }
}
