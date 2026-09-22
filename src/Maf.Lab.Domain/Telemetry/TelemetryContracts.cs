namespace Maf.Lab.Domain.Telemetry;

/// <summary>One row of a panel: what it is, and the number measured for it. Null means nothing was measured.</summary>
public sealed record TelemetrySeries(string Label, double? Value);

/// <summary>
/// One thing the telemetry screen shows. <paramref name="Series"/> is empty when the stack measured nothing in
/// the window, which the screen says rather than drawing a zero.
/// </summary>
public sealed record TelemetryPanel(string Id, string Title, string Unit, IReadOnlyList<TelemetrySeries> Series);

/// <param name="Window">The period every number covers, as the caller asked for it.</param>
/// <param name="Available">False when the metrics store could not be read; the screen still renders.</param>
/// <param name="TraceUrl">Where a turn's trace can be opened, with the trace id appended.</param>
public sealed record TelemetryReport(
    string Window,
    DateTimeOffset GeneratedAt,
    bool Available,
    string? Reason,
    IReadOnlyList<TelemetryPanel> Panels,
    string? TraceUrl);
