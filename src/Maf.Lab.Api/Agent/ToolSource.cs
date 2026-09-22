using ModelContextProtocol.Client;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent;

/// <summary>Supplies the agent's tools for one turn, authenticated as the calling user.</summary>
public interface IToolSource
{
    /// <summary>
    /// The tools for one turn. <paramref name="confirmations"/> catches a server's request for a person's
    /// approval; without one, such a request would be answered by the client on the person's behalf.
    /// </summary>
    Task<ToolSet> GetToolsAsync(string bearerToken, ConfirmationSink? confirmations, CancellationToken ct);
}

/// <summary>
/// Calls a tool outside the agent's loop, carrying a person's answer to a question the server asked: the
/// state says what was proposed, and the answer says whether to do it.
/// </summary>
/// <param name="idempotencyKey">
/// The caller's own, so a confirmation sent again after a stream was interrupted is answered rather than applied
/// twice. Null when the caller did not give one.
/// </param>
public delegate Task<ModelContextProtocol.Protocol.CallToolResult> ConfirmedCall(
    string tool, IReadOnlyDictionary<string, object?> arguments, string state, bool approve,
    string? idempotencyKey, CancellationToken ct);

public sealed class ToolSet(IReadOnlyList<AITool> tools, IAsyncDisposable? owner, ConfirmedCall? confirm = null) : IAsyncDisposable
{
    public IReadOnlyList<AITool> Tools { get; } = tools;
    public IReadOnlySet<string> Names { get; } = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Answers a pending question. Null when this source cannot carry an answer back.</summary>
    public ConfirmedCall? Confirm { get; } = confirm;

    public ValueTask DisposeAsync() => owner?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Consumes the retrieval MCP server through the MCP client integration. The user's bearer token is forwarded,
/// so the server derives the tenant itself; the agent host never passes a tenant.
/// </summary>
public sealed class McpToolSource(IOptions<AgentOptions> options, ILoggerFactory loggers, IHttpClientFactory http) : IToolSource
{
    public async Task<ToolSet> GetToolsAsync(string bearerToken, ConfirmationSink? confirmations, CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(options.Value.McpEndpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" },
            Name = "maf-lab-retrieval",
        }, http.CreateClient("mcp"), loggers, ownsHttpClient: true);
        var clientOptions = new McpClientOptions
        {
            // Declared so the server may ask; answered by taking the question down, never by deciding.
            Capabilities = new ModelContextProtocol.Protocol.ClientCapabilities
            {
                Elicitation = new ModelContextProtocol.Protocol.ElicitationCapability(),
            },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, _) =>
                    ValueTask.FromResult(confirmations?.Capture(request) ?? new ModelContextProtocol.Protocol.ElicitResult { Action = "cancel" }),
            },
        };
        var client = await McpClient.CreateAsync(transport, clientOptions, loggerFactory: loggers, cancellationToken: ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        // Ask the server for retrieval diagnostics in the result _meta (shown in the monitor, never to the model).
        var traced = options.Value.TraceRetrieval
            ? tools.Select(t => t.WithMeta(new System.Text.Json.Nodes.JsonObject { [TraceMeta.Flag] = true })).Cast<AITool>().ToList()
            : tools.Cast<AITool>().ToList();
        return new ToolSet(traced, client, (tool, arguments, state, approve, idempotencyKey, token) =>
            client.CallToolAsync(new ModelContextProtocol.Protocol.CallToolRequestParams
            {
                Name = tool,
                Arguments = arguments.ToDictionary(a => a.Key, a => System.Text.Json.JsonSerializer.SerializeToElement(a.Value)),
                RequestState = state,
                // The caller's key rides in the request's metadata: a tool argument would be a thing the model
                // could invent, and this belongs to whoever is retrying.
                Meta = idempotencyKey is { Length: > 0 }
                    ? new System.Text.Json.Nodes.JsonObject
                    {
                        [Maf.Lab.Domain.Billing.FeeAdjustmentTool.IdempotencyMetaKey] = idempotencyKey,
                    }
                    : null,
                InputResponses = new Dictionary<string, ModelContextProtocol.Protocol.InputResponse>
                {
                    [Maf.Lab.Domain.Billing.FeeAdjustmentTool.ConfirmationKey] =
                        ModelContextProtocol.Protocol.InputResponse.FromElicitResult(new ModelContextProtocol.Protocol.ElicitResult
                        {
                            Action = approve ? "accept" : "decline",
                            Content = approve
                                ? new Dictionary<string, System.Text.Json.JsonElement>
                                {
                                    ["approve"] = System.Text.Json.JsonSerializer.SerializeToElement(true),
                                }
                                : null,
                        }),
                },
            }, token).AsTask());
    }
}

/// <summary>_meta keys shared with the MCP server.</summary>
public static class TraceMeta
{
    public const string Flag = "maf-lab/trace";
    public const string Diagnostics = "maf-lab/trace";
    public const string Instance = "maf-lab/instance";
}
