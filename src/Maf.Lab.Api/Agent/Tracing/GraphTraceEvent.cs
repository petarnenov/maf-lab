using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Maf.Lab.Api.Agent.Tracing;

/// <summary>
/// The turn trace's <c>graph</c> event (add-graph-trace-event), built from what a graph tool returned under
/// <c>_meta["maf-lab/graph"]</c>: the reads its call made, in order, and their totals. Only the known structural fields
/// are copied, so whatever else a server put there never reaches the trace.
/// </summary>
public static class GraphTraceEvent
{
    public const string Ok = "ok";

    /// <summary>The event's title, data and duration; null when the payload holds no read.</summary>
    public static (string Title, JsonObject Data, long DurationMs)? From(string callId, string tool, JsonNode? diagnostics)
    {
        if (diagnostics is not JsonObject d || d["reads"] is not JsonArray array)
        {
            return null;
        }
        var reads = new JsonArray();
        var queries = new List<string>();
        var rows = 0;
        var truncated = false;
        var duration = 0.0;
        var outcome = Ok;
        foreach (var item in array)
        {
            if (item is not JsonObject r || Text(r["query"]) is not { Length: > 0 } query)
            {
                continue;
            }
            var readRows = Int(r["rows"]);
            var readTruncated = Bool(r["truncated"]);
            var readMs = Number(r["durationMs"]);
            var readOutcome = Text(r["outcome"]) ?? Ok;
            reads.Add(new JsonObject
            {
                ["query"] = query,
                ["limit"] = Int(r["limit"]),
                ["rows"] = readRows,
                ["truncated"] = readTruncated,
                ["durationMs"] = readMs,
                ["outcome"] = readOutcome,
                ["errorType"] = Text(r["errorType"]),
            });
            queries.Add(query);
            rows += readRows;
            truncated |= readTruncated;
            duration += readMs;
            if (outcome == Ok && readOutcome != Ok)
            {
                outcome = readOutcome;
            }
        }
        if (reads.Count == 0)
        {
            return null;
        }
        var tenants = d["tenantScope"] is JsonArray scope
            ? new JsonArray([.. scope.Select(Text).OfType<string>().Select(t => (JsonNode?)JsonValue.Create(t))])
            : [];
        var data = new JsonObject
        {
            ["callId"] = callId,
            ["tool"] = tool,
            ["instance"] = Text(d["instance"]),
            ["tenantScope"] = tenants,
            ["reads"] = reads,
            ["rows"] = rows,
            ["truncated"] = truncated,
            ["durationMs"] = Math.Round(duration, 1),
            ["outcome"] = outcome,
        };
        return (Title(queries, rows, truncated, outcome), data, (long)Math.Round(duration));
    }

    /// <summary>"Neo4j billing_neighbourhood_2 + firm_runs · 9 rows", "… · truncated", or "Neo4j callers_2 · unavailable".</summary>
    internal static string Title(IReadOnlyList<string> queries, int rows, bool truncated, string outcome)
    {
        var names = string.Join(" + ", queries);
        if (outcome != Ok)
        {
            return $"Neo4j {names} · {outcome}";
        }
        var count = rows == 1 ? "1 row" : $"{rows.ToString(CultureInfo.InvariantCulture)} rows";
        return $"Neo4j {names} · {count}{(truncated ? " · truncated" : "")}";
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static double Number(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<double>() : 0;

    private static int Int(JsonNode? node) => (int)Math.Max(0, Math.Min(int.MaxValue, Number(node)));

    private static bool Bool(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.True;
}
