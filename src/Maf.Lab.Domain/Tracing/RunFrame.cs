using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maf.Lab.Domain.Tracing;

/// <summary>
/// One AG-UI event as it crossed the wire, kept so a turn can be read from the protocol's side as well as from its
/// trace. <see cref="Seq"/> counts the frames of a run from 1 and <see cref="AtMs"/> is measured from its first
/// frame. A frame carrying a trace event keeps that event's <see cref="TraceSeq"/> and no <see cref="Payload"/>:
/// the event itself is already in the trace, and a second copy would double what the turn holds.
/// <see cref="Truncated"/> marks a frame the run's size cap left without its payload.
/// </summary>
public sealed record RunFrame(
    int Seq,
    long AtMs,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    int Bytes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TraceSeq,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Payload,
    bool Truncated);
