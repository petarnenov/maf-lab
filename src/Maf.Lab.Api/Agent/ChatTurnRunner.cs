using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Maf.Lab.Api.Storage;
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
        var state = new TurnState(principal, conversationId, turnId, events);
        var intent = IntentClassifier.Classify(message);
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

            var agent = new ChatClientAgent(
                    options.Value.EmulateRequiredToolMode ? new RequiredToolModeChatClient(models.CreateChatClient()) : models.CreateChatClient(),
                    new ChatClientAgentOptions
                    {
                        Name = "maf-lab-assistant",
                        ChatOptions = chatOptions,
                        ChatHistoryProvider = new SqliteChatHistoryProvider(db, tokens, conversationId, options.Value.HistoryTokenBudget, time),
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

        var sources = state.Sources.DistinctBy(s => (s.DocId, s.SectionPath)).ToList();
        if (sources.Count > 0)
        {
            await events.WriteAsync(new SourcesEvent(sources), ct);
        }

        var text = answer.ToString().Trim();
        var signals = TurnSignals.Compute(intent, state.ToolCalls.Count, state.ZeroResults, text.Length, sources.Count, options.Value.LongAnswerChars);
        await PersistAsync(principal, conversationId, turnId, message, text, intent, forced, state.ToolCalls, sources, signals, ct);

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
        await state.Events.WriteAsync(new ToolCallStartedEvent(callId, name, args), ct);

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
            state.ToolCalls.Add(new ToolCallRecord(name, args, "error", 0, [], []));
            await state.Events.WriteAsync(new ToolCallFinishedEvent(callId, name, "failed", 0, true), ct);
            return ToolDataEnvelope.Wrap(name, "The tool is temporarily unavailable.");
        }

        var (payload, structured, isError) = ToolDataEnvelope.Unpack(result);
        var (summary, sources) = Summarise(name, structured, isError);
        state.Sources.AddRange(sources);
        if (name == "search_documents" && !isError && sources.Count == 0)
        {
            state.ZeroResults = true;
        }

        var outcome = isError ? "error" : "ok";
        await audit.RecordAsync(new AuditEntry(state.Principal, state.ConversationId, state.TurnId, name, args, outcome, sw.ElapsedMilliseconds), ct);
        state.ToolCalls.Add(new ToolCallRecord(name, args, outcome, sources.Count, sources.Select(s => s.DocId).Distinct().ToList(), []));
        await state.Events.WriteAsync(new ToolCallFinishedEvent(callId, name, summary, sources.Count, isError), ct);
        return ToolDataEnvelope.Wrap(name, payload);
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
        await state.Events.WriteAsync(new ToolCallStartedEvent(call.CallId, name, ""), ct);
        await audit.RecordAsync(new AuditEntry(state.Principal, state.ConversationId, state.TurnId, name, "", "unknown_tool", 0), ct);
        state.ToolCalls.Add(new ToolCallRecord(name, "", "unknown_tool", 0, [], []));
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
        IReadOnlyList<ToolCallRecord> toolCalls, IReadOnlyList<SourceRef> sources, IReadOnlyList<string> signals, CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
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
            SourcesJson = JsonSerializer.Serialize(sources.Select(s => new { s.DocId, s.SectionPath }), Json),
            SignalsJson = JsonSerializer.Serialize(signals, Json),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });
        await ctx.SaveChangesAsync(ct);
    }

    private sealed class TurnState(Principal principal, string conversationId, string turnId, ChannelWriter<ChatEvent> events)
    {
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
