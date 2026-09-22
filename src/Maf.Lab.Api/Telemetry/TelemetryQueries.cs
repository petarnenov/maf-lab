using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Telemetry;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Telemetry;

/// <summary>
/// The stack's own numbers, read from the metrics store. The caller picks a window and nothing else: the queries
/// are written here, so the screen cannot be turned into a way of running any query anybody likes, and the store
/// never has to be reachable from a browser.
/// </summary>
public sealed class TelemetryQueries(
    IHttpClientFactory http,
    IOptions<TelemetryQueryOptions> options,
    TimeProvider time,
    ILogger<TelemetryQueries> logger)
{
    /// <summary>The windows a caller may ask for. Anything else is refused rather than passed on.</summary>
    public static readonly IReadOnlyDictionary<string, string> Windows = new Dictionary<string, string>
    {
        ["15m"] = "15 minutes",
        ["1h"] = "1 hour",
        ["6h"] = "6 hours",
        ["24h"] = "24 hours",
    };

    /// <summary>
    /// What the screen shows, and the query behind each. `$w` is replaced by the window the caller chose; nothing
    /// else of theirs reaches the store.
    /// </summary>
    private static readonly Panel[] Panels =
    [
        new("turns", "Turns by outcome", "turns", "sum by (outcome) (increase(maf_turns_total[$w]))", "outcome"),
        new("turn_duration", "Turn duration (p95)", "ms",
            "histogram_quantile(0.95, sum by (le) (rate(maf_turn_duration_milliseconds_bucket[$w])))"),
        new("model_duration", "Model call duration (p95)", "s",
            "histogram_quantile(0.95, sum by (le, gen_ai_request_model) (rate(gen_ai_client_operation_duration_seconds_bucket[$w])))",
            "gen_ai_request_model"),
        new("tokens", "Tokens used", "tokens",
            // A histogram of tokens per call: the tokens themselves are the sum, not the number of recordings.
            "sum by (gen_ai_request_model, gen_ai_token_type) (increase(gen_ai_client_token_usage_sum[$w]))",
            "gen_ai_request_model", "gen_ai_token_type"),
        new("tool_calls", "Tool calls by outcome", "calls",
            "sum by (tool_name, outcome) (increase(maf_tool_calls_total[$w]))", "tool_name", "outcome"),
        new("retrieval", "Retrieval by stage (p95)", "ms",
            "histogram_quantile(0.95, sum by (le, stage) (rate(maf_retrieval_stage_duration_milliseconds_bucket[$w])))",
            "stage"),
        new("instances", "Turns by instance", "turns",
            "sum by (service_instance_id) (increase(maf_turns_total{outcome=\"answered\"}[$w]))", "service_instance_id"),
    ];

    public async Task<TelemetryReport> ReadAsync(string window, CancellationToken ct)
    {
        var url = options.Value.PrometheusUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return Unavailable(window, "No metrics store is configured for this deployment.");
        }

        using var client = http.CreateClient("telemetry");
        client.Timeout = TimeSpan.FromSeconds(options.Value.QueryTimeoutSeconds);
        var panels = new List<TelemetryPanel>();
        try
        {
            foreach (var panel in Panels)
            {
                panels.Add(new TelemetryPanel(panel.Id, panel.Title, panel.Unit,
                    await QueryAsync(client, url.TrimEnd('/'), panel, window, ct)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A screen that cannot read the numbers still renders; what it must not do is claim they are zero.
            logger.LogWarning("telemetry query failed: {ErrorType}", ex.GetType().Name);
            return Unavailable(window, "The metrics store could not be reached.");
        }

        return new TelemetryReport(window, time.GetUtcNow(), true, null, panels, TraceUrl());
    }

    private async Task<IReadOnlyList<TelemetrySeries>> QueryAsync(HttpClient client, string prometheus, Panel panel,
        string window, CancellationToken ct)
    {
        var query = panel.Query.Replace("$w", window, StringComparison.Ordinal);
        var response = await client.GetAsync($"{prometheus}/api/v1/query?query={Uri.EscapeDataString(query)}", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        if (body.GetProperty("status").GetString() != "success")
        {
            return [];
        }
        var results = body.GetProperty("data").GetProperty("result");
        var series = new List<TelemetrySeries>();
        foreach (var row in results.EnumerateArray())
        {
            var metric = row.GetProperty("metric");
            var label = panel.Labels.Length == 0
                ? panel.Title
                : string.Join(" · ", panel.Labels.Select(l =>
                    metric.TryGetProperty(l, out var v) ? v.GetString() ?? "?" : "?"));
            var raw = row.GetProperty("value")[1].GetString();
            if (!double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value)
                || double.IsNaN(value) || value == 0)
            {
                // Nothing happened here in this window — a replica that has since gone, an outcome nothing
                // reached. That is no data, and must not be shown as a measured zero.
                continue;
            }
            series.Add(new TelemetrySeries(label, value));
        }
        return series;
    }

    private TelemetryReport Unavailable(string window, string reason) =>
        new(window, time.GetUtcNow(), false, reason, [], TraceUrl());

    private string? TraceUrl() =>
        string.IsNullOrWhiteSpace(options.Value.JaegerUrl) ? null : options.Value.JaegerUrl.TrimEnd('/');

    /// <param name="LabelNames">Which of the result's labels name a row; none means the panel is one number.</param>
    private sealed record Panel(string Id, string Title, string Unit, string Query, params string[] LabelNames)
    {
        public string[] Labels => LabelNames;
    }
}
