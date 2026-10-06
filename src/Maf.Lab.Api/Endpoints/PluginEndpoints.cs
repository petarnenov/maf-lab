using System.Collections.Concurrent;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Endpoints;

/// <summary>
/// What the web and make learn about the installed plugins (introduce-plugins tasks 3.2 and 2.3).
/// <c>GET /api/plugins</c> answers anonymously too: before sign-in it lists only plugins whose manifest says
/// <c>public = true</c> (only a sign-in plugin; none in stage or prod), after sign-in every installed plugin.
/// </summary>
public static class PluginEndpoints
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, string Health)> HealthCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan HealthTtl = TimeSpan.FromSeconds(15);

    public static IEndpointRouteBuilder MapPlugins(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plugins", async (HttpContext http, PluginCatalogue catalogue, IHttpClientFactory clients, CancellationToken ct) =>
        {
            var signedIn = http.User.Identity?.IsAuthenticated == true;
            var set = catalogue.Current;
            var visible = set.Plugins.Where(p => signedIn || p.Manifest.Public).ToList();
            var items = await Task.WhenAll(visible.Select(async p => new PluginInfo(
                p.Name, p.Manifest.Kind, p.Manifest.Scope, p.Manifest.Description,
                await HealthAsync(p, clients, ct),
                p.Manifest.Domain?.Id,
                p.Manifest.Domain?.CardTypes.Values.Distinct().ToList() ?? [])));
            return Results.Ok(new PluginList(items, signedIn ? set.Problems : []));
        }).AllowAnonymous();

        var admin = app.MapGroup("/api/plugins/{name}/open-work").RequireAuthorization(AuthPolicies.TenantAdmin);
        admin.MapGet("", async (string name, IEnumerable<NamedOpenWork> work, CancellationToken ct) =>
        {
            var items = new List<OpenWorkItem>();
            foreach (var w in work.Where(w => w.Plugin == name))
            {
                items.AddRange(await w.Work.ListOpenAsync(ct));
            }
            return Results.Ok(items);
        });
        admin.MapPost("/cancel", async (string name, IEnumerable<NamedOpenWork> work, CancellationToken ct) =>
        {
            foreach (var w in work.Where(w => w.Plugin == name))
            {
                await w.Work.CancelAllAsync(ct);
            }
            return Results.Accepted();
        });
        return app;
    }

    /// <summary>
    /// "ok" for a plugin whose code runs in this process; for a remote one, a short probe of its topology address, kept
    /// for 15 seconds; "unknown" when it names none.
    /// </summary>
    private static async Task<string> HealthAsync(InstalledPlugin plugin, IHttpClientFactory clients, CancellationToken ct)
    {
        if (plugin.HasServer)
        {
            return "ok";
        }
        if (plugin.Manifest.Topology?.Url is not { Length: > 0 } url)
        {
            return "unknown";
        }
        if (HealthCache.TryGetValue(plugin.Name, out var cached) && DateTimeOffset.UtcNow - cached.At < HealthTtl)
        {
            return cached.Health;
        }
        string health;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await clients.CreateClient("plugins").GetAsync(url, timeout.Token);
            health = response.IsSuccessStatusCode ? "ok" : "unavailable";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            health = "unavailable";
        }
        HealthCache[plugin.Name] = (DateTimeOffset.UtcNow, health);
        return health;
    }
}

public sealed record PluginInfo(string Name, string Kind, string Scope, string Description, string Health, string? Domain,
    IReadOnlyList<string> CardTypes);

public sealed record PluginList(IReadOnlyList<PluginInfo> Plugins, IReadOnlyList<string> Problems);
