using Maf.Lab.Api.Telemetry;

namespace Maf.Lab.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetry(this IEndpointRouteBuilder app)
    {
        // What the stack has measured about itself, over a window the caller picks from a list. The queries are
        // the server's; a caller chooses which period to see, and nothing else.
        app.MapGet("/api/telemetry", async (string? window, TelemetryQueries queries, CancellationToken ct) =>
        {
            var chosen = window ?? "1h";
            return TelemetryQueries.Windows.ContainsKey(chosen)
                ? Results.Ok(await queries.ReadAsync(chosen, ct))
                : Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["window"] = [$"window must be one of: {string.Join(", ", TelemetryQueries.Windows.Keys)}."],
                });
        }).RequireAuthorization();

        return app;
    }
}
