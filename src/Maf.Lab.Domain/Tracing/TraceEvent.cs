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
    /// <summary>Where Jev placed the question among the domains, and whether it crosses the boundary between them.</summary>
    public const string Domain = "domain";
    /// <summary>A tool call entered a different domain from the call before it.</summary>
    public const string Boundary = "boundary";
    public const string History = "history";
    public const string Prompt = "prompt";
    public const string ModelRequest = "model.request";
    public const string ModelResponse = "model.response";
    public const string ToolForced = "tool.forced";
    public const string ToolCall = "tool.call";
    public const string ToolResult = "tool.result";
    public const string Retrieval = "retrieval";
    /// <summary>Jev's relevance judgment of one search: the gate's verdict, the highest probability, the judge's latency.</summary>
    public const string Relevance = "relevance";
    public const string Envelope = "envelope";
    public const string AnswerDelta = "answer.delta";
    /// <summary>A chunk of what the model reasoned before it answered, recorded the way the answer is.</summary>
    public const string ReasoningDelta = "reasoning.delta";
    public const string ToolUnknown = "tool.unknown";
    public const string Audit = "audit";
    /// <summary>A step of a write: proposed, reviewed, confirmed, rejected or applied.</summary>
    public const string Adjustment = "adjustment";
    /// <summary>A content-guard screening: of the prompt, a tool result or another agent's words.</summary>
    public const string Guardrail = "guardrail";
    /// <summary>Jev's check of the final answer: relevant to the question, grounded in what the model read, or unchecked.</summary>
    public const string AnswerCheck = "answer.check";
    public const string Sources = "sources";

    /// <summary>A data card a tool result became (add-activity-cards): its type and its numbers, never free text.</summary>
    public const string Card = "card";

    /// <summary>The account in focus (add-focus-state): where the turn's came from, and any move a read made.</summary>
    public const string Focus = "focus";
    public const string Signals = "signals";
    public const string Memory = "memory";
    public const string TurnEnd = "turn.end";
}

/// <summary>
/// Stored trace of one turn (GET /api/turns/{turnId}/trace), with the AG-UI frames of the run that produced it.
/// <c>AguiFrames</c> is null for a turn whose frames were never recorded, which is not the same as a run with none.
/// </summary>
public sealed record TurnTraceDocument(string TurnId, string ConversationId, DateTimeOffset CreatedAt,
    IReadOnlyList<TraceEvent> Events, IReadOnlyList<RunFrame>? AguiFrames = null);
