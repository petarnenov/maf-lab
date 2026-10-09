namespace Maf.Lab.Plugins.Topology;

public static class TopologyEndpoints
{
    public static IEndpointRouteBuilder MapTopology(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/topology").RequireAuthorization();
        api.WithMetadata(Maf.Lab.Domain.Tenancy.OperatorConfigurationAccess.Instance);
        api.MapGet("", async (HttpContext context, TopologyProbe probe, CancellationToken ct) =>
        {
            var token = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
            return Results.Ok(await probe.GetAsync(token, ct));
        });
        api.MapGet("/diagram", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Stream(DiagramStore.Open(), "application/xml");
        });
        return api;
    }
}

public static class DiagramStore
{
    public static Stream Open() => typeof(DiagramStore).Assembly.GetManifestResourceStream("Maf.Lab.Plugins.Topology.topology.drawio")
        ?? throw new InvalidOperationException("The topology drawing is missing from its assembly.");
}
