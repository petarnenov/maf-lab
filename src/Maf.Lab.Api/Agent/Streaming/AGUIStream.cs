using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AGUI.Abstractions;
using Maf.Lab.Domain.Chat;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Api.Agent.Streaming;

/// <summary>
/// How this system's events are written on the wire, and the two names it adds to the protocol.
/// </summary>
public static class AGUIStream
{
    /// <summary>
    /// The protocol's own types are source-generated; ours are not. Combining the resolvers is what lets one
    /// options instance serialize both — without it every event fails with a missing-metadata error.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(
            AGUIJsonUtilities.DefaultTypeInfoResolver,
            new DefaultJsonTypeInfoResolver()),
    };

    /// <summary>The sources of an answer. The protocol has no word for them, so they travel as an extension.</summary>
    public const string SourcesEvent = "maf-lab/sources";

    /// <summary>One behind-the-scenes trace event of a turn.</summary>
    public const string TraceEvent = "maf-lab/trace";

    public static CustomEvent Sources(IReadOnlyList<SourceRef> sources) => new()
    {
        Name = SourcesEvent,
        Value = JsonSerializer.SerializeToElement(new { sources }, Json),
    };

    public static CustomEvent Trace(TraceEvent traceEvent) => new()
    {
        Name = TraceEvent,
        Value = JsonSerializer.SerializeToElement(traceEvent, Json),
    };

    /// <summary>The SSE frame name: the protocol's own discriminator, so the frame says what the payload is.</summary>
    public static string FrameName(BaseEvent e) => e.Type;

    public static string Serialize(BaseEvent e) => JsonSerializer.Serialize(e, e.GetType(), Json);
}
