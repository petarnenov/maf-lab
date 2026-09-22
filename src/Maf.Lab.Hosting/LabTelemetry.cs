using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Maf.Lab.Hosting;

/// <summary>
/// The one place the lab's services say what they emit about themselves. Every service calls
/// <see cref="AddLabTelemetry"/> once with its own name; what it measures is the same everywhere, which is why
/// this sits beside <see cref="InstanceIdentity"/> in a library none of them owns.
///
/// Off unless an OTLP endpoint is configured, so the tests and a local run neither export nor wait for a
/// collector that is not there.
/// </summary>
public static class LabTelemetry
{
    public const string Section = "Telemetry";

    /// <summary>Spans and instruments this system writes itself, where no library provides one.</summary>
    public const string SourceName = "maf-lab";

    /// <summary>The agent framework's own instrumentation, which is where the model and agent signals come from.</summary>
    public const string AgentsSource = "Experimental.Microsoft.Agents.AI";
    public const string AiSource = "Experimental.Microsoft.Extensions.AI";

    public static readonly ActivitySource Source = new(SourceName);
    public static readonly Meter Meter = new(SourceName);

    /// <summary>Configured endpoint, or null when nothing is listening and nothing should be exported.</summary>
    public static string? EndpointOf(IConfiguration configuration)
    {
        var endpoint = configuration[$"{Section}:Endpoint"];
        return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
    }

    /// <summary>
    /// What the lab measures about itself, where no library measures it. One prefix, so these are recognisable in
    /// the metrics store beside the convention-named ones the framework emits.
    /// </summary>
    public static class Instruments
    {
        /// <summary>Turns by outcome: started, answered, failed, awaiting a person.</summary>
        public static readonly Counter<long> Turns =
            Meter.CreateCounter<long>("maf.turns", "{turn}", "Chat turns, by outcome.");

        public static readonly Histogram<double> TurnDuration =
            Meter.CreateHistogram<double>("maf.turn.duration", "ms", "How long a chat turn takes.");

        /// <summary>Tool calls by tool and outcome, counted where the audit row is written.</summary>
        public static readonly Counter<long> ToolCalls =
            Meter.CreateCounter<long>("maf.tool.calls", "{call}", "Tool calls, by tool and outcome.");

        public static readonly Histogram<double> RetrievalStage =
            Meter.CreateHistogram<double>("maf.retrieval.stage.duration", "ms", "How long a stage of retrieval takes.");
    }

    /// <summary>
    /// Runs a step inside a span of the lab's own source. For the steps no library instruments — the MCP tool
    /// call, the vector store query, the sparse encode — and for nothing else.
    /// </summary>
    public static async Task<T> InSpanAsync<T>(string name, Func<Task<T>> work, params (string Key, object? Value)[] tags)
    {
        using var activity = Source.StartActivity(name);
        foreach (var (key, value) in tags)
        {
            activity?.SetTag(key, value);
        }
        return await work();
    }

    /// <summary>
    /// What every signal of this service says about where it came from: the service's name and the instance that
    /// produced it — the name the load balancer already reports, so a number can be attributed to a replica.
    /// </summary>
    public static ResourceBuilder ResourceFor(string serviceName) => ResourceBuilder.CreateDefault()
        .AddService(serviceName, serviceVersion: typeof(LabTelemetry).Assembly.GetName().Version?.ToString())
        .AddAttributes([new KeyValuePair<string, object>("service.instance.id", InstanceIdentity.Name)]);

    /// <param name="serviceName">What this service is called in every signal it emits.</param>
    public static IHostApplicationBuilder AddLabTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        var endpoint = EndpointOf(builder.Configuration);
        if (endpoint is null)
        {
            return builder;
        }
        var uri = new Uri(endpoint);
        var resource = ResourceFor(serviceName);

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                // The agent and the model are instrumented by the framework; this only listens to them.
                .AddSource(AgentsSource)
                .AddSource(AiSource)
                .AddSource(SourceName)
                .AddOtlpExporter(o => o.Endpoint = uri))
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(AgentsSource)
                .AddMeter(AiSource)
                .AddMeter(SourceName)
                // Cumulative, said out loud: the metrics store behind the collector counts that way, and a delta
                // export leaves every counter reading whatever happened in the last interval instead of a total.
                .AddOtlpExporter((exporter, reader) =>
                {
                    exporter.Endpoint = uri;
                    reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                }));

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resource);
            // Structure, never message content — the same rule the console logs already follow.
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = false;
            logging.AddOtlpExporter(o => o.Endpoint = uri);
        });

        return builder;
    }
}
