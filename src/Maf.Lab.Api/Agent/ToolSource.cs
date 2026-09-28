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

public sealed class ToolSet(IReadOnlyList<AITool> tools, IAsyncDisposable? owner, ConfirmedCall? confirm = null,
    IReadOnlyDictionary<string, ToolOrigin>? origins = null, IReadOnlyList<string>? unavailable = null) : IAsyncDisposable
{
    public IReadOnlyList<AITool> Tools { get; } = tools;
    public IReadOnlySet<string> Names { get; } = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Answers a pending question. Null when this source cannot carry an answer back.</summary>
    public ConfirmedCall? Confirm { get; } = confirm;

    /// <summary>Domains whose server could not be reached this turn: their tools are simply not offered.</summary>
    public IReadOnlyList<string> Unavailable { get; } = unavailable ?? [];

    /// <summary>The domain that owns a tool. A source that names none serves the billing domain, as the lab always did.</summary>
    public string DomainOf(string tool) => origins?.GetValueOrDefault(tool)?.Domain ?? Domains.Billing;

    /// <summary>The MCP server that owns a tool, by the name it gave itself.</summary>
    public string ServerOf(string tool) => origins?.GetValueOrDefault(tool)?.Server ?? DefaultServer;

    /// <summary>The domains this turn is offered tools of, in the catalogue's order.</summary>
    public IReadOnlyList<string> OfferedDomains =>
        [.. Names.Select(DomainOf).Distinct().OrderBy(d => Domains.All.ToList().IndexOf(d) is var i && i < 0 ? int.MaxValue : i)];

    public const string DefaultServer = "maf-lab-retrieval";

    public ValueTask DisposeAsync() => owner?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>Where a tool comes from: the domain it belongs to and the MCP server that serves it.</summary>
public sealed record ToolOrigin(string Domain, string Server);

/// <summary>
/// Consumes every domain's MCP server through the MCP client integration: billing first, then each configured domain.
/// The user's bearer token is forwarded to each, so every server derives the tenant itself; the agent host never
/// passes a tenant. The billing server failing fails the turn as it always did; another domain's server failing leaves
/// its tools out of the turn, which then runs with what it has.
/// </summary>
public sealed class McpToolSource(IOptions<AgentOptions> options, ILoggerFactory loggers, IHttpClientFactory http) : IToolSource
{
    private readonly ILogger _logger = loggers.CreateLogger<McpToolSource>();

    public async Task<ToolSet> GetToolsAsync(string bearerToken, ConfirmationSink? confirmations, CancellationToken ct)
    {
        var servers = options.Value.AllServers();
        var connected = await Task.WhenAll(servers.Select(async (server, index) =>
        {
            try
            {
                return (server, Client: await ConnectAsync(server, bearerToken, confirmations, ct), Error: (Exception?)null);
            }
            catch (Exception ex) when (index > 0 && ex is not OperationCanceledException)
            {
                return (server, Client: ((McpClient Client, IList<McpClientTool> Tools)?)null, Error: ex);
            }
        }));

        var tools = new List<AITool>();
        var origins = new Dictionary<string, ToolOrigin>(StringComparer.Ordinal);
        var owners = new Dictionary<string, McpClient>(StringComparer.Ordinal);
        var unavailable = new List<string>();
        foreach (var (server, connection, error) in connected)
        {
            if (connection is not { } c)
            {
                // The domain's name and the kind of failure only: an address is not model- or log-worthy detail.
                _logger.LogWarning("MCP server of domain {Domain} unavailable: {ErrorType}", server.Domain, error?.GetType().Name);
                unavailable.Add(server.Domain);
                continue;
            }
            var serverName = c.Client.ServerInfo?.Name is { Length: > 0 } n ? n : server.Domain;
            foreach (var tool in c.Tools)
            {
                if (!origins.TryAdd(tool.Name, new ToolOrigin(server.Domain, serverName)))
                {
                    _logger.LogWarning("tool {Tool} of domain {Domain} dropped: {Owner} already offers it", tool.Name, server.Domain,
                        origins[tool.Name].Domain);
                    continue;
                }
                owners[tool.Name] = c.Client;
                // Ask each server for diagnostics in the result _meta (shown in the monitor, never to the model).
                tools.Add(options.Value.TraceRetrieval
                    ? tool.WithMeta(new System.Text.Json.Nodes.JsonObject { [TraceMeta.Flag] = true })
                    : tool);
            }
        }

        var clients = connected.Select(x => x.Client?.Client).OfType<McpClient>().ToList();
        return new ToolSet(tools, new Owners(clients), (tool, arguments, state, approve, idempotencyKey, token) =>
            // An answer goes back to the server that asked the question: the one that owns the tool.
            (owners.GetValueOrDefault(tool) ?? clients[0]).CallToolAsync(new ModelContextProtocol.Protocol.CallToolRequestParams
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
            }, token).AsTask(),
            origins, unavailable);
    }

    private async Task<(McpClient Client, IList<McpClientTool> Tools)> ConnectAsync(
        McpServerOptions server, string bearerToken, ConfirmationSink? confirmations, CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(server.Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {bearerToken}" },
            Name = $"maf-lab-{server.Domain}",
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
        try
        {
            return (client, await client.ListToolsAsync(cancellationToken: ct));
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    /// <summary>Every client of the turn, disposed together when the turn ends.</summary>
    private sealed class Owners(IReadOnlyList<McpClient> clients) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }
}

/// <summary>_meta keys shared with the MCP server.</summary>
public static class TraceMeta
{
    public const string Flag = "maf-lab/trace";
    public const string Diagnostics = "maf-lab/trace";
    public const string Instance = "maf-lab/instance";
}
