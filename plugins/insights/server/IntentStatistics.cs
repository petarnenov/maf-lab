using System.Text.Json;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Plugins.Insights;

/// <summary>
/// Aggregates the <c>intent</c> trace events of a set of turns into <see cref="IntentStatsReport"/>. A pure function of
/// its input: it reads only the intent event of each trace, never the question, the prompt or the model's messages,
/// and returns numbers only.
/// </summary>
public static class IntentStatistics
{
    /// <summary>The windows a caller may pick, and the bucket each is drawn in. Bounded by trace retention (7 days).</summary>
    public static readonly IReadOnlyDictionary<string, (TimeSpan Span, TimeSpan Bucket)> Windows =
        new Dictionary<string, (TimeSpan, TimeSpan)>
        {
            ["1h"] = (TimeSpan.FromHours(1), TimeSpan.FromMinutes(5)),
            ["24h"] = (TimeSpan.FromHours(24), TimeSpan.FromHours(1)),
            ["7d"] = (TimeSpan.FromDays(7), TimeSpan.FromHours(6)),
        };

    public static readonly string[] Intents = ["procedural", "mixed", "data", "chitchat", "other"];

    private const int ProbabilityBins = 20;
    private const double LatencyBinMs = 100;
    private const int MaxPoints = 1000;

    /// <summary>One turn's stored trace, as the store holds it.</summary>
    public readonly record struct TraceRow(DateTime CreatedAt, string Json);

    private sealed record Event(
        DateTime At, string Outcome, string Label, string? Choice, string Intent, bool Forced, double? Confidence,
        double? InDomain, IReadOnlyDictionary<string, double>? Probabilities, string Model, double? DurationMs, bool TimedOut);

