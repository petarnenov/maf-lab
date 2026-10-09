using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Sockets;
using Maf.Lab.Domain.Services;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Plugins;

/// <summary>HTTP health for manifest-declared services; no protocol credentials or domain operations.</summary>
public sealed class PluginHealthOptions
{
    public const string Section = "PluginHealth";
    /// <summary>The shared ASP.NET service port used only with an explicitly declared relative health path.
    /// A topology URL declares its own scheme and port; a bare service has no HTTP health target.</summary>
    public int ServicePort { get; set; } = 8080;
    public double ProbeTimeoutSeconds { get; set; } = 2;
    public double CacheSeconds { get; set; } = 15;
}

public sealed class PluginHealth(IHttpClientFactory clients, IServiceResolver resolver, TimeProvider clock,
    IOptionsMonitor<PluginHealthOptions> options)
{
    public const string HttpClientName = "plugin-health";
    internal static readonly HttpRequestOptionsKey<string> ReplicaAddress = new("plugin-health-replica-address");

    /// <summary>Connect to each discovered replica while preserving the URL authority, Host, TLS SNI and certificate checks.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                if (context.InitialRequestMessage.Options.TryGetValue(ReplicaAddress, out var address))
                    await socket.ConnectAsync(IPAddress.Parse(address), context.DnsEndPoint.Port, ct);
                else await socket.ConnectAsync(context.DnsEndPoint, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        },
    };
    // Descriptor identity changes on catalogue refresh/reinstallation. Weak keys cannot retain removed plugins.
    private readonly ConditionalWeakTable<InstalledPlugin, Entry> _cache = new();
    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public long At { get; set; }
        public string? Health { get; set; }
        public Settings? Settings { get; set; }
    }
    private sealed record Settings(int Port, double Timeout, double Ttl);

    public async Task<string> GetAsync(InstalledPlugin plugin, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var o = options.CurrentValue;
        var settings = new Settings(o.ServicePort, o.ProbeTimeoutSeconds, o.CacheSeconds);
        var entry = _cache.GetValue(plugin, _ => new Entry());
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.Timeout, 0.001, 2)));
        try { await entry.Gate.WaitAsync(budget.Token); }
        catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); return "unavailable"; }
        try
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Health is not null && entry.Settings == settings
                && clock.GetElapsedTime(entry.At).TotalSeconds < settings.Ttl) return entry.Health;
            var nodes = plugin.Manifest.Topology is { } topology
                ? (HasTarget(topology) ? new[] { topology } : []).Concat(topology.Nodes).ToArray() : [];
            string result;
            if (nodes.Length == 0) result = plugin.HasServer && plugin.Manifest.Kind is PluginKinds.App or PluginKinds.Provider
                ? "ok" : "unknown";
            else
            {
                var statuses = await Task.WhenAll(nodes.Select(n => ProbeAsync(n, settings.Port, budget.Token)));
                result = statuses.Contains("unavailable") ? "unavailable" : statuses.Contains("unknown") ? "unknown" : "ok";
            }
            ct.ThrowIfCancellationRequested();
            entry.At = clock.GetTimestamp();
            entry.Settings = settings;
            entry.Health = result;
            return result;
        }
        finally { entry.Gate.Release(); }
    }

    private static bool HasTarget(TopologyTable node) => !string.IsNullOrWhiteSpace(node.Url)
        || !string.IsNullOrWhiteSpace(node.Service) || !string.IsNullOrWhiteSpace(node.Health);

    private async Task<string> ProbeAsync(TopologyTable node, int servicePort, CancellationToken ct)
    {
        var target = Target(node, servicePort);
        if (target is null) return "unknown";
        try
        {
            // A URL without service discovery is one generic HTTP health request. Services probe every DNS replica.
            var addresses = string.IsNullOrWhiteSpace(node.Service) ? [] : await resolver.ResolveAsync(node.Service, ct).WaitAsync(ct);
            if (addresses.Count == 0)
            {
                var endpointHealth = await HttpAsync(target, ct);
                // A live fallback endpoint cannot prove the health of undiscovered service replicas.
                return !string.IsNullOrWhiteSpace(node.Service) && endpointHealth == "ok" ? "unknown" : endpointHealth;
            }
            var replicas = await Task.WhenAll(addresses.Distinct().Select(address =>
                HttpAsync(target, ct, address)));
            return replicas.All(s => s == "ok") ? "ok" : "unavailable";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            return "unavailable";
        }
    }

    private static Uri? Target(TopologyTable node, int servicePort)
    {
        if (Uri.TryCreate(node.Url, UriKind.Absolute, out var declared) && declared.Scheme is "http" or "https"
            && string.IsNullOrEmpty(declared.UserInfo))
        {
            if (string.IsNullOrWhiteSpace(node.Health)) return declared;
            return node.Health.StartsWith('/') && !node.Health.StartsWith("//", StringComparison.Ordinal)
                ? new Uri(declared, node.Health) : null;
        }
        // A bare service may be non-HTTP infrastructure; do not invent an endpoint for it.
        if (string.IsNullOrWhiteSpace(node.Url) && !string.IsNullOrWhiteSpace(node.Service)
            && node.Health is { } path && path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal)
            && servicePort is > 0 and <= 65535
            && Uri.TryCreate($"http://{node.Service}:{servicePort}{path}", UriKind.Absolute, out var service)) return service;
        return null;
    }

    private async Task<string> HttpAsync(Uri target, CancellationToken ct, string? replicaAddress = null)
    {
        using var client = clients.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = null;
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        // Every health request owns its connection, including generic URLs sharing an authority with a replica.
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Headers.ConnectionClose = true;
        if (replicaAddress is not null)
        {
            request.Options.Set(ReplicaAddress, replicaAddress);
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return response.IsSuccessStatusCode ? "ok" : "unavailable";
    }
}
