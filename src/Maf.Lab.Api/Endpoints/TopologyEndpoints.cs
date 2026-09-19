using Maf.Lab.Api.Topology;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Endpoints;

/// <summary>The running shape of the stack, and the drawing it is rendered on. Infrastructure only.</summary>
public static class TopologyEndpoints
{
    public static IEndpointRouteBuilder MapTopology(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/topology").RequireAuthorization();

        api.MapGet("", async (HttpContext http, TopologyProbe probe, CancellationToken ct) =>
        {
            var token = http.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
            return Results.Ok(await probe.GetAsync(token, ct));
        });

        // The diagram lives in docs/ so it is edited in draw.io and diffed like any other file; the api serves it
        // because the web image is built from web/ only.
        api.MapGet("/diagram", (IOptions<TopologyOptions> options, IWebHostEnvironment env) =>
        {
            var path = DiagramStore.ResolvePath(options.Value.DiagramPath, env.ContentRootPath);
            return path is null
                ? Results.NotFound(new { error = "topology.drawio was not found" })
                : (IResult)Results.File(path, "application/xml");
        });

        return api;
    }
}

/// <summary>Finds the drawn diagram: configured path, or docs/topology.drawio up from the content root.</summary>
public static class DiagramStore
{
    public static string? ResolvePath(string configured, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? configured : null;
        }
        for (var dir = new DirectoryInfo(contentRoot); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "topology.drawio");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
