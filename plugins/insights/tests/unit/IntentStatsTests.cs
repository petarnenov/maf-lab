using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.Insights;
using Maf.Lab.TestSupport;

namespace Maf.Lab.Tests;

public class IntentStatsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IntentStatsSettings Settings = new("jev-1.13.0", 0.5, 0.2, 2);
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    // ---- The aggregation, fed directly ----

    [Theory]
    [InlineData(null, IntentOutcome.Used, "used")]
    [InlineData("low confidence (0.43)", IntentOutcome.Gated, "low confidence")]
    [InlineData("low confidence (none)", IntentOutcome.Gated, "low confidence")]
    [InlineData("outside the domain (0.03)", IntentOutcome.Gated, "outside the domain")]
    [InlineData("answer is not one of the known intents", IntentOutcome.Gated, "unknown choice")]
    [InlineData("timed out after 2s", IntentOutcome.Failed, "timed out")]
    [InlineData("rejected (503)", IntentOutcome.Failed, "rejected (503)")]
    [InlineData("rejected (429)", IntentOutcome.Failed, "rejected (429)")]
    [InlineData("no answer", IntentOutcome.Failed, "no answer")]
    [InlineData("no key", IntentOutcome.Failed, "no key")]
    [InlineData("classification disabled", IntentOutcome.Failed, "classification disabled")]
    [InlineData("HttpRequestException", IntentOutcome.Failed, "HttpRequestException")]
    public void Every_reason_the_classifier_writes_has_one_outcome(string? reason, string outcome, string label) =>
        Assert.Equal((outcome, label), IntentStatistics.Classify(reason));

    [Fact]
    public void Aggregates_outcomes_pipeline_histograms_and_latency()
    {
        var rows = new[]
        {
            Row(-10, Intent("Procedural", forced: true, choice: "procedural", confidence: 0.98, inDomain: 0.9, ms: 280)),
            Row(-20, Intent("Data", choice: "data", confidence: 0.91, inDomain: 0.05, ms: 300)),
            Row(-30, Intent("Other", choice: "procedural", confidence: 0.95, inDomain: 0.03, ms: 260, reason: "outside the domain (0.03)")),
            Row(-40, Intent("Other", choice: "mixed", confidence: 0.42, inDomain: 0.8, ms: 320, reason: "low confidence (0.42)")),
            Row(-50, Intent("Other", ms: 2003, reason: "timed out after 2s")),
            Row(-60, Intent("Other", ms: 120, reason: "rejected (503)")),
            // Written by an earlier classifier: excluded, but counted as such.
            Row(-70, Intent("Procedural", forced: true, model: "gemma4:31b", ms: 480)),
            Row(-80, Intent("Procedural", forced: true, model: null)),
            // No intent event at all (a trace of something else) is not an exclusion.
            Row(-90, "[]"),
        };

        var r = IntentStatistics.Aggregate(rows, "24h", Settings, Now);

        Assert.Equal(new IntentStatsTotals(6, 2, 2, 2, 1, 2), r.Totals);
        Assert.Equal(new IntentPipelineCounts(6, 2, 4, 0, 1, 1, 2, 1, 1), r.Pipeline);
        Assert.Equal(60, r.BucketMinutes);
        Assert.Equal(24, r.Timeline.Count);
        Assert.Equal(6, r.Timeline.Sum(b => b.Used + b.Gated + b.Failed));
        Assert.Equal(1, r.Timeline.Sum(b => b.TimedOut));

        Assert.Contains(new IntentReasonCount(IntentOutcome.Failed, "rejected (503)", 1), r.Reasons);
        Assert.Contains(new IntentReasonCount(IntentOutcome.Gated, "outside the domain", 1), r.Reasons);
        Assert.Equal(IntentOutcome.Used, r.Reasons[0].Outcome);
        Assert.Contains(new IntentChoiceCount("procedural", "other", 1), r.Choices);
        Assert.Contains(new IntentChoiceCount("none", "other", 2), r.Choices);

        // 0.98 and 0.95 → bin 19; 0.91 → bin 18; 0.42 → bin 8 (gated).
        Assert.Equal(20, r.Confidence.Count);
        Assert.Equal((1, 1), (r.Confidence[19].Used, r.Confidence[19].Gated));
        Assert.Equal((1, 0), (r.Confidence[18].Used, r.Confidence[18].Gated));
        Assert.Equal((0, 1), (r.Confidence[8].Used, r.Confidence[8].Gated));
        Assert.Equal(4, r.InDomain.Sum(b => b.Used + b.Gated));
        Assert.Equal(4, r.Points.Count);
        Assert.DoesNotContain(r.Points, p => p.Outcome == IntentOutcome.Failed);

        Assert.Equal(["procedural", "mixed", "data", "chitchat", "other"], r.MeanProbabilities.Select(m => m.Intent));
        Assert.Equal(2, r.MeanProbabilities[0].Chosen);

        Assert.Equal(6, r.Latency.Count);
        Assert.Equal(280, r.Latency.P50);
        Assert.Equal(2003, r.Latency.Max);
        Assert.Equal(21, r.Latency.Bins.Count);
        Assert.Null(r.Latency.Bins[^1].ToMs);
        Assert.Equal(1, r.Latency.Bins[^1].Count);
        Assert.Equal(1, r.Latency.Bins[1].Count);

        Assert.Equal([new IntentModelCount("jev-1.13.0", 6)], r.Models);
    }

    [Fact]
    public void Nothing_recorded_is_zeros_and_empty_distributions()
    {
        var r = IntentStatistics.Aggregate([], "1h", Settings, Now);
        Assert.Equal(new IntentStatsTotals(0, 0, 0, 0, 0, 0), r.Totals);
        Assert.Equal(12, r.Timeline.Count);
        Assert.All(r.Timeline, b => Assert.Null(b.P50Ms));
        Assert.Empty(r.Points);
        Assert.Null(r.Latency.P90);
        Assert.All(r.MeanProbabilities, m => Assert.Equal(0, m.Mean));
    }

    [Fact]
    public void Percentiles_are_nearest_rank()
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        Assert.Equal(50, IntentStatistics.Percentile(values, 0.5));
        Assert.Equal(90, IntentStatistics.Percentile(values, 0.9));
        Assert.Equal(99, IntentStatistics.Percentile(values, 0.99));
        Assert.Equal(7, IntentStatistics.Percentile([7], 0.99));
        Assert.Null(IntentStatistics.Percentile([], 0.5));
    }

    // ---- Through the API ----

    [Fact]
    public async Task Counts_the_firms_turns_under_their_outcomes()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = InsightsPluginSupport.Installed,
            ExtraSettings = new Dictionary<string, string?> { ["Jev:TimeoutSeconds"] = "0.3" },
        };
        var adam = api.ClientFor("adam", "firm-a", Role.USER);

        await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");   // used, forced
        await ApiFactory.ChatAsync(adam, "hello");                                                  // used, chitchat
        api.Jev.Confidence = 0.3;
        await ApiFactory.ChatAsync(adam, "what is the procedure for closing a billing period");     // low confidence
        api.Jev.Confidence = 1.0;
        api.Jev.InDomain = 0.01;
        await ApiFactory.ChatAsync(adam, "what is the procedure by which a frog eats an elephant");  // outside the domain
        api.Jev.InDomain = 1.0;
        api.Jev.Hang = TimeSpan.FromSeconds(1);
        await ApiFactory.ChatAsync(adam, "how do I re-run a failed billing run");                   // timed out
        api.Jev.Hang = null;

        var r = await StatsAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN));

        Assert.Equal(new IntentStatsTotals(5, 2, 2, 1, 1, 0), r.Totals);
        Assert.Equal(1, r.Pipeline.Forced);
        Assert.Contains(r.Reasons, x => x.Label == "timed out" && x.Count == 1);
        Assert.Contains(r.Reasons, x => x.Label == "low confidence" && x.Count == 1);
        Assert.Contains(r.Reasons, x => x.Label == "outside the domain" && x.Count == 1);
        Assert.Equal(1, r.Timeline.Sum(b => b.TimedOut));
        Assert.Equal(new IntentStatsSettings("jev-1.13.0", 0.5, 0.2, 0.3), r.Settings);
        Assert.Equal("jev-1.13.0", Assert.Single(r.Models).Model);
    }

    [Fact]
    public async Task A_firm_admin_never_sees_another_firms_turns()
    {
        using var api = InsightsPluginSupport.Api(ApiFactory.ProceduralModel());
        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "what is the procedure when a fee schedule is missing");
        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "hello");
        await ApiFactory.ChatAsync(api.ClientFor("bob", "firm-b", Role.USER), "hello");

        Assert.Equal(2, (await StatsAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN))).Totals.Classified);
        Assert.Equal(1, (await StatsAsync(api.ClientFor("bea", "firm-b", Role.TENANT_ADMIN))).Totals.Classified);
        // No parameter can widen it: an unknown one is ignored, and the firm still comes from the token.
        var widened = await api.ClientFor("bea", "firm-b", Role.TENANT_ADMIN)
            .GetFromJsonAsync<IntentStatsReport>("/api/admin/intent-stats?window=24h&firmId=firm-a", Json, Ct);
        Assert.Equal(1, widened!.Totals.Classified);
        Assert.Equal(0, (await StatsAsync(api.ClientFor("carl", "firm-c", Role.TENANT_ADMIN))).Totals.Classified);
    }

    [Fact]
    public async Task Only_a_firm_admin_may_read_and_only_the_listed_windows()
    {
        using var api = InsightsPluginSupport.Api(ApiFactory.ProceduralModel());
        foreach (var role in new[] { Role.USER, Role.USER })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor("x", "firm-a", role).GetAsync("/api/admin/intent-stats", Ct)).StatusCode);
        }
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var refused = await admin.GetAsync("/api/admin/intent-stats?window=30d", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("1h, 24h, 7d", await refused.Content.ReadAsStringAsync(Ct));
        foreach (var window in new[] { "1h", "24h", "7d" })
        {
            Assert.Equal(window, (await StatsAsync(admin, window)).Window);
        }
    }

    [Fact]
    public async Task No_message_content_in_the_response_or_the_logs()
    {
        const string marker = "zq-sentinel-7781";
        using var api = InsightsPluginSupport.Api(ApiFactory.ProceduralModel());
        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), $"what is the procedure for {marker} fees");

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN).GetAsync("/api/admin/intent-stats", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(marker, body);
        Assert.DoesNotContain("adam", body);
        Assert.DoesNotContain("turnId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains(marker));
    }

    private static async Task<IntentStatsReport> StatsAsync(HttpClient client, string window = "24h") =>
        (await client.GetFromJsonAsync<IntentStatsReport>($"/api/admin/intent-stats?window={window}", Json, Ct))!;

    private static IntentStatistics.TraceRow Row(int minutesAgo, string json) =>
        new(Now.AddMinutes(minutesAgo).UtcDateTime, json);

    /// <summary>A stored trace holding one intent event with the shape the chat turn writes.</summary>
    private static string Intent(string intent, bool forced = false, string? choice = null, double? confidence = null,
        double? inDomain = null, double? ms = null, string? reason = null, string? model = "jev-1.13.0")
    {
        var data = new JsonObject
        {
            ["intent"] = intent,
            ["forcedRetrieval"] = forced,
            ["forcedTool"] = forced ? "search_documents" : null,
            ["choice"] = choice,
            ["probabilities"] = choice is null
                ? null
                : new JsonObject(IntentStatistics.Intents.Select(i =>
                    KeyValuePair.Create(i, (JsonNode?)(i == choice ? confidence : (1 - confidence) / 4)))),
            ["confidence"] = confidence,
            ["inDomain"] = inDomain,
            ["model"] = model,
            ["durationMs"] = ms,
            ["reason"] = reason,
        };
        var events = new JsonArray(
            new JsonObject { ["seq"] = 1, ["atMs"] = 0, ["kind"] = "turn.start", ["title"] = "Turn", ["data"] = new JsonObject { ["question"] = "secret question" } },
            new JsonObject { ["seq"] = 2, ["atMs"] = 1, ["kind"] = "intent", ["title"] = "Intent", ["data"] = data });
        return events.ToJsonString();
    }
}
