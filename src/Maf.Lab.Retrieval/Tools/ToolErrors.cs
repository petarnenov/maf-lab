using Grpc.Core;
using ModelContextProtocol.Protocol;

namespace Maf.Lab.Retrieval.Tools;

/// <summary>Model-facing error text: short, actionable, and free of stack traces, hostnames and query internals.</summary>
public static class ToolErrors
{
    public static CallToolResult Error(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }],
    };

    public static string ForException(Exception ex, string capability) => ex switch
    {
        RpcException or HttpRequestException or TimeoutException or TaskCanceledException =>
            $"{capability} is temporarily unavailable; try again shortly.",
        UnauthorizedAccessException => "The request is not authorized.",
        _ => $"{capability} failed; try rephrasing the request.",
    };
}
