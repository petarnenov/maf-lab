namespace Maf.Lab.Api.Telemetry;

public sealed class TelemetryQueryOptions
{
    public const string Section = "Telemetry";

    /// <summary>The metrics store the screen's numbers are read from. Empty means there is none to read.</summary>
    public string PrometheusUrl { get; set; } = "";

    /// <summary>Where a trace is opened, from the browser. The trace id is appended to it.</summary>
    public string JaegerUrl { get; set; } = "";

    public double QueryTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Where one turn's trace opens, with <c>{traceId}</c> standing for it. Empty means a turn offers no link,
    /// which is what a deployment without a trace store should do.
    /// </summary>
    public string TraceUrlTemplate { get; set; } = "";

    public string? TraceUrlFor(string? traceId) =>
        string.IsNullOrWhiteSpace(TraceUrlTemplate) || string.IsNullOrWhiteSpace(traceId)
            ? null
            : TraceUrlTemplate.Replace("{traceId}", traceId, StringComparison.Ordinal);
}
