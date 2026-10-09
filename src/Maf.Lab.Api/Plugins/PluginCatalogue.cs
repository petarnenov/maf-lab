using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Api.Agent;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Maf.Lab.Api.Plugins;

public sealed class PluginOptions
{
    public const string Section = "Plugins";

    /// <summary>The plugins folder, mounted read-only; `.installed` in it is the set make resolved.</summary>
    public string Root { get; set; } = "plugins";

    /// <summary>How often a replica re-reads the set when no `plugins-changed` message reached it.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>One installed plugin as the core sees it: its manifest, its MCP server description, and whether code of it runs here.</summary>
public sealed record InstalledPlugin(PluginManifest Manifest, JsonObject? ServerJson, bool HasServer)
{
    public JsonObject? AgentCard { get; init; }
    public string Name => Manifest.Name;

    /// <summary>The MCP endpoint its server.json advertises (the first remote), when it has one.</summary>
    public string? McpEndpoint => ServerJson?["remotes"] is JsonArray { Count: > 0 } remotes ? remotes[0]?["url"]?.GetValue<string>() : null;
}

/// <summary>The set as last read: the plugins, the environment it was resolved for, and the problems of the last read.</summary>
public sealed record PluginSet(IReadOnlyList<InstalledPlugin> Plugins, string? Environment, IReadOnlyList<string> Problems)
{
    public static readonly PluginSet Empty = new([], null, []);

    public bool Contains(string name) => Plugins.Any(p => p.Name == name);
}

/// <summary>
/// The installed plugins, read at run time from `plugins/.installed` (introduce-plugins decision 2). make writes the file
/// by rename and then publishes `plugins-changed` on the shared store; every replica re-reads on that message and every
/// <see cref="PluginOptions.RefreshInterval"/> anyway, so a missed message is bounded. No file watcher: one over a Docker
/// Desktop bind mount is unreliable. A file that does not parse keeps the last good set; the problem is reported by
/// `/api/plugins` and logged with the file's name, never its content.
/// </summary>
public sealed class PluginCatalogue : IHostedService, IDisposable
{
    public const string Channel = "plugins-changed";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PluginOptions _options;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private PluginSet _current = PluginSet.Empty;
    private Timer? _timer;

    public PluginCatalogue(IOptions<PluginOptions> options, ILogger<PluginCatalogue> logger, IConnectionMultiplexer? redis = null)
    {
        _options = options.Value;
        _redis = redis;
        _logger = logger;
        Refresh();
    }

    /// <summary>The installed set as last read.</summary>
    public PluginSet Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public string InstalledPath => Path.Combine(_options.Root, ".installed");

    /// <summary>Re-reads `.installed`. A missing file is the empty set; a bad one keeps the last good set and reports it.</summary>
    public PluginSet Refresh()
    {
        PluginSet next;
        try
        {
            next = File.Exists(InstalledPath) ? Parse(File.ReadAllText(InstalledPath)) : PluginSet.Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning("plugins: {File} could not be read ({ErrorType}); keeping the last good set", ".installed", ex.GetType().Name);
            lock (_gate)
            {
                _current = _current with { Problems = [$".installed could not be read ({ex.GetType().Name}); the last good set is kept"] };
                return _current;
            }
        }
        lock (_gate)
        {
            _current = next;
            return next;
        }
    }

    /// <summary>
    /// Reads the document make writes. A plugin whose manifest is malformed is reported and left out; every other plugin
    /// still loads, so one bad manifest never takes the others down.
    /// </summary>
    public static PluginSet Parse(string text)
    {
        var root = JsonNode.Parse(text) as JsonObject ?? throw new FormatException("not an object");
        var plugins = new List<InstalledPlugin>();
        var problems = new List<string>();
        foreach (var entry in root["plugins"] as JsonArray ?? [])
        {
            var manifest = entry?["manifest"]?.Deserialize<PluginManifest>(Json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Kind))
            {
                problems.Add($"invalid manifest: {entry?["manifest"]?["name"]?.ToString() ?? "(no name)"}");
                continue;
            }
            plugins.Add(new InstalledPlugin(manifest, entry!["serverJson"] as JsonObject, entry["hasServer"]?.GetValue<bool>() ?? false)
            {
                AgentCard = entry["agentCard"] as JsonObject,
            });
        }
        return new PluginSet(plugins, root["env"]?.GetValue<string>(), problems);
    }

    /// <summary>Each installed MCP plugin's server, keyed by plugin name, as the agent connects to it (decision 3).</summary>
    public IReadOnlyDictionary<string, McpServerOptions> McpServers() =>
        Current.Plugins
            .Where(p => p.Manifest.Kind == PluginKinds.Mcp && p.Manifest.Domain is not null && p.McpEndpoint is not null)
            .ToDictionary(p => p.Name, p => new McpServerOptions
            {
                Plugin = p.Name,
                Domain = p.Manifest.Domain!.Id,
                Endpoint = p.McpEndpoint!,
                Tools = [.. p.Manifest.Domain.Tools],
            }, StringComparer.Ordinal);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_redis is not null)
        {
            _redis.GetSubscriber().Subscribe(RedisChannel.Literal(Channel), (_, _) => Refresh());
        }
        _timer = new Timer(_ => Refresh(), null, _options.RefreshInterval, _options.RefreshInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}
