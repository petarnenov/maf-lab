using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AGUI.Abstractions;

namespace Maf.Lab.Api.Agent.Streaming;

/// <summary>
/// How the test-generation run's events are written on the wire, until that run is an agent too (agui-protocol-only).
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

    /// <summary>The SSE frame name: the protocol's own discriminator, so the frame says what the payload is.</summary>
    public static string FrameName(BaseEvent e) => e.Type;

    public static string Serialize(BaseEvent e) => JsonSerializer.Serialize(e, e.GetType(), Json);
}
