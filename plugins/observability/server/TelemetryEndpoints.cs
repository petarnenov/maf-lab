using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Plugins.Observability;

public static class TelemetryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // What the stack has measured about itself, over a window the caller picks from a list. The queries are
        // the server's; a caller chooses which period to see, and nothing else.
        app.MapGet("/api/platform/telemetry", async (string? window, TelemetryQueries queries, CancellationToken ct) =>
        {
            var chosen = window ?? "1h";
            return TelemetryQueries.Windows.ContainsKey(chosen)
                ? Results.Ok(await queries.ReadAsync(chosen, ct))
                : Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["window"] = [$"window must be one of: {string.Join(", ", TelemetryQueries.Windows.Keys)}."],
                });
        }).RequireAuthorization(PolicyNames.PlatformAdmin).WithMetadata(OperatorConfigurationAccess.Instance);
    }
}
