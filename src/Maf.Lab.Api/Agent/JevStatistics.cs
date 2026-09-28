using System.Text.Json;
using Maf.Lab.Domain.Intent;
using Maf.Lab.Domain.Jev;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// Aggregates every Jev call site of a set of turns into a <see cref="JevStatsReport"/>. A pure function of its input:
/// it reuses <see cref="IntentStatistics"/> for the intent section and reads only the <c>guardrail</c>, <c>relevance</c>,
/// <c>retrieval</c> and <c>model.request</c> events for the rest — never the question, the passages or the model's messages — and returns
/// numbers only.
/// </summary>
public static class JevStatistics
{
    private const int RelevanceBins = 20;
    private const double LatencyBinMs = 100;
    // The content guard and the relevance judge both budget 2 s; their latency histograms share it.
    private const double ContentBudgetMs = 2000;

    /// <summary>The guard configuration surfaced with the numbers, as the running service has it.</summary>
    public readonly record struct GuardSettings(bool Enabled, double PromptBlockAt, double ContentWithholdAt, double CrossTenantAt);

    private sealed record IntentFact(DateTime At, bool Jev, bool Failed, string Intent, bool Used, bool HasRouting,
        string? RoutedTool, string? RouteReason, double? DurationMs, int ModelCalls);

    private sealed record GuardFact(DateTime At, string Check, string Decision, string? TopQuestion, bool Content,
        int Items, int UnscreenedItems, IReadOnlyList<double> ItemDurations, int Requests);

    private sealed record RelevanceFact(DateTime At, bool Silenced, bool Unavailable, bool RerankJev, double? Max,
        double? DurationMs, double? Floor, string? CallId = null, string Domain = "billing");

    /// <summary>One turn's domain verdict and what its calls did: in scope, crossed, and whether the two agree.</summary>
    private sealed record DomainFact(string Verdict, bool Crossed, bool WithCalls, bool Agreed);

    public static JevStatsReport Aggregate(
        IReadOnlyCollection<IntentStatistics.TraceRow> rows, string window, IntentStatsSettings intentSettings,
        GuardSettings guard, DateTimeOffset now)
    {
        var (span, bucket) = IntentStatistics.Windows[window];
        var from = now - span;

        var intent = new List<IntentFact>();
        var guards = new List<GuardFact>();
        var relevance = new List<RelevanceFact>();
        var domains = new List<DomainFact>();
        foreach (var row in rows)
        {
            Read(row, intent, guards, relevance, domains);
        }

        var intentReport = IntentStatistics.Aggregate(rows, window, intentSettings, now);

        // --- Cross-cutting requests and availability, per request-bearing site ---
        var jevIntents = intent.Where(i => i.Jev).ToList();
        var contentItems = guards.Where(g => g.Content).SelectMany(g => g.ItemDurations).ToList();
        var contentRequests = guards.Where(g => g.Content).Sum(g => g.Requests);
        var contentUnavailable = guards.Where(g => g.Content).Sum(g => g.UnscreenedItems);
        var relDurations = relevance.Where(r => r.DurationMs is not null).Select(r => r.DurationMs!.Value).ToList();

        var sites = new List<JevSiteSummary>
        {
            Site("intent", jevIntents.Count, jevIntents.Count(i => i.Failed),
                jevIntents.Where(i => i.DurationMs is not null).Select(i => i.DurationMs!.Value)),
            Site("guardrail", contentRequests, contentUnavailable, contentItems),
            Site("relevance", relevance.Count, relevance.Count(r => r.Unavailable), relDurations),
        };
        var requests = sites.Sum(s => s.Requests);
        var unavailable = sites.Sum(s => s.Unavailable);

        var relevanceFloor = relevance.Select(r => r.Floor).FirstOrDefault(f => f is not null);
        var overview = new JevOverview(
            new JevStatsSettings(intentSettings.Model, guard.Enabled, guard.PromptBlockAt, guard.ContentWithholdAt,
                guard.CrossTenantAt, relevanceFloor),
            requests,
            unavailable,
            intentReport.Totals.Classified,
            intentReport.Totals.Classified == 0 ? null : Math.Round((double)requests / intentReport.Totals.Classified, 2),
            sites,
            AvailabilityTimeline(jevIntents, guards, relevance, from, now, bucket));

        return new JevStatsReport(window, from, now, (int)bucket.TotalMinutes, overview, intentReport,
            Guardrail(guards, contentItems, from, now, bucket), Relevance(relevance, relevanceFloor, from, now, bucket),
            Routing(intent), Domains(domains));
    }

