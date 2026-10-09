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
    public static IEndpointRouteBuilder MapPlugins(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plugins", async (HttpContext http, PluginCatalogue catalogue, Agent.DomainCatalogue domains, PluginHealth health,
            CancellationToken ct) =>
        {
            var signedIn = http.User.Identity?.IsAuthenticated == true;
            var set = catalogue.Current;
            var visible = set.Plugins.Where(p => signedIn ? PluginAccessContext.Current?.IsInUse(p.Name) == true : p.Manifest.Public).ToList();
            var items = await Task.WhenAll(visible.Select(async p => new PluginInfo(
                p.Name, p.Manifest.Kind, p.Manifest.Scope, p.Manifest.Description,
                await health.GetAsync(p, ct),
                p.Manifest.Domain?.Id,
                p.Manifest.Domain?.CardTypes.Values.Distinct().ToList() ?? [])));
            // The domains in use, built-in ones included, so the chat page can say up front when there are none (5h).
            var inUse = signedIn ? domains.All.Select(d => new DomainInfo(d.Id, d.ScopeSummary)).ToList() : [];
            return Results.Ok(new PluginList(items, signedIn ? set.Problems : [], inUse));
        }).AllowAnonymous();

        var admin = app.MapGroup("/api/plugins/{name}/open-work").RequireAuthorization(AuthPolicies.TenantAdmin);
        admin.MapGet("", async Task<IResult> (string name, IEnumerable<NamedOpenWork> work, CancellationToken ct) =>
        {
            if (PluginAccessContext.Current?.IsInUse(name) != true) return Results.NotFound();
            var items = new List<OpenWorkItem>();
            foreach (var w in work.Where(w => w.Plugin == name))
            {
                items.AddRange(await w.Work.ListOpenAsync(ct));
            }
            return Results.Ok(items);
        });
        admin.MapPost("/cancel", async Task<IResult> (string name, IEnumerable<NamedOpenWork> work, CancellationToken ct) =>
        {
            if (PluginAccessContext.Current?.IsInUse(name) != true) return Results.NotFound();
            foreach (var w in work.Where(w => w.Plugin == name))
            {
                await w.Work.CancelAllAsync(ct);
            }
            return Results.Accepted();
        });
        return app;
    }


}

public sealed record PluginInfo(string Name, string Kind, string Scope, string Description, string Health, string? Domain,
    IReadOnlyList<string> CardTypes);

public sealed record PluginList(IReadOnlyList<PluginInfo> Plugins, IReadOnlyList<string> Problems, IReadOnlyList<DomainInfo> Domains);

/// <summary>A domain in use: its id, and how it is named for a user, by language ("en", "bg").</summary>
public sealed record DomainInfo(string Id, IReadOnlyDictionary<string, string> Scope);
