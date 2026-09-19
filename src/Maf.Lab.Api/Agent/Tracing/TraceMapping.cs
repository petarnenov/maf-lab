using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>Maps Microsoft.Extensions.AI objects to the trace's JSON shapes (docs/trace-events.md).</summary>
public static class TraceMapping
{
    public static JsonArray Messages(IEnumerable<ChatMessage> messages) =>
        new(messages.Select(m => (JsonNode)new JsonObject
        {
            ["role"] = m.Role.Value,
            ["contents"] = new JsonArray(m.Contents.Select(Content).Where(c => c is not null).ToArray()),
        }).ToArray());

    public static JsonNode? Content(AIContent content) => content switch
    {
        TextContent t => new JsonObject { ["type"] = "text", ["text"] = t.Text },
        FunctionCallContent c => new JsonObject { ["type"] = "functionCall", ["callId"] = c.CallId, ["name"] = c.Name, ["arguments"] = Node(c.Arguments) },
        FunctionResultContent r => new JsonObject { ["type"] = "functionResult", ["callId"] = r.CallId, ["result"] = Node(r.Result) },
        UsageContent => null,
        _ => new JsonObject { ["type"] = content.GetType().Name },
    };

    public static JsonNode? Node(object? value) => value switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        JsonElement e => JsonNode.Parse(e.GetRawText()),
        string s => JsonValue.Create(s),
        _ => JsonSerializer.SerializeToNode(value, TurnTrace.Json),
    };

    public static string ToolMode(ChatToolMode? mode) => mode switch
    {
        RequiredChatToolMode { RequiredFunctionName: { } name } => $"required:{name}",
        RequiredChatToolMode => "required:any",
        NoneChatToolMode => "none",
        _ => "auto",
    };
}
