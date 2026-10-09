using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Domain.Services;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Maf.Lab.Tests;

public sealed class PluginHealthTests
{
    [Fact]
    public async Task Malformed_service_discovery_does_not_fault_the_health_reader()
    {
        var resolver = new Maf.Lab.Hosting.Services.DnsServiceResolver();
        Assert.Empty(await resolver.ResolveAsync(new string('a', 256), TestContext.Current.CancellationToken));
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static InstalledPlugin Plugin(TopologyTable? topology = null, bool server = true) => new(new PluginManifest
    {
        Name = "health-fixture", Kind = "mcp", Scope = "tenant", Description = "health fixture", Environments = ["dev"],
        Topology = topology,
    }, null, server);

    [Fact]
    public async Task A_module_does_not_hide_failed_remote_replicas_and_requests_have_no_bearer()
    {
        var handler = new HealthHandler((uri, _) => Task.FromResult(uri.Host == "10.0.0.2" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        var health = Service(handler, new HealthResolver(["10.0.0.1", "10.0.0.2"]));
        Assert.Equal("unavailable", await health.GetAsync(Plugin(new TopologyTable { Service = "remote", Health = "/ready" }), Ct));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => { Assert.Equal(8080, uri.Port); Assert.Equal("/ready", uri.AbsolutePath); });
        Assert.False(handler.SawAuthorization);
    }

    [Fact]
    public async Task Every_declared_node_is_aggregated_with_its_explicit_port_and_health_path()
    {
        var handler = new HealthHandler((uri, _) => Task.FromResult(uri.Port == 9090 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        var health = Service(handler, new HealthResolver(["10.0.0.1"]));
        var topology = new TopologyTable { Service = "collector", Url = "http://collector:8889/metrics", Nodes =
            [new TopologyTable { Service = "metrics", Url = "http://metrics:9090/ui", Health = "/-/healthy" }] };
        Assert.Equal("unavailable", await health.GetAsync(Plugin(topology), Ct));
        Assert.Contains(handler.Requests, u => u.Port == 8889 && u.PathAndQuery == "/metrics");
        Assert.Contains(handler.Requests, u => u.Port == 9090 && u.PathAndQuery == "/-/healthy");
    }

    [Fact]
    public async Task Generic_URL_is_one_request_and_redirect_response_is_unavailable()
    {
        var handler = new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.Redirect));
        var resolver = new HealthResolver([]);
        Assert.Equal("unavailable", await Service(handler, resolver).GetAsync(Plugin(new TopologyTable { Url = "http://remote:1234/ready" }), Ct));
        Assert.Single(handler.Requests);
        Assert.Equal(0, resolver.Calls);
        Assert.True(handler.CorrectReplicaTransport);
    }

    [Fact]
    public async Task A_live_fallback_URL_cannot_claim_that_undiscovered_service_replicas_are_healthy()
    {
        var handler = new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.OK));
        var resolver = new HealthResolver([]);
        Assert.Equal("unknown", await Service(handler, resolver).GetAsync(Plugin(new TopologyTable
        {
            Service = "removed-service", Url = "https://gateway/ready",
        }), Ct));
        Assert.Single(handler.Requests);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public void Production_transport_disables_redirects_cookies_and_proxies_and_has_replica_routing()
    {
        using var handler = PluginHealth.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    public async Task Replica_routing_preserves_declared_authority_for_HTTP_Host_and_TLS_SNI(string scheme)
    {
        var handler = new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.OK));
        var target = $"{scheme}://remote.example:8443/ready";
        Assert.Equal("ok", await Service(handler, new HealthResolver(["10.0.0.1", "10.0.0.2"]))
            .GetAsync(Plugin(new TopologyTable { Service = "replicas", Url = target }), Ct));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.Equal(target, uri.AbsoluteUri));
        Assert.Equal(["10.0.0.1", "10.0.0.2"], handler.ReplicaAddresses.Order().ToArray());
        Assert.True(handler.CorrectReplicaTransport);
    }

    [Theory]
    [InlineData("qdrant")]
    [InlineData("neo4j")]
    [InlineData("unspecified-service")]
    public async Task A_bare_service_is_unknown_without_an_invented_HTTP_target(string service)
    {
        var handler = new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.OK));
        Assert.Equal("unknown", await Service(handler).GetAsync(Plugin(new TopologyTable { Service = service }), Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unknown_nodes_prevent_an_OK_aggregate_and_only_local_modules_need_no_probe()
    {
        var health = Service(new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.OK)));
        var local = Plugin();
        Assert.Equal("ok", await health.GetAsync(local with { Manifest = local.Manifest with { Kind = PluginKinds.App } }, Ct));
        Assert.Equal("ok", await health.GetAsync(local with { Manifest = local.Manifest with { Kind = PluginKinds.Provider } }, Ct));
        Assert.Equal("unknown", await health.GetAsync(local, Ct));
        Assert.Equal("unknown", await health.GetAsync(local with { Manifest = local.Manifest with { Kind = PluginKinds.A2A } }, Ct));
        Assert.Equal("unknown", await health.GetAsync(Plugin(server: false), Ct));
        Assert.Equal("unknown", await health.GetAsync(Plugin(new TopologyTable { Nodes =
            [new TopologyTable { Url = "http://remote/ready" }, new TopologyTable { Service = "store" }] }), Ct));
    }

    [Fact]
    public async Task Cache_expires_at_fifteen_seconds_and_separates_descriptors_and_configuration()
    {
        var clock = new FakeTimeProvider();
        var options = new PluginHealthOptions();
        var handler = new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.OK));
        var health = Service(handler, clock: clock, options: options);
        var plugin = Plugin(new TopologyTable { Service = "remote", Health = "/health" });
        Assert.Equal("ok", await health.GetAsync(plugin, Ct));
        clock.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal("ok", await health.GetAsync(plugin, Ct));
        Assert.Single(handler.Requests);
        clock.Advance(TimeSpan.FromSeconds(1));
        await health.GetAsync(plugin, Ct);
        Assert.Equal(2, handler.Requests.Count);
        await health.GetAsync(plugin with { Manifest = plugin.Manifest with { Topology = new TopologyTable { Url = "http://changed/ready" } } }, Ct);
        Assert.Equal(3, handler.Requests.Count);
        options.ServicePort = 8090;
        await health.GetAsync(plugin, Ct);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains(handler.Requests, uri => uri.Port == 8090);
        await health.GetAsync(plugin with { }, Ct); // a new installation descriptor with identical data still gets a fresh probe
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_the_next_read_probes_instead_of_reusing_a_failure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = true;
        var handler = new HealthHandler(async (_, ct) =>
        {
            if (block) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            return HttpStatusCode.OK;
        });
        var health = Service(handler);
        var plugin = Plugin(new TopologyTable { Url = "http://remote/health" });
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var probe = health.GetAsync(plugin, cancelled.Token);
        await entered.Task.WaitAsync(Ct);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
        block = false;
        Assert.Equal("ok", await health.GetAsync(plugin, Ct));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Cancellation_during_DNS_is_not_cached_and_a_fresh_read_can_recover()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = true;
        var resolver = new HealthResolver(["10.0.0.1"], async ct =>
        {
            if (block) { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
        });
        var handler = new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.OK));
        var health = Service(handler, resolver);
        var plugin = Plugin(new TopologyTable { Service = "remote", Health = "/health" });
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var probe = health.GetAsync(plugin, cancelled.Token);
        await entered.Task.WaitAsync(Ct);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
        Assert.Empty(handler.Requests);
        block = false;
        Assert.Equal("ok", await health.GetAsync(plugin, Ct));
        Assert.Equal(2, resolver.Calls);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DNS_and_parallel_nodes_share_a_single_total_timeout()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new HealthHandler(async (_, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return HttpStatusCode.OK; });
        var health = Service(handler, options: new PluginHealthOptions { ProbeTimeoutSeconds = 0.1 });
        var plugin = Plugin(new TopologyTable { Url = "http://one/health", Nodes = [new TopologyTable { Url = "http://two/health" }] });
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal("unavailable", await health.GetAsync(plugin, Ct).WaitAsync(TimeSpan.FromSeconds(2), Ct));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(2, handler.Requests.Count);
        var resolver = new HealthResolver([], async ct => await Task.Delay(Timeout.Infinite, ct));
        Assert.Equal("unavailable", await Service(handler, resolver, options: new PluginHealthOptions { ProbeTimeoutSeconds = 0.1 })
            .GetAsync(Plugin(new TopologyTable { Service = "slow-dns", Health = "/health" }), Ct).WaitAsync(TimeSpan.FromSeconds(2), Ct));
    }

    [Fact]
    public async Task A_resolver_ignoring_cancellation_is_still_bounded_by_the_total_budget()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.OK));
        var resolver = new HealthResolver(["10.0.0.1"], _ => release.Task);
        try
        {
            var health = Service(handler, resolver, options: new PluginHealthOptions { ProbeTimeoutSeconds = 0.1 });
            Assert.Equal("unavailable", await health.GetAsync(Plugin(new TopologyTable { Service = "slow-dns", Health = "/health" }), Ct)
                .WaitAsync(TimeSpan.FromSeconds(1), Ct));
            Assert.Empty(handler.Requests);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Cancellation_while_waiting_on_a_probe_does_not_cancel_or_poison_that_probe()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new HealthHandler(async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return HttpStatusCode.OK; });
        var health = Service(handler);
        var plugin = Plugin(new TopologyTable { Url = "http://remote/health" });
        var producer = health.GetAsync(plugin, Ct);
        await entered.Task.WaitAsync(Ct);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var waiting = health.GetAsync(plugin, cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.TrySetResult();
        Assert.Equal("ok", await producer);
        Assert.Equal("ok", await health.GetAsync(plugin, Ct));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Hosted_tenant_platform_and_plugin_lists_share_real_health_without_the_topology_plugin()
    {
        var manifest = Plugin(new TopologyTable { Service = "remote", Health = "/ready" }).Manifest;
        var handler = new HealthHandler((_, _) => Task.FromResult(HttpStatusCode.ServiceUnavailable));
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = [manifest], BootstrapTenantPlugins = false,
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IServiceResolver>();
                services.AddSingleton<IServiceResolver>(new HealthResolver(["10.0.0.1", "10.0.0.2"]));
                services.AddHttpClient(PluginHealth.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            },
        };
        var platform = api.ClientFor("operator", "firm-a", Role.PLATFORM_ADMIN);
        var tenant = api.ClientFor("admin", "firm-a", Role.TENANT_ADMIN);
        var allow = await platform.PutAsJsonAsync($"/api/platform/plugins/{manifest.Name}", new { allowed = true, enabled = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, allow.StatusCode);
        foreach (var path in new[] { "/api/platform/plugins", "/api/admin/plugins", "/api/plugins" })
        {
            var client = path.Contains("platform", StringComparison.Ordinal) ? platform : tenant;
            var json = await client.GetFromJsonAsync<JsonElement>(path, Ct);
            var rows = path == "/api/plugins" ? json.GetProperty("plugins") : json;
            Assert.DoesNotContain(rows.EnumerateArray(), row => row.GetProperty("name").GetString() == "topology");
            var remote = Assert.Single(rows.EnumerateArray(), row => row.GetProperty("name").GetString() == manifest.Name);
            Assert.Equal("unavailable", remote.GetProperty("health").GetString());
        }
        Assert.Equal(2, handler.Requests.Count); // the three HTTP endpoint reads share the 15-second descriptor cache
        Assert.False(handler.SawAuthorization);
    }

    private static PluginHealth Service(HealthHandler handler, HealthResolver? resolver = null, TimeProvider? clock = null, PluginHealthOptions? options = null) =>
        new(new HealthClients(handler), resolver ?? new HealthResolver(["10.0.0.1"]), clock ?? TimeProvider.System, new HealthOptions(options ?? new()));
    private sealed class HealthOptions(PluginHealthOptions value) : IOptionsMonitor<PluginHealthOptions>
    {
        public PluginHealthOptions CurrentValue => value;
        public PluginHealthOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PluginHealthOptions, string?> listener) => null;
    }
    private sealed class HealthClients(HealthHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(PluginHealth.HttpClientName, name);
            var client = new HttpClient(handler, false);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fixture-caller-token");
            return client;
        }
    }
    private sealed class HealthResolver(IReadOnlyList<string> addresses, Func<CancellationToken, Task>? before = null) : IServiceResolver
    {
        public int Calls { get; private set; }
        public async Task<IReadOnlyList<string>> ResolveAsync(string service, CancellationToken ct)
        {
            Calls++;
            if (before is not null) await before(ct);
            return addresses;
        }
    }
    private sealed class HealthHandler(Func<Uri, CancellationToken, Task<HttpStatusCode>> response) : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentBag<Uri> Requests { get; } = [];
        public System.Collections.Concurrent.ConcurrentBag<string> ReplicaAddresses { get; } = [];
        public bool CorrectReplicaTransport { get; private set; } = true;
        public bool SawAuthorization { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            SawAuthorization |= request.Headers.Authorization is not null;
            CorrectReplicaTransport &= request.Version == HttpVersion.Version11
                && request.VersionPolicy == HttpVersionPolicy.RequestVersionExact && request.Headers.ConnectionClose == true;
            var fixtureTarget = request.RequestUri!;
            if (request.Options.TryGetValue(PluginHealth.ReplicaAddress, out var address))
            {
                ReplicaAddresses.Add(address);
                fixtureTarget = new UriBuilder(fixtureTarget) { Host = address }.Uri;
            }
            return new HttpResponseMessage(await response(fixtureTarget, ct));
        }
    }
}
