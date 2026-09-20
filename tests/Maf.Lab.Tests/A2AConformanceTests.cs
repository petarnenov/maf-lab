using System.Text.Json;
using A2A;
using Maf.Lab.A2AProbe;
using Maf.Lab.Domain.Evals;

namespace Maf.Lab.Tests;

/// <summary>
/// The dataset and the probe that reads it. The scenarios themselves are run against a live stack by
/// `make eval-a2a` — what is held here is what can be wrong without a stack: a scenario nobody implements, and a
/// report nothing can read.
/// </summary>
public sealed class A2AConformanceTests
{
    private static string Dataset => Path.Combine(Repo(), "evals", "a2a-conformance.jsonl");

    [Fact]
    public void Every_scenario_in_the_dataset_has_a_runner()
    {
        var rows = Conformance.Load(Dataset);
        Assert.NotEmpty(rows);

        var orphans = rows.Where(r => !Scenarios.All.ContainsKey(r.Scenario)).Select(r => $"{r.Id}: {r.Scenario}");
        Assert.Empty(orphans);
        // Every row says what it is for, in a sentence a failure report can carry.
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.What), row.Id));
    }

    [Fact]
    public async Task A_scenario_nobody_implements_fails_rather_than_passing_quietly()
    {
        var row = new ConformanceRow("c-99", "teleportation", "something no runner answers to", null, null,
            JsonDocument.Parse("{}").RootElement);

        var outcome = await Scenarios.RunAsync(Context(), row, TestContext.Current.CancellationToken);

        Assert.False(outcome.Passed);
        Assert.Contains("teleportation", outcome.Reason);
    }

    [Fact]
    public async Task A_scenario_that_throws_is_reported_as_failed()
    {
        // Nothing is listening on this port, so the runner's first call throws. The probe says so.
        var row = new ConformanceRow("c-98", "direct-message", "an agent that is not there", "hello", null,
            JsonDocument.Parse("""{"contains":["anything"]}""").RootElement);

        var outcome = await Scenarios.RunAsync(Context(), row, TestContext.Current.CancellationToken);

        Assert.False(outcome.Passed);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
    }

    [Fact]
    public async Task The_report_is_the_shape_every_other_suite_writes()
    {
        var started = DateTimeOffset.Parse("2026-09-20T10:00:00Z");
        var report = Conformance.Build(
            [
                new ConformanceOutcome("c-01", "card-discovery", true, "found it"),
                new ConformanceOutcome("c-07", "cancel", false, "", "the task never reached Canceled"),
            ],
            new Dictionary<string, string> { ["baseUrl"] = "http://localhost:7171", ["partner"] = "acme-portal" },
            started, started.AddSeconds(42));

        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var path = await Conformance.WriteAsync(root, report, TestContext.Current.CancellationToken);

            // The test that matters: the harness's own type reads the probe's file. The probe cannot reference
            // Maf.Lab.Domain — that independence is what makes it evidence — so this is what holds the two
            // shapes together.
            var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            var read = JsonSerializer.Deserialize<EvalReport>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            Assert.Equal("a2a-conformance", read.Suite);
            Assert.Equal(report.RunId, read.RunId);
            Assert.False(read.Passed);
            var variant = Assert.Single(read.Variants);
            Assert.Equal("partner", variant.Name);
            Assert.Equal(2, variant.Cases);
            Assert.Equal(0.5, variant.Metrics["passRate"]);
            Assert.Equal(1, variant.Thresholds["passRate"]);
            var failure = Assert.Single(variant.Failures);
            Assert.Equal("c-07", failure.CaseId);
            Assert.Contains("Canceled", failure.Reason);

            // …and the Markdown summary the reports directory is read with.
            var markdown = await File.ReadAllTextAsync(
                Path.ChangeExtension(path, ".md"), TestContext.Current.CancellationToken);
            Assert.Contains("# Eval report: a2a-conformance — FAILED", markdown);
            Assert.Contains("| partner | 2 | 0.5 (≥1) | FAIL |", markdown);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_dataset_naming_no_scenarios_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "\n\n");
        try
        {
            Assert.Throws<InvalidOperationException>(() => Conformance.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A probe pointed at nothing: enough to dispatch a row, not enough to answer one.</summary>
    private static ProbeContext Context()
    {
        var nowhere = new Uri("http://localhost:1");
        return new ProbeContext
        {
            BaseUri = nowhere,
            Anonymous = new HttpClient { BaseAddress = nowhere },
            Authenticated = new HttpClient { BaseAddress = nowhere },
            Client = new A2AClient(nowhere, new HttpClient()),
            PublicCard = new AgentCard { Name = "nothing" },
            PushHost = "localhost",
            ReviewerUrl = "http://localhost:1/compliance",
            ReviewerSecret = "unused",
        };
    }

    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "evals")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("No evals/ directory above the test binary.");
    }
}
