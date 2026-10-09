using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.Retrieval.Tools;

/// <summary>Official MCP result framing and the existing opt-in trace flag, shared by protocol hosts.</summary>
public static class ToolResults
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static bool TraceRequested(RequestContext<CallToolRequestParams>? context) =>
        context?.Params?.Meta is { } meta && meta.TryGetPropertyValue("maf-lab/trace", out var flag) && flag is not null
        && flag.GetValueKind() == JsonValueKind.True;

    public static CallToolResult Structured<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, Json);
        return new CallToolResult { StructuredContent = element, Content = [new TextContentBlock { Text = element.GetRawText() }] };
    }
}
