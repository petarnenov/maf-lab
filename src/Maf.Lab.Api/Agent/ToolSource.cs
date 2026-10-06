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
    /// <param name="domains">
    /// The domains whose servers this turn needs (add-codebase-domain); only those are contacted. Null — a confirmation, a
    /// probe, a turn with no domain verdict — contacts every configured server.
    /// </param>
    Task<ToolSet> GetToolsAsync(string bearerToken, ConfirmationSink? confirmations, CancellationToken ct, IReadOnlySet<string>? domains = null);
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

    /// <summary>
    /// The domain that owns a tool: the server's that offered it, else the domain whose descriptor names it, else
    /// <see cref="Domains.None"/> — no domain is where unclaimed tools go (task 4.6).
    /// </summary>
    public string DomainOf(string tool) => origins?.GetValueOrDefault(tool)?.Domain ?? Domains.OfTool(tool) ?? Domains.None;

    /// <summary>The MCP server that owns a tool, by the name it gave itself.</summary>
    public string ServerOf(string tool) => origins?.GetValueOrDefault(tool)?.Server ?? DefaultServer;

    /// <summary>The domains this turn is offered tools of, in the catalogue's order.</summary>
    public IReadOnlyList<string> OfferedDomains =>
        [.. Names.Select(DomainOf).Distinct().OrderBy(DomainCatalogue.Current.Order)];

    public const string DefaultServer = "maf-lab-retrieval";

    public ValueTask DisposeAsync() => owner?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>Where a tool comes from: the domain it belongs to and the MCP server that serves it.</summary>
public sealed record ToolOrigin(string Domain, string Server);

/// <summary>
/// Consumes every domain's MCP server through the MCP client integration, every server alike (task 4.6). The user's
/// bearer token is forwarded to each, so every server derives the tenant itself; the agent host never passes a tenant. A
/// server that fails leaves its domain's tools out of the turn, which runs with what it has; only when every server the
/// turn needs fails does the turn fail. A server whose domain is not in use is not contacted, and a tool whose descriptor
/// requires a plugin that is not in use (fee adjustment without a compliance reviewer, task 4.4) is not offered.
/// </summary>
public sealed class McpToolSource(IOptions<AgentOptions> options, ILoggerFactory loggers, IHttpClientFactory http,
    Plugins.PluginCatalogue? plugins = null, DomainCatalogue? domainCatalogue = null, IConfiguration? configuration = null,
    Tracing.TurnObservers? observers = null) : IToolSource
{
    /// <summary>
    /// Whether a plugin (or capability) a tool requires is in use: installed as a plugin, or — until it becomes one — the
    /// setting that wires it today configured (<see cref="BuiltIn.BuiltInDomains.LegacyCapabilities"/>).
    /// </summary>
    internal static bool InUse(string name, Plugins.PluginCatalogue? plugins, IConfiguration? configuration) =>
        plugins?.Current.Contains(name) == true
        || (BuiltIn.BuiltInDomains.LegacyCapabilities.GetValueOrDefault(name) is { } setting && configuration?[setting] is { Length: > 0 });

    private readonly ILogger _logger = loggers.CreateLogger<McpToolSource>();

    public async Task<ToolSet> GetToolsAsync(string bearerToken, ConfirmationSink? confirmations, CancellationToken ct,
        IReadOnlySet<string>? domains = null)
    {
        var catalogue = domainCatalogue ?? DomainCatalogue.AllBuiltIn;
        var servers = options.Value.AllServers(plugins?.McpServers())
            .Where(s => catalogue.Get(s.Domain) is not null && (domains is null || domains.Contains(s.Domain)))
            .OrderBy(s => catalogue.Order(s.Domain))
            .ToList();
        var connected = await Task.WhenAll(servers.Select(async server =>
        {
            try
            {
                return (server, Client: ((McpClient Client, IList<McpClientTool> Tools)?)await ConnectAsync(server, bearerToken, confirmations, ct),
                    Error: (Exception?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (server, Client: ((McpClient Client, IList<McpClientTool> Tools)?)null, Error: (Exception?)ex);
            }
        }));
        if (connected.Length > 0 && connected.All(c => c.Client is null))
        {
            // Every server this turn needs is down: there is nothing to answer from, so the turn fails as a whole.
            throw connected[0].Error!;
        }

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
                var descriptor = catalogue.Get(server.Domain);
                if (descriptor?.ToolRequires.GetValueOrDefault(tool.Name) is { } required && !InUse(required, plugins, configuration))
                {
                    // Offered only while what it needs is in use: a fee adjustment needs a reviewer to pass (task 4.4).
                    continue;
                }
                if (server.Tools.Count > 0 && !server.Tools.Contains(tool.Name, StringComparer.Ordinal))
                {
                    // Not offered to the agent: the server keeps it for its other clients (ask_codebase writes its own answer).
                    continue;
                }
                if (!origins.TryAdd(tool.Name, new ToolOrigin(server.Domain, serverName)))
                {
                    _logger.LogWarning("tool {Tool} of domain {Domain} dropped: {Owner} already offers it", tool.Name, server.Domain,
                        origins[tool.Name].Domain);
                    continue;
                }
                owners[tool.Name] = c.Client;
                // Ask each server for diagnostics in the result _meta (shown in the monitor, never to the model) — only
                // when an observer wants them: a deployment without the monitor never pays for building them.
                tools.Add(options.Value.TraceRetrieval && (observers ?? Tracing.TurnObservers.None).IsEnabled(Maf.Lab.Domain.Tracing.TraceKinds.Retrieval)
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
    /// <summary>The relevance judge's numbers-only verdict, sent with every judged search whether traced or not.</summary>
    public const string Relevance = "maf-lab/relevance";
    /// <summary>A graph tool's reads (template, rows, truncation, time, outcome), sent when diagnostics were asked for.</summary>
    public const string Graph = "maf-lab/graph";
}
