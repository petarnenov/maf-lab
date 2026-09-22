using System.Text;
using System.Text.Json.Nodes;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>
/// Records a stream of model text in the trace as coalesced <c>{ offset, text }</c> events, so the chat can be rewound
/// to any step. A chunk is flushed at <see cref="MaxChars"/>, after <see cref="MaxWaitMs"/>, before a tool call and at
/// the end of the turn. Concatenating the chunks yields the stream exactly.
///
/// One instance per stream: the answer writes <c>answer.delta</c> and the model's reasoning <c>reasoning.delta</c>,
/// under the same rules and each with its own offsets. Flushing them at the same points keeps the trace's order true
/// to the turn — reasoning that arrived before a tool call is recorded before it.
/// </summary>
public sealed class AnswerChunker(TurnTrace trace, string kind = TraceKinds.AnswerDelta, string label = "Answer")
{
    public const int MaxChars = 160;
    public const long MaxWaitMs = 150;

    private readonly StringBuilder _pending = new();
    private int _offset;
    private long _lastFlushMs = trace.ElapsedMs;

    public void Append(string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }
        _pending.Append(delta);
        if (_pending.Length >= MaxChars || trace.ElapsedMs - _lastFlushMs >= MaxWaitMs)
        {
            Flush();
        }
    }

    public void Flush()
    {
        _lastFlushMs = trace.ElapsedMs;
        if (_pending.Length == 0)
        {
            return;
        }
        var text = _pending.ToString();
        _pending.Clear();
        trace.Add(kind, $"{label} +{text.Length} chars", new JsonObject { ["offset"] = _offset, ["text"] = text });
        _offset += text.Length;
    }
}
