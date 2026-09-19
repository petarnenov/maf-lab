using System.Text;
using System.Text.Json.Nodes;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>
/// Records the streamed answer in the trace as coalesced <c>answer.delta { offset, text }</c> events, so the chat can
/// be rewound to any step. A chunk is flushed at <see cref="MaxChars"/>, after <see cref="MaxWaitMs"/>, before a tool
/// call and at the end of the turn. Concatenating the chunks yields the answer exactly.
/// </summary>
public sealed class AnswerChunker(TurnTrace trace)
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
        trace.Add(TraceKinds.AnswerDelta, $"Answer +{text.Length} chars", new JsonObject { ["offset"] = _offset, ["text"] = text });
        _offset += text.Length;
    }
}
