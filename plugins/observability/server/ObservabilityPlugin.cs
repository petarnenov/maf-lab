using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Observability;

/// <summary>
/// The observability plugin (extract-observability-plugin): the telemetry stack's in-process part. It serves the
/// Telemetry screen's numbers, read from Prometheus by the api, and gives each turn its link into Jaeger. The stack itself
/// (collector, Prometheus, Jaeger) is this plugin's compose services; without it the core's exports go nowhere and a turn
/// carries no trace link.
/// </summary>
public sealed class ObservabilityPlugin : IMafPlugin, IContributesServices, IContributesEndpoints
{
    public const string PluginName = "observability";

    public string Name => PluginName;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelemetryQueryOptions>(configuration.GetSection(TelemetryQueryOptions.Section));
        services.AddHttpClient("telemetry");
        services.AddSingleton<TelemetryQueries>();
        services.AddSingleton<ITraceLink, JaegerTraceLink>();
    }

    public void MapEndpoints(IMafEndpoints endpoints) => TelemetryEndpoints.Map(endpoints.Routes);
}

/// <summary>A turn's trace in the trace store, from <c>Telemetry:TraceUrlTemplate</c>; none while it is empty.</summary>
internal sealed class JaegerTraceLink(IOptions<TelemetryQueryOptions> options) : ITraceLink
{
    public string? UrlFor(string traceId) => options.Value.TraceUrlFor(traceId);
}
