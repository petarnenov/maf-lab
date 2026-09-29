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
using Maf.Lab.Domain.Portfolio;
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

    /// <summary>Jev's check of the answer; null for a turn that ran none (refused, waiting for a person, failed, empty).</summary>
    public Jev.AnswerCheck? AnswerCheck { get; init; }

    /// <summary>The data cards the turn showed, in the order they were sent (add-activity-cards).</summary>
    public IReadOnlyList<TurnCard> Cards { get; init; } = [];
}

/// <summary>One data card: the AG-UI activity a carded tool result became, keyed by the call it came from.</summary>
public sealed record TurnCard(string CallId, string MessageId, string ActivityType, JsonElement Content);

/// <summary>
/// Runs one chat turn through the Microsoft Agent Framework agent and publishes SSE events.
/// Retrieval happens only when the model calls search_documents over MCP; this class never queries the store.
/// </summary>
public sealed partial class ChatTurnRunner(
    IChatClientFactory models,
    IIntentClassifier intents,
    IToolSource toolSource,
    SystemPrompt prompt,
    ToolAudit audit,
    TokenCounter tokens,
    FeeAdjustmentFlow adjustments,
    Guardrail guardrail,
    Jev.JevAnswerCheck answerCheck,
    IDbContextFactory<MafDbContext> db,
    IOptions<AgentOptions> options,
    IOptions<Telemetry.TelemetryQueryOptions> telemetry,
    TimeProvider time,
    ILoggerFactory loggers)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger = loggers.CreateLogger<ChatTurnRunner>();

    /// <param name="clientState">
    /// The run's AG-UI state as the client sent it (add-focus-state). Only its <c>focus</c> is read, and only an account
    /// this conversation's cards have shown is accepted.
    /// </param>
    public async Task<TurnResult> RunAsync(Principal principal, string bearerToken, string conversationId, string message,
        string runId, ChannelWriter<BaseEvent> events, CancellationToken ct, JsonElement? clientState = null)
    {
        var turnId = $"t_{Guid.NewGuid():N}";
        await events.WriteAsync(new RunStartedEvent { ThreadId = conversationId, RunId = runId }, ct);
        var traceId = Activity.Current?.TraceId.ToHexString();
        var trace = new TurnTrace(events);
        var state = new TurnState(principal, conversationId, turnId, events, trace);
        // The state the turn starts with goes out first, right after the run starts (add-focus-state).
        await ResolveFocusAsync(state, clientState, ct);
        var chunker = state.Answer;
        var reasoning = state.Reasoning;
        var decision = new IntentDecision(Intent.Other);
        PromptScreen? screen = null;
        // A first question Jev put in no domain: answered with the fixed reply, like a refused prompt, with no model call.
        var outOfScope = false;
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
        if (state.StartingFocus is { } startingFocus)
        {
            trace.Add(TraceKinds.Focus, startingFocus.Title, startingFocus.Data);
        }
        var forced = false;
        // Whether the turn's answer came from the model — a refused prompt's came from the guard.
        var reachedModel = false;
        string? error = null;
        var answer = new StringBuilder();
        var sw = Stopwatch.StartNew();
        LabTelemetry.Instruments.Turns.Add(1, new KeyValuePair<string, object?>("outcome", "started"));

        try
        {
            (state.PreviousQuestion, state.PreviousTurnId) = await MarkRephraseAsync(conversationId, message, ct);
            state.UserMessage = message;

            decision = await intents.ClassifyAsync(message, ct, state.Focus);
            // The prompt's screening answered in the same request; a refused prompt forces nothing and runs nothing.
            screen = guardrail.JudgePrompt(decision);
            // Only a conversation's first question: a follow-up ("and June?", "why?") can be about the domain without
            // naming it, so from the second turn on the system prompt's scope rule is what keeps the model on topic.
            outOfScope = !screen.Blocked && decision.OutsideDomains && state.PreviousQuestion is null;

            // A refused prompt reaches no model, so this turn reads no tools over MCP and builds no prompt: the tool
            // schemas and the system prompt — what an injection may be trying to extract — are neither fetched nor traced.
            // A question outside every domain is not an attack, but it has nothing for the model either.
            await using var tools = screen.Blocked || outOfScope ? null : await toolSource.GetToolsAsync(bearerToken, state.Confirmations, ct);
            Jev.ToolRoute? route = null;
            IReadOnlyList<string> forcedSearches = [];
            IReadOnlyList<Jev.ToolRoute> alongside = [];
            if (tools is not null)
            {
                reachedModel = true;
                state.KnownTools = tools.Names;
                state.Tools = tools;
                // A forcing intent searches every domain Jev put the question in, each through its own server's search.
                forcedSearches = IntentClassifier.ForcesRetrieval(decision.Intent) ? ForcedSearches(decision.Domains, tools) : [];
                forced = forcedSearches.Count > 0;
                alongside = forced ? Alongside(decision, message, tools) : [];
                // A data turn Jev routed to a read tool this server offers: the call is issued without the model's first
                // call. Never a write — the router has no write to offer.
                route = !forced && decision.Route is { } r && tools.Names.Contains(r.Tool) ? r : null;
            }

            var outside = decision.Reason?.StartsWith("outside the domain", StringComparison.Ordinal) == true
                ? $", outside the domain {decision.Domains?.Highest ?? decision.InDomain ?? 0:F2}"
                : "";
            var jev = decision.Confidence is { } confidence ? $" (jev {confidence:F2}{outside}, {decision.DurationMs:F0} ms)" : "";
            var routed = route is null ? "" : $" → routing {route.Tool} (jev {route.Probability:F2})";
            var refusedScope = outOfScope ? $" → outside every domain ({decision.Domains?.Highest ?? 0:F2}), fixed reply" : "";
            trace.Add(TraceKinds.Intent, $"Intent {decision.Intent}{jev}{(forced ? $" → forcing {string.Join(" + ", forcedSearches.Concat(alongside.Select(a => a.Tool)))}" : "")}{routed}{refusedScope}", new JsonObject
            {
                ["intent"] = decision.Intent.ToString(),
                ["forcedRetrieval"] = forced,
                ["forcedTool"] = forced ? forcedSearches[0] : null,
                ["forcedTools"] = new JsonArray([.. forcedSearches.Concat(alongside.Select(a => a.Tool)).Select(t => (JsonNode)JsonValue.Create(t)!)]),
                ["choice"] = decision.Choice,
                ["probabilities"] = decision.Probabilities is { } p
                    ? new JsonObject(p.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)))
                    : null,
                ["confidence"] = decision.Confidence,
                ["inDomain"] = decision.InDomain,
                ["model"] = decision.Model,
                ["durationMs"] = decision.DurationMs,
                ["reason"] = decision.Reason,
                ["routing"] = Routing(decision, route),
                ["domains"] = decision.Domains is { } d
                    ? new JsonObject(d.Probabilities.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)))
                    : null,
                ["outsideDomains"] = decision.OutsideDomains,
                ["outOfScopeReply"] = outOfScope,
            });
            TraceDomains(trace, decision.Domains, forcedSearches, tools);
            guardrail.Trace(trace, Guardrail.CheckPrompt, null, null, screen.Decision, screen.Threshold,
                [new ScreenedItem(0, screen.Decision, screen.Scores)], 0, screen.Reason);

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

            IAsyncEnumerable<ChatResponseUpdate> stream;
            if (tools is not null)
            {
                var chatOptions = models.BaseChatOptions();
                chatOptions.Instructions = prompt.Text + FocusNote(state.Focus);
                chatOptions.Tools = [.. tools.Tools];
                chatOptions.ToolMode = forced ? ChatToolMode.RequireSpecific(forcedSearches[0])
                    : route is not null ? ChatToolMode.RequireSpecific(route.Tool)
                    : ChatToolMode.Auto;
                trace.Add(TraceKinds.Prompt, $"System prompt {prompt.Version} + {tools.Tools.Count} tool(s) from {string.Join(" + ", tools.OfferedDomains)}", new JsonObject
                {
                    ["version"] = prompt.Version,
                    // What the model is actually given, the focus note included.
                    ["systemPrompt"] = chatOptions.Instructions,
                    ["focusNote"] = state.FocusCleared ? ClearedFocusNote : null,
                    ["toolMode"] = TraceMapping.ToolMode(chatOptions.ToolMode),
                    ["domains"] = new JsonArray([.. tools.OfferedDomains.Select(x => (JsonNode)JsonValue.Create(x)!)]),
                    ["unavailableDomains"] = new JsonArray([.. tools.Unavailable.Select(x => (JsonNode)JsonValue.Create(x)!)]),
                    ["tools"] = new JsonArray(tools.Tools.OfType<AIFunctionDeclaration>().Select(t => (JsonNode)new JsonObject
                    {
                        ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = TraceMapping.Node(t.JsonSchema),
                        ["domain"] = tools.DomainOf(t.Name), ["server"] = tools.ServerOf(t.Name),
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
                // tool_choice names one function: a turn that must issue several calls up front — a crossing's searches,
                // a run's status beside them — is emulated whatever the provider supports, as a routed call is.
                if (options.Value.EmulateRequiredToolMode || route is not null || forcedSearches.Count > 1 || alongside.Count > 0)
                {
                    chatClient = new RequiredToolModeChatClient(chatClient, call => trace.Add(TraceKinds.ToolForced,
                        call.Name == route?.Tool ? $"Routed {call.Name} issued on the model's behalf" : $"Forced {call.Name} issued on the model's behalf",
                        new JsonObject
                        {
                            ["callId"] = call.CallId,
                            ["tool"] = call.Name,
                            ["domain"] = tools.DomainOf(call.Name),
                            ["server"] = tools.ServerOf(call.Name),
                            ["arguments"] = TraceMapping.Node(call.Arguments),
                            ["reason"] = call.Name == route?.Tool
                                ? $"Data intent routed by Jev ({route.Tool} {route.Probability:F2}); the call is issued without asking the model which tool to use."
                                : alongside.Any(a => a.Tool == call.Name)
                                    ? "Mixed intent about one named run: its state is read together with the documentation, without asking the model."
                                : forcedSearches.Count > 1
                                    ? $"Procedural intent requires retrieval in every domain in scope ({string.Join(", ", decision.Domains?.InScope ?? [])}); the searches are issued together without asking the model."
                                    : "Procedural intent requires retrieval; the provider ignores tool_choice, so the call is issued without asking the model.",
                        }), route, forcedSearches, alongside);
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
                stream = Observed(agent, session, message, state, tools.Names, answer, chunker, reasoning, ct);
            }
            else
            {
                // A refused prompt never reaches the model — not this turn, and not the next one's history, which is
                // written only by a run of the agent. The same holds for a question outside every domain.
                stream = Refused(outOfScope ? OutOfScope.Reply(message) : Guardrail.Refusal(message), answer, chunker);
            }

            await foreach (var e in stream.AsAGUIEventStreamAsync(context, ct))
            {
                if (redaction.Apply(e) is { } send)
                {
                    await events.WriteAsync(send, ct);
                    // A carded result's card follows its tool-call result, so the card and the call it belongs to
                    // arrive together — before the model has written a word about them.
                    if (send is ToolCallResultEvent result && state.PendingCards.Remove(result.ToolCallId, out var card))
                    {
                        await events.WriteAsync(AGUIStream.Card(card.CallId, card.ActivityType, card.Content), ct);
                        // The focus the read moved follows its card, so the client learns both together.
                        if (state.PendingFocus.Remove(result.ToolCallId, out var moved))
                        {
                            await events.WriteAsync(AGUIStream.State(moved), ct);
                        }
                    }
                }
            }
            // A card whose result event never came through (a result the adapter did not render) still belongs to the turn.
            foreach (var card in state.PendingCards.Values.ToList())
            {
                await events.WriteAsync(AGUIStream.Card(card.CallId, card.ActivityType, card.Content), ct);
            }
            state.PendingCards.Clear();
            if (state.PendingFocus.Count > 0)
            {
                await events.WriteAsync(AGUIStream.State(state.Focus), ct);
                state.PendingFocus.Clear();
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
        // The answer has streamed, so the check cannot block it; it runs before the trace is stored and the run ends, so
        // both carry it. A refused prompt, a pause for a person and a failure have no answer of the model's to check.
        Jev.AnswerCheck? check = null;
        if (reachedModel && error is null && !state.AwaitingConfirmation && text.Length > 0)
        {
            check = await answerCheck.CheckAsync(message, text, state.Read, ct, state.PreviousQuestion,
                await PreviousReadAsync(state.PreviousTurnId, ct));
            Jev.JevAnswerCheck.Trace(trace, check);
        }
        // A refused turn ran no tool on purpose: that is the guard's signal, not "how/why answered without a tool".
        var signals = TurnSignals.Compute(screen?.Blocked == true || outOfScope ? Intent.Other : decision.Intent, state.ToolCalls.Count, state.Searched,
            text.Length, sources.Count, options.Value.LongAnswerChars);
        if (outOfScope)
        {
            // In the review queue, so a question wrongly judged off-domain is found and labelled.
            signals.Add(TurnSignal.OutOfScope);
        }
        signals.AddRange(Guardrail.Signals(trace.Events).Distinct().Where(s => !signals.Contains(s)).ToList());
        signals.AddRange((check?.Signals ?? []).Where(s => !signals.Contains(s)).ToList());
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
        var path = state.DomainPath;
        var crossed = path.Count > 1 ? $" across {string.Join(" → ", path)}" : "";
        trace.Add(TraceKinds.TurnEnd, $"Turn finished in {sw.ElapsedMilliseconds} ms{crossed}{(error is null ? "" : " with an error")}", new JsonObject
        {
            ["durationMs"] = sw.ElapsedMilliseconds,
            ["error"] = error,
            ["answerChars"] = text.Length,
            ["toolCalls"] = state.ToolCalls.Count,
            ["sourceCount"] = sources.Count,
            // Where the turn actually went, beside where Jev said it would: the two disagreeing is worth a look.
            ["domainPath"] = new JsonArray([.. path.Select(x => (JsonNode)JsonValue.Create(x)!)]),
            ["domainsTouched"] = new JsonArray([.. path.Distinct().Select(x => (JsonNode)JsonValue.Create(x)!)]),
            ["domainsPredicted"] = new JsonArray([.. (decision.Domains?.InScope ?? []).Select(x => (JsonNode)JsonValue.Create(x)!)]),
            ["crossings"] = Math.Max(0, path.Count - 1),
        }, sw.ElapsedMilliseconds);
        await PersistAsync(principal, conversationId, turnId, message, text, decision.Intent, forced, state.ToolCalls, sources, signals, state.Cards, trace, ct);
        if (state.FocusMoved)
        {
            await SaveFocusAsync(conversationId, state.Focus, ct);
        }

        _logger.LogInformation("chat turn done turn={TurnId} intent={Intent} forced={Forced} tools={ToolCount} sources={SourceCount} signals={Signals} answerCheck={AnswerCheck} ms={Elapsed}",
            turnId, decision.Intent, forced, state.ToolCalls.Count, sources.Count, string.Join(",", signals), check?.Verdict ?? "none", sw.ElapsedMilliseconds);

        await events.WriteAsync(Terminal(state, conversationId, runId, error), ct);
        return new TurnResult(conversationId, turnId, decision.Intent, forced, text, state.ToolCalls, sources, signals, error)
        {
            Proposal = state.Proposal,
            ProposalQuestion = state.Interrupt?.Message,
            Cards = state.Cards,
            AnswerCheck = check,
        };
    }

    /// <summary>
    /// The documentation searches a forcing intent issues: one per domain in scope whose server is offering its search.
    /// With no domain verdict at all the turn behaves as it did before domains existed — billing's search, if offered.
    /// </summary>
    internal static IReadOnlyList<string> ForcedSearches(DomainVerdict? domains, ToolSet tools)
    {
        if (domains is not { InScope.Count: > 0 })
        {
            return tools.Names.Contains(Domains.SearchTool[Domains.Billing]) ? [Domains.SearchTool[Domains.Billing]] : [];
        }
        return [.. domains.InScope
            .Select(d => Domains.SearchTool.GetValueOrDefault(d))
            .OfType<string>()
            .Where(tools.Names.Contains)];
    }

    /// <summary>
    /// The read call a mixed question needs beside its documentation: the status of the one run it names, when billing is
    /// in scope and the server offers the tool. The run id comes from the question through the router's fixed pattern,
    /// never from the model; two run ids, or none, and the model decides as before.
    /// </summary>
    internal static IReadOnlyList<Jev.ToolRoute> Alongside(IntentDecision decision, string question, ToolSet tools)
    {
        var status = Maf.Lab.Retrieval.Tools.BillingTools.GetStatusName;
        if (decision.Intent != Intent.Mixed || !tools.Names.Contains(status)
            || decision.Domains is { InScope.Count: > 0 } d && !d.InScope.Contains(Domains.Billing))
        {
            return [];
        }
        var runs = Jev.DataToolRouter.RunIds(question);
        return runs.Count == 1 ? [new Jev.ToolRoute(status, new Dictionary<string, object?> { ["runId"] = runs[0] }, decision.Confidence ?? 0)] : [];
    }

    /// <summary>
    /// Where Jev put the question among the domains: each domain's probability against the scope floor, the domains in
    /// scope, and whether the question crosses the boundary between them. Nothing when Jev gave no domain answer.
    /// </summary>
    private static void TraceDomains(TurnTrace trace, DomainVerdict? domains, IReadOnlyList<string> forcedSearches, ToolSet? tools)
    {
        if (domains is null)
        {
            return;
        }
        var scores = string.Join(" · ", domains.Probabilities
            .OrderBy(p => Domains.All.ToList().IndexOf(p.Key))
            .Select(p => $"{p.Key} {p.Value:F2}"));
        var title = domains.Crossing ? $"Domains: {scores} → crosses {string.Join(" ↔ ", domains.InScope)}"
            : domains.Primary is { } primary ? $"Domain {primary} ({scores})"
            : $"No domain in scope ({scores})";
        trace.Add(TraceKinds.Domain, title, new JsonObject
        {
            ["probabilities"] = new JsonObject(domains.Probabilities.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
            ["scopeFloor"] = domains.ScopeFloor,
            ["inScope"] = new JsonArray([.. domains.InScope.Select(x => (JsonNode)JsonValue.Create(x)!)]),
            ["primary"] = domains.Primary,
            ["crossing"] = domains.Crossing,
            ["forcedSearches"] = new JsonArray([.. forcedSearches.Select(x => (JsonNode)JsonValue.Create(x)!)]),
            ["offered"] = tools is null ? null : new JsonArray([.. tools.OfferedDomains.Select(x => (JsonNode)JsonValue.Create(x)!)]),
            ["unavailable"] = tools is null ? null : new JsonArray([.. tools.Unavailable.Select(x => (JsonNode)JsonValue.Create(x)!)]),
        });
    }

    /// <summary>
    /// Jev's routing answer as the trace shows it: each tool's probability, the status asked about, and the call issued
    /// or why none was. Null when routing is off or Jev gave no routing answer at all.
    /// </summary>
    private static JsonObject? Routing(IntentDecision decision, Jev.ToolRoute? route)
    {
        if (decision.Routing is null && decision.RouteReason is null)
        {
            return null;
        }
        return new JsonObject
        {
            ["tools"] = decision.Routing is { } r
                ? new JsonObject(r.Tools.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)))
                : null,
            ["status"] = decision.Routing?.Status,
            ["statusConfidence"] = decision.Routing?.StatusConfidence,
            ["routedTool"] = route?.Tool,
            ["arguments"] = route is null ? null : TraceMapping.Node(route.Arguments),
            ["reason"] = route is null ? decision.RouteReason ?? (decision.Route is { } unoffered ? $"{unoffered.Tool} is not offered" : null) : null,
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
        // A cleared focus is said right before the question, where it outweighs the account the history keeps
        // mentioning. It is a system message, which the history provider does not store (add-focus-state).
        IEnumerable<ChatMessage> request = state.FocusCleared
            ? [new ChatMessage(ChatRole.System, ClearedFocusNote), new ChatMessage(ChatRole.User, message)]
            : [new ChatMessage(ChatRole.User, message)];
        await foreach (var update in agent.RunStreamingAsync(request, session, cancellationToken: ct))
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

    /// <summary>The fixed refusal, as the one update of a run that called no model.</summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> Refused(string refusal, StringBuilder answer, AnswerChunker chunker)
    {
        answer.Append(refusal);
        chunker.Append(refusal);
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, refusal)
        {
            MessageId = $"msg_{Guid.NewGuid():N}",
            ResponseId = $"resp_{Guid.NewGuid():N}",
        };
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

        // The user let go of the account in focus and this question names none: code, not the model's reading of the
        // history, decides that no account is assumed (add-focus-state). The model is told to ask instead.
        if (state.FocusCleared && name is PortfolioTools.GetPortfolio or PortfolioTools.AumHistory
            && Jev.DataToolRouter.AccountIds(state.UserMessage).Count == 0)
        {
            state.Trace.Add(TraceKinds.Focus, $"{name} not called: the user cleared the account in focus", new JsonObject
            {
                ["callId"] = callId, ["tool"] = name, ["source"] = "cleared",
            });
            return ToolDataEnvelope.Wrap(name,
                "Not called: the user cleared the account in focus and this question names no account. Ask which account they mean.");
        }

        state.Answer.Flush();
        state.Reasoning.Flush();
        state.Arguments[callId] = args;
        var domain = state.Tools?.DomainOf(name) ?? Domains.Billing;
        var server = state.Tools?.ServerOf(name) ?? ToolSet.DefaultServer;
        EnterDomain(state, domain, name, callId);
        state.Trace.Add(TraceKinds.ToolCall, $"Calling {name} over MCP ({domain})", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["domain"] = domain, ["server"] = server, ["arguments"] = TraceMapping.Node(context.Arguments),
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
                ["callId"] = callId, ["tool"] = name, ["domain"] = domain, ["server"] = server, ["isError"] = true,
                ["latencyMs"] = sw.ElapsedMilliseconds, ["result"] = null,
            }, sw.ElapsedMilliseconds);
            state.ToolCalls.Add(new ToolCallRecord(name, args, "error", 0, [], [], callId, "failed"));
            state.Summaries[callId] = Result(name, "failed", 0, isError: true);
            const string unavailable = "The tool is temporarily unavailable.";
            state.Read.Add($"{name}: {unavailable}");
            return ToolDataEnvelope.Wrap(name, unavailable);
        }

        var latency = sw.ElapsedMilliseconds;

        // A proposal is not a result: the server asked for a person, and the client took the question down
        // rather than answering it. What happens next is the flow's business, not the model's.
        if (state.Confirmations.Captured is { } captured && name == FeeAdjustmentTool.Name)
        {
            return await ProposedAsync(state, captured, callId, name, latency, ct);
        }

        var (payload, structured, isError) = ToolDataEnvelope.Unpack(result);
        // What the tool returned is judged before anything is derived from it — and before it is traced: a withheld
        // item is neither read by the model, cited as a source, nor written to the trace, only recorded as withheld.
        // Unscreened (Jev down) fails open — the envelope still frames it as data.
        var screened = await guardrail.ScreenToolResultAsync(name, payload, structured, isError, ct);
        TraceToolResult(state.Trace, callId, name, domain, server, result, screened, isError, latency);
        if (screened is not null)
        {
            guardrail.Trace(state.Trace, Guardrail.CheckToolResult, name, callId, screened.Decision, screened.Threshold, screened.Items,
                screened.Withheld, null, screened.Requests, screened.ElapsedMs);
            (payload, structured) = (screened.Payload, screened.Structured);
        }
        var (summary, sources) = screened is { WholeWithheld: true }
            ? ("withheld by the content guard", new List<SourceRef>())
            : Summarise(name, structured, isError);
        state.Sources.AddRange(sources);
        // A result the client may see whole becomes a data card: only an allow-listed tool's successful, structured
        // result the guard let through. It is sent right after this call's result event (see the run loop).
        if (!isError && screened is not { WholeWithheld: true } && structured is { } data && AGUIStream.Cards.TryGetValue(name, out var carded))
        {
            var card = new TurnCard(callId, AGUIStream.CardMessageId(callId), carded.ActivityType, data.Clone());
            state.Cards.Add(card);
            state.PendingCards[callId] = card;
            // A read of one account's portfolio or AUM puts that account in focus; the list of accounts does not.
            if (name is PortfolioTools.GetPortfolio or PortfolioTools.AumHistory
                && data.TryGetProperty("accountId", out var read) && read.GetString() is { } readId && readId != state.Focus)
            {
                state.Trace.Add(TraceKinds.Focus, $"Focus moved to {readId} by {name}", new JsonObject
                {
                    ["accountId"] = readId, ["source"] = "read", ["previous"] = state.Focus, ["callId"] = callId,
                });
                state.Focus = readId;
                state.FocusMoved = true;
                state.PendingFocus[callId] = readId;
            }
            state.Trace.Add(TraceKinds.Card, $"Data card {carded.ActivityType}", new JsonObject
            {
                ["callId"] = callId, ["messageId"] = card.MessageId, ["activityType"] = card.ActivityType,
                ["content"] = JsonNode.Parse(data.GetRawText()),
            });
        }
        if (Domains.IsSearch(name) && !isError)
        {
            // Whether this call found anything is not the question: a turn that searches again and succeeds has
            // nothing wrong with it. What matters is that the turn asked the documentation at all.
            state.Searched = true;
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
        Read(state, name, payload, structured, isError);
        var envelope = ToolDataEnvelope.Wrap(name, payload);
        state.Trace.Add(TraceKinds.Envelope, $"Data envelope handed to the model ({envelope.Length} chars)", new JsonObject
        {
            ["callId"] = callId, ["tool"] = name, ["text"] = envelope,
        });
        return envelope;
    }

    /// <summary>
    /// What the model was handed, as the answer check reads it: a documentation search's excerpts one by one, any other
    /// result whole, prefixed with its tool. Called after the guard, so a withheld item is never among them.
    /// </summary>
    private static void Read(TurnState state, string tool, string payload, JsonElement? structured, bool isError)
    {
        // An empty search is read whole: its hint is what the model was told.
        if (!isError && Domains.IsSearch(tool) && structured is { } s && s.TryGetProperty("results", out var results)
            && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
        {
            foreach (var r in results.EnumerateArray())
            {
                state.Read.Add($"{Str(r, "docId")} › {Str(r, "sectionPath")}: {Str(r, "snippet")}");
            }
            return;
        }
        state.Read.Add($"{tool}: {payload}");
    }

    /// <summary>
    /// Records the domain a call belongs to, and — when it differs from the previous call's — the boundary the turn just
    /// crossed. Derived from the tool set, never from anything the model said: the crossing is what the calls did.
    /// </summary>
    private static void EnterDomain(TurnState state, string domain, string tool, string callId)
    {
        var from = state.DomainPath.Count > 0 ? state.DomainPath[^1] : null;
        if (from == domain)
        {
            return;
        }
        state.DomainPath.Add(domain);
        if (from is null)
        {
            return;
        }
        state.Trace.Add(TraceKinds.Boundary, $"Crossed {from} → {domain} with {tool}", new JsonObject
        {
            ["from"] = from,
            ["to"] = domain,
            ["tool"] = tool,
            ["callId"] = callId,
            ["server"] = state.Tools?.ServerOf(tool),
            ["hop"] = state.DomainPath.Count - 1,
        });
    }

    /// <summary>
    /// What a tool call's result says to whoever is watching: which tool, how it went, and how many sources it
    /// found — never the result itself. A tool result is structured content, so this is one too.
    /// </summary>
    private static string Result(string tool, string summary, int sourceCount, bool isError) =>
        JsonSerializer.Serialize(new { tool, summary, sourceCount, isError }, Json);

    /// <summary>
    /// Raw MCP result (diagnostics removed) and, when the server sent them, the retrieval diagnostics. When the guard
    /// withheld anything, the redacted result the model may read — carrying the neutral notice and a count — is recorded
    /// in place of the raw one, so a withheld item's content never enters the trace.
    /// </summary>
    private static void TraceToolResult(TurnTrace trace, string callId, string tool, string domain, string server, object? result,
        ScreenedToolResult? screened, bool isError, long latencyMs)
    {
        var raw = TraceMapping.Node(result) as JsonObject;
        JsonNode? diagnostics = null;
        JsonObject? relevance = null;
        string? instance = null;
        if (raw?["_meta"] is JsonObject meta)
        {
            diagnostics = meta[TraceMeta.Diagnostics]?.DeepClone();
            relevance = meta[TraceMeta.Relevance]?.DeepClone() as JsonObject;
            instance = meta[TraceMeta.Instance]?.GetValue<string>();
            meta.Remove(TraceMeta.Diagnostics);
            meta.Remove(TraceMeta.Relevance);
            if (meta.Count == 0)
            {
                raw.Remove("_meta");
            }
        }
        JsonNode? recorded = screened is { Withheld: > 0 }
            ? (screened.Structured is { } s ? JsonNode.Parse(s.GetRawText()) : new JsonObject { ["withheld"] = screened.Withheld })
            : raw;
        trace.Add(TraceKinds.ToolResult, $"{tool} returned{(isError ? " an error" : "")} in {latencyMs} ms{(instance is null ? "" : $" from {instance}")}", new JsonObject
        {
            ["callId"] = callId, ["tool"] = tool, ["domain"] = domain, ["server"] = server, ["isError"] = isError, ["latencyMs"] = latencyMs,
            ["mcpInstance"] = instance, ["result"] = recorded,
        }, latencyMs);
        if (diagnostics is JsonObject d)
        {
            d["callId"] = callId;
            var fused = (d["fused"] as JsonArray)?.Count ?? 0;
            trace.Add(TraceKinds.Retrieval, $"Retrieval: {d["settings"]?["mode"]} search, {fused} fused candidate(s)", d);
            // An MCP server from before the summary existed: the diagnostics hold the same judgment.
            relevance ??= RelevanceFromDiagnostics(d);
        }
        if (relevance is not null)
        {
            relevance["callId"] = callId;
            var ms = relevance["durationMs"]?.GetValue<double>() ?? 0;
            trace.Add(TraceKinds.Relevance, RelevanceTitle(relevance), relevance, (long)ms);
        }
    }

    /// <summary>The judgment as the summary carries it, read from full diagnostics — without the per-candidate scores.</summary>
    private static JsonObject? RelevanceFromDiagnostics(JsonObject diagnostics)
    {
        if (diagnostics["relevance"] is not JsonObject r)
        {
            return null;
        }
        var silenced = r["silenced"]?.GetValue<bool>() ?? false;
        var reranker = diagnostics["settings"]?["reranker"]?.GetValue<string>();
        var rerankedByJev = reranker == "jev" && !silenced && r["reason"] is null;
        return new JsonObject
        {
            ["gate"] = r["gate"]?.DeepClone(),
            ["reranker"] = reranker,
            ["floor"] = r["floor"]?.DeepClone(),
            ["judged"] = r["judged"]?.DeepClone(),
            ["max"] = r["max"]?.DeepClone(),
            ["silenced"] = silenced,
            ["rerankedByJev"] = rerankedByJev,
            ["model"] = r["model"]?.DeepClone(),
            ["durationMs"] = r["durationMs"]?.DeepClone(),
            ["reason"] = r["reason"]?.DeepClone(),
        };
    }

    /// <summary>"Jev relevance: max 0.87 ≥ floor 0.50 — kept", "… — silenced", or why the search was left ungated.</summary>
    internal static string RelevanceTitle(JsonObject r)
    {
        var reason = r["reason"]?.GetValue<string>();
        if (reason is not null)
        {
            return $"Jev relevance unavailable: {reason} — search left ungated";
        }
        var gate = r["gate"]?.GetValue<bool>() ?? false;
        var max = r["max"]?.GetValue<double>();
        var floor = r["floor"]?.GetValue<double>() ?? 0;
        var silenced = r["silenced"]?.GetValue<bool>() ?? false;
        var reranked = r["rerankedByJev"]?.GetValue<bool>() ?? false;
        var score = max is not { } m ? "no candidates judged"
            : gate ? $"max {m:F2} {(m < floor ? "<" : "≥")} floor {floor:F2}"
            : $"max {m:F2}";
        var verdict = silenced ? "silenced" : gate ? "kept" : "kept (gate off)";
        return $"Jev relevance: {score} — {verdict}{(reranked ? " · reranked by Jev" : "")}";
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
        state.Read.Add($"{name}: {told}");
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
            case var search when Domains.IsSearch(search) && s.TryGetProperty("results", out var results):
                foreach (var r in results.EnumerateArray())
                {
                    sources.Add(new SourceRef(Str(r, "docId"), Str(r, "sectionPath"), Str(r, "sourcePath"), Str(r, "snippet")));
                }
                return (sources.Count == 0 ? "no matching documentation" : $"{sources.Count} snippet(s)", sources);
            case "get_billing_run_status":
                return ($"run {Str(s, "runId")}: {Str(s, "status")}", sources);
            case "search_billing_runs" when s.TryGetProperty("runs", out var runs):
                return ($"{runs.GetArrayLength()} run(s)", sources);
            case Maf.Lab.Domain.Portfolio.PortfolioTools.GetPortfolio:
                return ($"{Str(s, "accountId")}: {Str(s, "modelPortfolio")}"
                    + (s.TryGetProperty("outsideTolerance", out var drift) && drift.ValueKind == JsonValueKind.True ? ", outside tolerance" : ""), sources);
            case Maf.Lab.Domain.Portfolio.PortfolioTools.AumHistory when s.TryGetProperty("valuations", out var valuations):
                return ($"{Str(s, "accountId")}: {valuations.GetArrayLength()} quarter-end valuation(s)", sources);
            case Maf.Lab.Domain.Portfolio.PortfolioTools.ListAccounts when s.TryGetProperty("count", out var count):
                return ($"{count.GetInt32()} account(s)", sources);
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

    /// <summary>
    /// Marks the conversation's previous turn as rephrased when this message restates it, and returns that turn's question
    /// and id (nulls on a first turn) — what the answer check reads a follow-up against.
    /// </summary>
    private async Task<(string? Question, string? TurnId)> MarkRephraseAsync(string conversationId, string message, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var previous = await ctx.Turns.Where(t => t.ConversationId == conversationId).OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        if (previous is null || !TurnSignals.IsRephrase(previous.Question, message, time.GetUtcNow().UtcDateTime - previous.CreatedAt))
        {
            return (previous?.Question, previous?.Id);
        }
        var signals = JsonSerializer.Deserialize<List<string>>(previous.SignalsJson, Json) ?? [];
        if (!signals.Contains(TurnSignal.Rephrased))
        {
            signals.Add(TurnSignal.Rephrased);
            previous.SignalsJson = JsonSerializer.Serialize(signals, Json);
            await ctx.SaveChangesAsync(ct);
        }
        return (previous.Question, previous.Id);
    }

    /// <summary>
    /// What the model was handed in the previous turn: the text of its data envelopes, from that turn's stored trace. An
    /// envelope holds what the content guard let through, so a withheld item is not here either. Empty on a first turn,
    /// and when the trace is gone (retention) or unreadable — the check then judges against this turn's sources alone.
    /// </summary>
    private async Task<IReadOnlyList<string>> PreviousReadAsync(string? turnId, CancellationToken ct)
    {
        if (turnId is null)
        {
            return [];
        }
        await using var ctx = await db.CreateDbContextAsync(ct);
        var row = await ctx.TurnTraces.Where(t => t.TurnId == turnId).Select(t => t.Json).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<TraceEvent>>(row, TurnTrace.Json)?
                .Where(e => e.Kind == TraceKinds.Envelope && e.Data.ValueKind == JsonValueKind.Object
                    && e.Data.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                .Select(e => e.Data.GetProperty("text").GetString()!)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task PersistAsync(Principal principal, string conversationId, string turnId, string question, string answer, Intent intent, bool forced,
        IReadOnlyList<ToolCallRecord> toolCalls, IReadOnlyList<SourceRef> sources, IReadOnlyList<string> signals, IReadOnlyList<TurnCard> cards,
        TurnTrace trace, CancellationToken ct)
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
            ActivitiesJson = JsonSerializer.Serialize(cards.Select(c => new Maf.Lab.Domain.History.HistoryActivity(c.MessageId, c.ActivityType, c.Content)), Json),
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

    // ---- The account in focus (add-focus-state) ----------------------------------------------------------------------

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Z]-\d{2,}$")]
    private static partial System.Text.RegularExpressions.Regex AccountIdPattern();

    /// <summary>What the client's state says about the focus: nothing, a clear, or an account it asks for.</summary>
    private static (bool Sent, string? AccountId) ClientFocus(JsonElement? clientState)
    {
        if (clientState is not { ValueKind: JsonValueKind.Object } s || !s.TryGetProperty("focus", out var focus))
        {
            return (false, null);
        }
        return focus.ValueKind switch
        {
            JsonValueKind.Null => (true, null),
            JsonValueKind.Object when focus.TryGetProperty("accountId", out var id) && id.ValueKind == JsonValueKind.String => (true, id.GetString()),
            // Anything else is not a focus the client can mean: rejected like an account it was never shown.
            _ => (true, ""),
        };
    }

    /// <summary>
    /// The turn's starting focus: the client's choice when it cleared the focus or picked an account this conversation's
    /// cards have shown, else what the conversation stored. A rejected choice is traced without its value. The focus is
    /// sent as the run's first state snapshot.
    /// </summary>
    private async Task ResolveFocusAsync(TurnState state, JsonElement? clientState, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        var conversation = await ctx.Conversations.FirstOrDefaultAsync(c => c.Id == state.ConversationId, ct);
        var stored = conversation?.FocusAccountId;
        var focus = stored;
        var source = stored is null ? "none" : "stored";
        bool? accepted = null;
        var (sent, asked) = ClientFocus(clientState);
        if (sent && asked is null)
        {
            (focus, source, accepted) = (null, "client", true);
            // Letting go of an account is an instruction, not only an absence: the model must not take it back from history.
            state.FocusCleared = stored is not null;
        }
        else if (sent)
        {
            var offered = asked!.Length > 0 && AccountIdPattern().IsMatch(asked)
                && (await OfferedAccountsAsync(ctx, state.ConversationId, ct)).Contains(asked);
            if (offered)
            {
                (focus, source, accepted) = (asked, "client", true);
            }
            else
            {
                accepted = false;
            }
        }
        if (conversation is not null && focus != stored)
        {
            conversation.FocusAccountId = focus;
            await ctx.SaveChangesAsync(ct);
        }
        state.Focus = focus;
        // The snapshot first: it is the run's state as of its start, and nothing may come between the two. Its trace
        // event waits for turn.start, which opens every trace.
        await state.Events.WriteAsync(AGUIStream.State(focus), ct);
        state.StartingFocus = (
            accepted == false ? $"Focus from the client rejected; {focus ?? "no account"} stays" : $"Focus: {focus ?? "none"} ({source})",
            new JsonObject { ["accountId"] = focus, ["source"] = source, ["accepted"] = accepted });
    }

    /// <summary>Every account id a card in this conversation has shown: what the user may put in focus.</summary>
    private static async Task<HashSet<string>> OfferedAccountsAsync(MafDbContext ctx, string conversationId, CancellationToken ct)
    {
        var offered = new HashSet<string>(StringComparer.Ordinal);
        var stored = await ctx.Turns.AsNoTracking().Where(t => t.ConversationId == conversationId).Select(t => t.ActivitiesJson).ToListAsync(ct);
        foreach (var json in stored.Where(j => !string.IsNullOrWhiteSpace(j)))
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var activity in doc.RootElement.EnumerateArray())
            {
                if (!activity.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (content.TryGetProperty("accountId", out var id) && id.GetString() is { } one)
                {
                    offered.Add(one);
                }
                if (content.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in accounts.EnumerateArray())
                    {
                        if (a.TryGetProperty("accountId", out var aid) && aid.GetString() is { } listed)
                        {
                            offered.Add(listed);
                        }
                    }
                }
            }
        }
        return offered;
    }

    /// <summary>The note the model gets with a focus: the validated id, and nothing else interpolated.</summary>
    internal static string FocusNote(string? focus) => focus is null
        ? ""
        : $"\n\n## Conversation focus\nIf the question names no account, it is about account {focus}.";

    /// <summary>Said right before the question on the turn the user cleared the focus.</summary>
    internal const string ClearedFocusNote =
        "The user has just cleared the account in focus. If this question names no account, ask which account they mean. "
        + "Do not assume an account from earlier in the conversation, and do not call a per-account tool until they name one.";

    private async Task SaveFocusAsync(string conversationId, string? focus, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        await ctx.Conversations.Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.FocusAccountId, focus), ct);
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

        /// <summary>The turn's tools, for the domain and server each belongs to.</summary>
        public ToolSet? Tools { get; set; }

        /// <summary>The domains the turn's calls went to, in order, consecutive repeats collapsed.</summary>
        public List<string> DomainPath { get; } = [];
        public List<ToolCallRecord> ToolCalls { get; } = [];
        public List<SourceRef> Sources { get; } = [];

        /// <summary>The data the model was handed this turn, after the guard — what the answer check calls its sources.</summary>
        public List<string> Read { get; } = [];
        public bool Searched { get; set; }

        /// <summary>The conversation's previous question, for reading a follow-up; null on a first turn.</summary>
        public string? PreviousQuestion { get; set; }

        /// <summary>The previous turn, whose stored trace says what the model read for that question.</summary>
        public string? PreviousTurnId { get; set; }

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

        /// <summary>The account in focus, as the turn resolved it and as reads moved it (add-focus-state).</summary>
        public string? Focus { get; set; }
        public bool FocusMoved { get; set; }

        /// <summary>Set on the turn the user cleared a focus the conversation had.</summary>
        public bool FocusCleared { get; set; }

        /// <summary>How the turn's focus was resolved, traced once turn.start has opened the trace.</summary>
        public (string Title, JsonObject Data)? StartingFocus { get; set; }

        /// <summary>A focus a read moved, waiting to be sent after that call's card.</summary>
        public Dictionary<string, string> PendingFocus { get; } = [];

        /// <summary>The data cards the turn showed, in order; and those still waiting for their call's result event.</summary>
        public List<TurnCard> Cards { get; } = [];
        public Dictionary<string, TurnCard> PendingCards { get; } = [];
    }
}
