using ModelContextProtocol.Client;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent;

/// <summary>Supplies the agent's tools for one turn, authenticated as the calling user.</summary>
public interface IToolSource
{
    Task<ToolSet> GetToolsAsync(string bearerToken, CancellationToken ct);
}

public sealed class ToolSet(IReadOnlyList<AITool> tools, IAsyncDisposable? owner) : IAsyncDisposable
{
    public IReadOnlyList<AITool> Tools { get; } = tools;
    public IReadOnlySet<string> Names { get; } = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
    public ValueTask DisposeAsync() => owner?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Consumes the retrieval MCP server through the MCP client integration. The user's bearer token is forwarded,
/// so the server derives the tenant itself; the agent host never passes a tenant.
/// </summary>
public sealed class McpToolSource(IOptions<AgentOptions> options, ILoggerFactory loggers, IHttpClientFactory http) : IToolSource
{
    public async Task<ToolSet> GetToolsAsync(string bearerToken, CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(options.Value.McpEndpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" },
            Name = "maf-lab-retrieval",
        }, http.CreateClient("mcp"), loggers, ownsHttpClient: true);
        var client = await McpClient.CreateAsync(transport, loggerFactory: loggers, cancellationToken: ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        // Ask the server for retrieval diagnostics in the result _meta (shown in the monitor, never to the model).
        var traced = options.Value.TraceRetrieval
            ? tools.Select(t => t.WithMeta(new System.Text.Json.Nodes.JsonObject { [TraceMeta.Flag] = true })).Cast<AITool>().ToList()
            : tools.Cast<AITool>().ToList();
        return new ToolSet(traced, client);
    }
}

/// <summary>_meta keys shared with the MCP server.</summary>
public static class TraceMeta
{
    public const string Flag = "maf-lab/trace";
    public const string Diagnostics = "maf-lab/trace";
    public const string Instance = "maf-lab/instance";
}
