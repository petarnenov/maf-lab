using System.Text.Json;
using System.Text.Json.Serialization;
using Maf.Lab.Domain.Chat;

namespace Maf.Lab.Domain.Tracing;

/// <summary>
/// One step of a chat turn as seen "behind the scenes". Shapes of <see cref="Data"/> per kind are documented in
/// docs/trace-events.md. Streamed live as the SSE event "trace" and stored with the turn.
/// </summary>
public sealed record TraceEvent(int Seq, long AtMs, string Kind, string Title, long? DurationMs, JsonElement Data, bool Truncated);

public static class TraceKinds
{
    public const string TurnStart = "turn.start";
    public const string Intent = "intent";
    public const string History = "history";
    public const string Prompt = "prompt";
    public const string ModelRequest = "model.request";
    public const string ModelResponse = "model.response";
    public const string ToolForced = "tool.forced";
    public const string ToolCall = "tool.call";
    public const string ToolResult = "tool.result";
    public const string Retrieval = "retrieval";
    public const string Envelope = "envelope";
    public const string AnswerDelta = "answer.delta";
    public const string ToolUnknown = "tool.unknown";
    public const string Audit = "audit";
    public const string Sources = "sources";
    public const string Signals = "signals";
    public const string Memory = "memory";
    public const string TurnEnd = "turn.end";
}

/// <summary>SSE wrapper so the trace travels in the same channel as the other chat events.</summary>
public sealed record TraceChatEvent(TraceEvent Event) : ChatEvent
{
    [JsonIgnore]
    public override string EventName => ChatEventNames.Trace;
}

/// <summary>Stored trace of one turn (GET /api/turns/{turnId}/trace).</summary>
public sealed record TurnTraceDocument(string TurnId, string ConversationId, DateTimeOffset CreatedAt, IReadOnlyList<TraceEvent> Events);