    private static DomainStats Domains(List<DomainFact> domains) => new(
        domains.Count,
        domains.Count(d => d.Verdict == Agent.Domains.Billing),
        domains.Count(d => d.Verdict == Agent.Domains.Portfolio),
        domains.Count(d => d.Verdict == "both"),
        domains.Count(d => d.Verdict == "none"),
        domains.Count(d => d.Crossed),
        domains.Count(d => d.WithCalls),
        domains.Count(d => d.WithCalls && d.Agreed));

    // ---- Parsing ----

    private static void Read(IntentStatistics.TraceRow row, List<IntentFact> intent, List<GuardFact> guards, List<RelevanceFact> relevance,
        List<DomainFact> domains)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(row.Json);
        }
        catch (JsonException)
        {
            return;
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            var at = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc);
            var modelCalls = 0;
            IntentFact? intentFact = null;
            // A turn recorded since the relevance event exists has one per judged search; an older turn has only the
            // judgment inside its retrieval diagnostics. One source per turn, so no search is counted twice.
            var judged = new List<RelevanceFact>();
            var diagnosed = new List<RelevanceFact>();
            // Which domain each call belonged to, so a judged search is counted against its own domain's documentation.
            var callDomains = new Dictionary<string, string>(StringComparer.Ordinal);
            string[]? predicted = null;
            string? verdict = null;
            string[]? touched = null;
            var crossed = false;
            foreach (var ev in doc.RootElement.EnumerateArray())
            {
                if (!ev.TryGetProperty("kind", out var k) || k.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                var kind = k.GetString();
                var data = ev.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
                switch (kind)
                {
                    case TraceKinds.ModelRequest:
                        modelCalls++;
                        break;
                    case TraceKinds.ToolCall when data.ValueKind == JsonValueKind.Object:
                        if (Str(data, "callId") is { } callId && Str(data, "domain") is { } callDomain)
                        {
                            callDomains[callId] = callDomain;
                        }
                        break;
                    case TraceKinds.Domain when data.ValueKind == JsonValueKind.Object:
                        predicted = Strings(data, "inScope");
                        verdict = predicted.Length switch { 0 => "none", 1 => predicted[0], _ => "both" };
                        break;
                    case TraceKinds.TurnEnd when data.ValueKind == JsonValueKind.Object:
                        touched = Strings(data, "domainsTouched");
                        crossed = data.TryGetProperty("crossings", out var c) && c.ValueKind == JsonValueKind.Number && c.GetInt32() > 0;
                        break;
                    case TraceKinds.Intent when data.ValueKind == JsonValueKind.Object:
                        intentFact = IntentOf(at, data);
                        break;
                    case TraceKinds.Guardrail when data.ValueKind == JsonValueKind.Object:
                        if (GuardOf(at, data) is { } g)
                        {
                            guards.Add(g);
                        }
                        break;
                    case TraceKinds.Retrieval when data.ValueKind == JsonValueKind.Object:
                        if (RelevanceOf(at, data) is { } r)
                        {
                            diagnosed.Add(r);
                        }
                        break;
                    case TraceKinds.Relevance when data.ValueKind == JsonValueKind.Object:
                        if (JudgedOf(at, data) is { } j)
                        {
                            judged.Add(j);
                        }
                        break;
                }
            }
            relevance.AddRange((judged.Count > 0 ? judged : diagnosed)
                .Select(r => r.CallId is { } id && callDomains.TryGetValue(id, out var domain) ? r with { Domain = domain } : r));
            if (verdict is not null)
            {
                var calls = touched is { Length: > 0 };
                domains.Add(new DomainFact(verdict, crossed, calls, calls && predicted!.Order().SequenceEqual(touched!.Order())));
            }
            if (intentFact is not null)
            {
                intent.Add(intentFact with { ModelCalls = modelCalls });
            }
        }
    }

    private static IntentFact IntentOf(DateTime at, JsonElement data)
    {
        var model = Str(data, "model");
        var jev = model is not null && model.StartsWith("jev-", StringComparison.Ordinal);
        var reason = Str(data, "reason");
        var (outcome, _) = IntentStatistics.Classify(reason);
        var intent = (Str(data, "intent") ?? "Other").ToLowerInvariant();
        string? routedTool = null, routeReason = null;
        var hasRouting = false;
        if (data.TryGetProperty("routing", out var routing) && routing.ValueKind == JsonValueKind.Object)
        {
            hasRouting = true;
            routedTool = Str(routing, "routedTool");
            routeReason = Str(routing, "reason");
        }
        return new IntentFact(at, jev, jev && outcome == IntentOutcome.Failed, intent, outcome == IntentOutcome.Used,
            hasRouting, routedTool, routeReason, Num(data, "durationMs"), 0);
    }

    private static GuardFact? GuardOf(DateTime at, JsonElement data)
    {
        // Only a real Jev screening counts; a disabled or no-key screening carries no jev model.
        var model = Str(data, "model");
        if (model is null || !model.StartsWith("jev-", StringComparison.Ordinal))
        {
            return null;
        }
        var check = Str(data, "check") ?? "unknown";
        var content = check is "tool_result" or "reviewer";
        var items = 0;
        var unscreened = 0;
        var durations = new List<double>();
        if (data.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsEl.EnumerateArray())
            {
                items++;
                if (Str(item, "decision") == "unscreened")
                {
                    unscreened++;
                }
                if (content && Num(item, "durationMs") is { } ms)
                {
                    durations.Add(ms);
                }
            }
        }
        // Recorded since the count exists: an empty item made no request. Before it, one request per item.
        var requests = data.TryGetProperty("requests", out var req) && req.ValueKind == JsonValueKind.Number ? req.GetInt32() : items;
        return new GuardFact(at, check, Str(data, "decision") ?? "pass", Str(data, "topQuestion"), content,
            items, unscreened, durations, requests);
    }

    private static RelevanceFact? RelevanceOf(DateTime at, JsonElement data)
    {
        if (!data.TryGetProperty("relevance", out var rel) || rel.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var model = Str(rel, "model");
        if (model is null || !model.StartsWith("jev-", StringComparison.Ordinal))
        {
            return null;
        }
        var reason = Str(rel, "reason");
        var unavailable = reason is not null;
        var rerankJev = data.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object
            && Str(s, "reranker") == "jev";
        return new RelevanceFact(at, Bool(rel, "silenced"), unavailable, rerankJev, Num(rel, "max"),
            Num(rel, "durationMs"), Num(rel, "floor"), Str(data, "callId"));
    }

    /// <summary>A search's judgment from its own <c>relevance</c> event — the summary, not the diagnostics.</summary>
    private static RelevanceFact? JudgedOf(DateTime at, JsonElement data)
    {
        var model = Str(data, "model");
        if (model is null || !model.StartsWith("jev-", StringComparison.Ordinal))
        {
            return null;
        }
        return new RelevanceFact(at, Bool(data, "silenced"), Str(data, "reason") is not null, Str(data, "reranker") == "jev",
            Num(data, "max"), Num(data, "durationMs"), Num(data, "floor"), Str(data, "callId"));
    }

    // ---- Sections ----

    private static GuardrailStats Guardrail(List<GuardFact> guards, List<double> contentItems, DateTimeOffset from, DateTimeOffset now, TimeSpan bucket)
    {
        var checks = guards.GroupBy(g => g.Check)
            .Select(gr => new GuardrailCheckCount(gr.Key, gr.Count(),
                gr.Count(x => x.Decision == "pass"), gr.Count(x => x.Decision == "blocked"),
                gr.Count(x => x.Decision == "withheld"), gr.Count(x => x.Decision == "unscreened")))
            .OrderBy(c => c.Check, StringComparer.Ordinal).ToList();
        var trippedBy = guards.Where(g => g.Decision is "blocked" or "withheld" && g.TopQuestion is not null)
            .GroupBy(g => (g.TopQuestion!, g.Decision))
            .Select(gr => new GuardrailQuestionCount(gr.Key.Item1, gr.Key.Decision, gr.Count()))
            .OrderByDescending(q => q.Count).ThenBy(q => q.Question, StringComparer.Ordinal).ToList();

        var buckets = Buckets(from, now, bucket, s =>
        {
            var inside = guards.Where(g => g.At >= s.Start.UtcDateTime && g.At < s.End.UtcDateTime).ToList();
            return new GuardrailTimelineBucket(s.Start, inside.Count, inside.Count(g => g.Decision == "blocked"),
                inside.Count(g => g.Decision == "withheld"), inside.Count(g => g.Decision == "unscreened"));
        });

        return new GuardrailStats(checks, trippedBy, guards.Count,
            guards.Count(g => g.Decision == "blocked"), guards.Count(g => g.Decision == "withheld"),
            guards.Count(g => g.Decision == "unscreened"), Latency(contentItems, ContentBudgetMs), buckets);
    }

    private static RelevanceStats Relevance(List<RelevanceFact> relevance, double? floor, DateTimeOffset from, DateTimeOffset now, TimeSpan bucket)
    {
        var bins = new int[RelevanceBins, 2];
        foreach (var r in relevance.Where(r => r.Max is not null))
        {
            var i = Math.Clamp((int)Math.Floor(r.Max!.Value * RelevanceBins), 0, RelevanceBins - 1);
            bins[i, r.Silenced ? 1 : 0]++;
        }
        var histogram = Enumerable.Range(0, RelevanceBins)
            .Select(i => new RelevanceMaxBin(Math.Round((double)i / RelevanceBins, 2), Math.Round((double)(i + 1) / RelevanceBins, 2),
                bins[i, 0], bins[i, 1]))
            .ToList();
        var durations = relevance.Where(r => r.DurationMs is not null).Select(r => r.DurationMs!.Value).ToList();
        var timeline = Buckets(from, now, bucket, s =>
        {
            var inside = relevance.Where(r => r.At >= s.Start.UtcDateTime && r.At < s.End.UtcDateTime).ToList();
            return new RelevanceTimelineBucket(s.Start, inside.Count, inside.Count(r => r.Silenced),
                inside.Count(r => r.Unavailable));
        });
        return new RelevanceStats(floor, relevance.Count, relevance.Count(r => r.Silenced),
            relevance.Count(r => r.RerankJev), relevance.Count(r => r.Unavailable),
            histogram, Latency(durations, ContentBudgetMs), timeline,
            [.. relevance.GroupBy(r => r.Domain).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new RelevanceDomainCount(g.Key, g.Count(), g.Count(r => r.Silenced), g.Count(r => r.Unavailable)))]);
    }

    private static RoutingStats Routing(List<IntentFact> intent)
    {
        var enabled = intent.Any(i => i.HasRouting);
        var dataTurns = intent.Where(i => i.Jev && i.Used && i.Intent == "data").ToList();
        var routed = dataTurns.Where(i => i.RoutedTool is not null).ToList();
        var notRouted = dataTurns.Where(i => i.RoutedTool is null).ToList();
        var tools = routed.GroupBy(i => i.RoutedTool!)
            .Select(g => new RoutingToolCount(g.Key, g.Count()))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Tool, StringComparer.Ordinal).ToList();
        var reasons = notRouted.Where(i => i.RouteReason is not null)
            .GroupBy(i => StripNumber(i.RouteReason!))
            .Select(g => new RoutingReasonCount(g.Key, g.Count()))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Reason, StringComparer.Ordinal).ToList();
        var routedCalls = routed.Select(i => (double)i.ModelCalls).Order().ToList();
        var unroutedCalls = notRouted.Select(i => (double)i.ModelCalls).Order().ToList();
        var latency = routed.Where(i => i.DurationMs is not null).Select(i => i.DurationMs!.Value).ToList();
        return new RoutingStats(enabled, dataTurns.Count, routed.Count, tools, reasons,
            IntentStatistics.Percentile(routedCalls, 0.5), IntentStatistics.Percentile(unroutedCalls, 0.5),
            Latency(latency, ContentBudgetMs));
    }

    private static IReadOnlyList<JevAvailabilityBucket> AvailabilityTimeline(
        List<IntentFact> jevIntents, List<GuardFact> guards, List<RelevanceFact> relevance,
        DateTimeOffset from, DateTimeOffset now, TimeSpan bucket) =>
        Buckets(from, now, bucket, s =>
        {
            bool In(DateTime at) => at >= s.Start.UtcDateTime && at < s.End.UtcDateTime;
            var content = guards.Where(g => g.Content && In(g.At)).ToList();
            var req = jevIntents.Count(i => In(i.At)) + content.Sum(g => g.Requests) + relevance.Count(r => In(r.At));
            var un = jevIntents.Count(i => In(i.At) && i.Failed) + content.Sum(g => g.UnscreenedItems)
                + relevance.Count(r => In(r.At) && r.Unavailable);
            return new JevAvailabilityBucket(s.Start, req, un);
        });

    // ---- Helpers ----

    private static JevSiteSummary Site(string name, int requests, int unavailable, IEnumerable<double> durations)
    {
        var sorted = durations.Order().ToList();
        return new JevSiteSummary(name, requests, unavailable,
            IntentStatistics.Percentile(sorted, 0.5), IntentStatistics.Percentile(sorted, 0.9));
    }

    /// <summary>A latency summary and a 100 ms-binned histogram up to a budget, like the intent one.</summary>
    private static IntentLatency Latency(List<double> durations, double budgetMs)
    {
        var sorted = durations.Order().ToList();
        var count = Math.Max(1, (int)Math.Ceiling(budgetMs / LatencyBinMs));
        var bins = new int[count + 1];
        foreach (var ms in sorted)
        {
            bins[Math.Min(count, (int)Math.Floor(ms / LatencyBinMs))]++;
        }
        var histogram = Enumerable.Range(0, count + 1)
            .Select(i => new IntentLatencyBin(i * LatencyBinMs, i == count ? null : (i + 1) * LatencyBinMs, bins[i]))
            .ToList();
        return new IntentLatency(sorted.Count, IntentStatistics.Percentile(sorted, 0.5), IntentStatistics.Percentile(sorted, 0.9),
            IntentStatistics.Percentile(sorted, 0.99), sorted.Count == 0 ? null : Math.Round(sorted[^1], 1), histogram);
    }

    private readonly record struct Slot(DateTimeOffset Start, DateTimeOffset End);

    private static List<T> Buckets<T>(DateTimeOffset from, DateTimeOffset now, TimeSpan bucket, Func<Slot, T> make)
    {
        var start = new DateTimeOffset(from.UtcTicks - from.UtcTicks % bucket.Ticks, TimeSpan.Zero);
        var list = new List<T>();
        for (var s = start; s < now; s += bucket)
        {
            list.Add(make(new Slot(s, s + bucket)));
        }
        return list;
    }

    /// <summary>Drops a trailing "(0.xx)" or "(3)" from a router reason so a count does not split it, keeping the shape.</summary>
    private static string StripNumber(string reason)
    {
        var open = reason.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && reason.EndsWith(')') ? reason[..open] : reason;
    }

    private static string[] Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)]
            : [];

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