    public static IntentStatsReport Aggregate(IEnumerable<TraceRow> rows, string window, IntentStatsSettings settings, DateTimeOffset now)
    {
        var (span, bucket) = Windows[window];
        var from = now - span;
        var events = new List<Event>();
        var excluded = 0;
        foreach (var row in rows)
        {
            switch (Read(row))
            {
                case { } e:
                    events.Add(e);
                    break;
                case null when HasIntentEvent(row.Json):
                    excluded++;
                    break;
            }
        }

        var used = events.Where(e => e.Outcome == IntentOutcome.Used).ToList();
        var answered = events.Where(e => e.Outcome != IntentOutcome.Failed).ToList();
        var timed = events.Where(e => e.DurationMs is not null).Select(e => e.DurationMs!.Value).Order().ToList();
        var timeoutMs = settings.TimeoutSeconds * 1000;

        return new IntentStatsReport(
            window,
            from,
            now,
            (int)bucket.TotalMinutes,
            settings,
            new IntentStatsTotals(events.Count, used.Count, events.Count(e => e.Outcome == IntentOutcome.Gated),
                events.Count(e => e.Outcome == IntentOutcome.Failed), events.Count(e => e.Forced), excluded),
            Pipeline(events),
            events.GroupBy(e => (e.Outcome, e.Label))
                .Select(g => new IntentReasonCount(g.Key.Outcome, g.Key.Label, g.Count()))
                .OrderBy(r => OutcomeOrder(r.Outcome)).ThenByDescending(r => r.Count).ThenBy(r => r.Label, StringComparer.Ordinal)
                .ToList(),
            events.GroupBy(e => (Choice: e.Choice ?? "none", e.Intent))
                .Select(g => new IntentChoiceCount(g.Key.Choice, g.Key.Intent, g.Count()))
                .OrderBy(c => c.Choice, StringComparer.Ordinal).ThenBy(c => c.Intent, StringComparer.Ordinal)
                .ToList(),
            Timeline(events, from, now, bucket),
            Histogram(answered, e => e.Confidence),
            Histogram(answered, e => e.InDomain),
            answered.Where(e => e.Confidence is not null && e.InDomain is not null)
                .OrderByDescending(e => e.At).Take(MaxPoints)
                .Select(e => new IntentPoint(Math.Round(e.Confidence!.Value, 4), Math.Round(e.InDomain!.Value, 4), e.Outcome,
                    e.Choice ?? "none"))
                .ToList(),
            Intents.Select(intent =>
            {
                var withProbabilities = answered.Where(e => e.Probabilities is not null).ToList();
                var mean = withProbabilities.Count == 0
                    ? 0
                    : withProbabilities.Average(e => e.Probabilities!.GetValueOrDefault(intent));
                return new IntentMeanProbability(intent, Math.Round(mean, 4),
                    answered.Count(e => string.Equals(e.Choice, intent, StringComparison.OrdinalIgnoreCase)));
            }).ToList(),
            new IntentLatency(timed.Count, Percentile(timed, 0.5), Percentile(timed, 0.9), Percentile(timed, 0.99),
                timed.Count == 0 ? null : Math.Round(timed[^1], 1), LatencyBins(timed, timeoutMs)),
            events.GroupBy(e => e.Model).Select(g => new IntentModelCount(g.Key, g.Count()))
                .OrderByDescending(m => m.Count).ThenBy(m => m.Model, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Which outcome a reason means, and the label it is counted under. The reasons are the ones
    /// the api's <c>DecisionIntentClassifier</c> writes; anything unrecognised is a failure under its own label, so a new
    /// reason shows up rather than disappearing.
    /// </summary>
    public static (string Outcome, string Label) Classify(string? reason)
    {
        if (reason is null)
        {
            return (IntentOutcome.Used, "used");
        }
        if (reason.StartsWith("low confidence", StringComparison.Ordinal))
        {
            return (IntentOutcome.Gated, "low confidence");
        }
        if (reason.StartsWith("outside the domain", StringComparison.Ordinal))
        {
            return (IntentOutcome.Gated, "outside the domain");
        }
        if (reason == "answer is not one of the known intents")
        {
            return (IntentOutcome.Gated, "unknown choice");
        }
        if (reason.StartsWith("timed out", StringComparison.Ordinal))
        {
            return (IntentOutcome.Failed, "timed out");
        }
        // A rejection keeps its status: 429 and 503 are different problems.
        return (IntentOutcome.Failed, reason);
    }

    private static Event? Read(TraceRow row)
    {
        try
        {
            using var doc = JsonDocument.Parse(row.Json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            foreach (var ev in doc.RootElement.EnumerateArray())
            {
                if (ev.TryGetProperty("kind", out var kind) && kind.GetString() == TraceKinds.Intent
                    && ev.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    return FromData(row.CreatedAt, data);
                }
            }
        }
        catch (JsonException)
        {
            // A trace that does not parse contributes nothing; it is not this screen's to repair.
        }
        return null;
    }

    private static Event? FromData(DateTime at, JsonElement data)
    {
        // Jev records its versioned id on every event, answered or failed; earlier classifiers did not use this prefix.
        var model = String(data, "model");
        if (model is null || !model.StartsWith("jev-", StringComparison.Ordinal))
        {
            return null;
        }
        var reason = String(data, "reason");
        var (outcome, label) = Classify(reason);
        IReadOnlyDictionary<string, double>? probabilities = null;
        if (data.TryGetProperty("probabilities", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            probabilities = p.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.Number)
                .ToDictionary(x => x.Name.ToLowerInvariant(), x => x.Value.GetDouble());
        }
        return new Event(
            DateTime.SpecifyKind(at, DateTimeKind.Utc),
            outcome,
            label,
            String(data, "choice")?.ToLowerInvariant(),
            (String(data, "intent") ?? "Other").ToLowerInvariant(),
            data.TryGetProperty("forcedRetrieval", out var f) && f.ValueKind == JsonValueKind.True,
            Number(data, "confidence"),
            Number(data, "inDomain"),
            probabilities,
            model,
            // A classification the circuit breaker skipped sent nothing: it has no latency (add-jev-circuit-breaker).
            reason == Maf.Lab.Plugins.Abstractions.DecisionFailures.CircuitOpen ? null : Number(data, "durationMs"),
            label == "timed out");
    }

    private static bool HasIntentEvent(string json) => json.Contains("\"kind\":\"intent\"", StringComparison.Ordinal);

    private static string? String(JsonElement data, string name) =>
        data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Number(JsonElement data, string name) =>
        data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static IntentPipelineCounts Pipeline(List<Event> events)
    {
        var failed = events.Count(e => e.Outcome == IntentOutcome.Failed);
        var unknown = events.Count(e => e.Label == "unknown choice");
        var below = events.Count(e => e.Label == "low confidence");
        var outside = events.Count(e => e.Label == "outside the domain");
        // Used answers split by whether their intent forces retrieval; the domain gate only ever applied to those that do.
        var usedForcing = events.Count(e => e.Outcome == IntentOutcome.Used && e.Intent is "procedural" or "mixed");
        var usedOther = events.Count(e => e.Outcome == IntentOutcome.Used) - usedForcing;
        return new IntentPipelineCounts(events.Count, failed, events.Count - failed, unknown, below, usedOther,
            usedForcing + outside, outside, usedForcing);
    }

    private static List<IntentTimelineBucket> Timeline(List<Event> events, DateTimeOffset from, DateTimeOffset now, TimeSpan bucket)
    {
        // Buckets are aligned to the bucket size, so a refresh a minute later moves the edges only when a bucket rolls over.
        var start = new DateTimeOffset(from.UtcTicks - from.UtcTicks % bucket.Ticks, TimeSpan.Zero);
        var buckets = new List<IntentTimelineBucket>();
        for (var s = start; s < now; s += bucket)
        {
            var end = s + bucket;
            var inside = events.Where(e => e.At >= s.UtcDateTime && e.At < end.UtcDateTime).ToList();
            var ms = inside.Where(e => e.DurationMs is not null).Select(e => e.DurationMs!.Value).Order().ToList();
            buckets.Add(new IntentTimelineBucket(s,
                inside.Count(e => e.Outcome == IntentOutcome.Used),
                inside.Count(e => e.Outcome == IntentOutcome.Gated),
                inside.Count(e => e.Outcome == IntentOutcome.Failed),
                inside.Count(e => e.TimedOut),
                Percentile(ms, 0.5),
                Percentile(ms, 0.9)));
        }
        return buckets;
    }

    private static List<IntentProbabilityBin> Histogram(List<Event> answered, Func<Event, double?> value)
    {
        var bins = new int[ProbabilityBins, 2];
        foreach (var e in answered)
        {
            if (value(e) is not { } v)
            {
                continue;
            }
            var i = Math.Clamp((int)Math.Floor(v * ProbabilityBins), 0, ProbabilityBins - 1);
            bins[i, e.Outcome == IntentOutcome.Used ? 0 : 1]++;
        }
        return Enumerable.Range(0, ProbabilityBins)
            .Select(i => new IntentProbabilityBin(Math.Round((double)i / ProbabilityBins, 2), Math.Round((double)(i + 1) / ProbabilityBins, 2),
                bins[i, 0], bins[i, 1]))
            .ToList();
    }

    private static List<IntentLatencyBin> LatencyBins(List<double> sorted, double timeoutMs)
    {
        var count = Math.Max(1, (int)Math.Ceiling(timeoutMs / LatencyBinMs));
        var bins = new int[count + 1];
        foreach (var ms in sorted)
        {
            bins[Math.Min(count, (int)Math.Floor(ms / LatencyBinMs))]++;
        }
        return Enumerable.Range(0, count + 1)
            .Select(i => new IntentLatencyBin(i * LatencyBinMs, i == count ? null : (i + 1) * LatencyBinMs, bins[i]))
            .ToList();
    }

    /// <summary>Nearest-rank percentile of an ascending list; null when it is empty.</summary>
    public static double? Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return null;
        }
        var rank = (int)Math.Ceiling(p * sorted.Count);
        return Math.Round(sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)], 1);
    }

    private static int OutcomeOrder(string outcome) => outcome switch
    {
        IntentOutcome.Used => 0,
        IntentOutcome.Gated => 1,
        _ => 2,
    };
}
