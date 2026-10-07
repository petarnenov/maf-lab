// What GET /api/telemetry answers: what the stack measured about itself. See this plugin's docs/http-api.md.

export interface TelemetrySeries {
  label: string;
  /** Null when the query returned nothing usable for that row. */
  value: number | null;
}

export interface TelemetryPanel {
  id: string;
  title: string;
  unit: string;
  /** Empty when nothing was measured in the window, which reads as no data rather than as zero. */
  series: TelemetrySeries[];
}

export interface TelemetryReport {
  window: string;
  generatedAt: string;
  /** False when the metrics store could not be read; the screen still renders. */
  available: boolean;
  reason: string | null;
  panels: TelemetryPanel[];
  /** Where a turn's trace opens; the trace id is appended to it. */
  traceUrl: string | null;
}
