using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Observability;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

/// <summary>
/// The observability plugin in the api (extract-observability-plugin): the Telemetry screen's route and a turn's link to
/// its trace exist while it is installed, and neither does without it.
/// </summary>
public class ObservabilityPluginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ApiFactory Api(bool installed) => ObservabilityPluginSupport.Api(installed);

    [Fact]
    public async Task The_telemetry_route_is_the_plugins()
    {
        using (var without = Api(installed: false))
        {
            var response = await without.ClientFor("operator", "firm-a", Role.PLATFORM_ADMIN).GetAsync("/api/platform/telemetry?window=1h", Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var with = Api(installed: true);
        // No metrics store is configured here: the screen still answers, and says the numbers are unavailable.
        var report = await with.ClientFor("operator", "firm-a", Role.PLATFORM_ADMIN)
            .GetFromJsonAsync<TelemetryReport>("/api/platform/telemetry?window=1h", Ct);
        Assert.NotNull(report);
        Assert.False(report.Available);
        Assert.Equal("http://localhost:7171/jaeger", report.TraceUrl);
    }

    [Fact]
    public async Task A_turn_links_to_its_trace_only_while_the_plugin_is_installed()
    {
        Assert.Null(await TraceUrlOfATurn(installed: false));

        var url = await TraceUrlOfATurn(installed: true);
        Assert.NotNull(url);
        Assert.StartsWith("http://localhost:7171/jaeger/trace/", url);
        Assert.DoesNotContain("{traceId}", url);
    }

    private static async Task<string?> TraceUrlOfATurn(bool installed)
    {
        using var api = Api(installed);
        var events = await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER),
            "what is the procedure when a fee schedule is missing");
        var start = ApiFactory.TracesOf(events).First(t => t.GetProperty("kind").GetString() == TraceKinds.TurnStart);
        return start.GetProperty("data").TryGetProperty("traceUrl", out var url) && url.ValueKind == JsonValueKind.String
            ? url.GetString()
            : null;
    }
}
