using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// The test-generation agent as the agents page shows it (show-test-agent-on-agents-page): its card, whether it answers,
/// what a run gets by default, and its runs — all from the api, read-only, admin only.
/// </summary>
[Collection("TestGeneration")]
public sealed class TestAgentOverviewApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Secret = "assistant-secret";
    private const string Route = "/api/admin/a2a/test-agent";

    private static ApiFactory Api(string baseUrl, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Compliance:BaseUrl"] = "",
            ["TestAgent:BaseUrl"] = baseUrl,
            ["TestAgent:ClientId"] = "maf-lab-assistant",
            ["TestAgent:ClientSecret"] = Secret,
        };
        foreach (var (k, v) in extra ?? [])
        {
            settings[k] = v;
        }
        return new ApiFactory(ApiFactory.ProceduralModel()) { ExtraSettings = settings };
    }

    private static async Task<TestAgentFactory> AgentAsync()
    {
        var repo = await TempGitRepo.CreateAsync(new Dictionary<string, string> { ["src/Lab/Calc.cs"] = "namespace Lab;\n" }, Ct);
        return new TestAgentFactory(repo, new AttemptModel(_ => new Move(new Dictionary<string, string>())), new FakeCoverageRunner());
    }

    private static async Task<string> AgentUrlAsync(TestAgentFactory agent) =>
        (await agent.ClientAsync(authenticated: false)).BaseAddress!.ToString();

    private static HttpClient Admin(ApiFactory api) => api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

    private static async Task<TestAgentOverviewDto> OverviewAsync(ApiFactory api) =>
        (await Admin(api).GetFromJsonAsync<TestAgentOverviewDto>(Route, Json, Ct))!;

    private static async Task SeedAsync(ApiFactory api, params TestGenRunRow[] rows)
    {
        await using var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct);
        db.TestGenRuns.AddRange(rows);
        await db.SaveChangesAsync(Ct);
    }

    private static TestGenRunRow Run(string id, string path, string state, int minutesAgo, int attempt = 0, double? pct = null,
        string? reason = null)
    {
        var row = CoverageStorageTests.Run(id, path, state);
        row.UpdatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
        row.Attempt = attempt;
        row.LastPct = pct;
        row.Reason = reason;
        return row;
    }

    [Fact]
    public async Task A_reachable_agent_shows_its_card()
    {
        await using var agent = await AgentAsync();
        using var api = Api(await AgentUrlAsync(agent));

        var overview = await OverviewAsync(api);

        Assert.True(overview.Status.Configured);
        Assert.True(overview.Status.Reachable);
        Assert.Null(overview.Status.Reason);
        var card = overview.Card!;
        Assert.Equal("maf-lab test agent", card.Name);
        Assert.False(string.IsNullOrEmpty(card.Version));
        var skill = Assert.Single(card.Skills);
        Assert.Equal("generate-tests", skill.Id);
        Assert.Contains("coverage", skill.Tags);
        Assert.Equal(["a2a.testgen.run"], card.RequiredScopes);
        Assert.EndsWith("/a2a", card.Endpoint);
        Assert.True(card.Streaming);
        Assert.Equal("maf-lab-assistant", overview.Connection!.ClientId);
    }

    [Fact]
    public async Task An_unreachable_agent_still_gets_its_defaults_and_runs()
    {
        using var api = Api("http://127.0.0.1:1/");
        await SeedAsync(api, Run("r_1", "src/A.cs", TestGenRunState.Working, 1));

        var overview = await OverviewAsync(api);

        Assert.True(overview.Status.Configured);
        Assert.False(overview.Status.Reachable);
        Assert.False(string.IsNullOrWhiteSpace(overview.Status.Reason));
        Assert.DoesNotContain("Exception", overview.Status.Reason);
        Assert.Null(overview.Card);
        Assert.Equal(1, overview.Runs.Running);
        Assert.Equal(TestGenRequest.AttemptLimit, overview.Limits.MaxAttempts.Default);
    }

    [Fact]
    public async Task No_agent_configured_is_said_and_nothing_is_asked()
    {
        using var api = Api("");

        var overview = await OverviewAsync(api);

        Assert.False(overview.Status.Configured);
        Assert.False(overview.Status.Reachable);
        Assert.Null(overview.Connection);
        Assert.Null(overview.Card);
    }

    [Fact]
    public async Task The_defaults_are_the_ones_a_run_gets()
    {
        using var api = Api("", new() { ["TestAgent:RunDeadline"] = "01:00:00" });

        var overview = await OverviewAsync(api);

        Assert.Equal(new LimitBounds(1, 10, 10), overview.Limits.MaxAttempts);
        Assert.Equal(new LimitBounds(1, 40, 40), overview.Limits.ToolRoundsPerAttempt);
        Assert.Equal(new LimitBounds(0, 2, 2), overview.Limits.TestRunsPerAttempt);
        Assert.Equal(new LimitBounds(0, 3, 3), overview.Limits.MaxSuspectedBugs);
        Assert.Equal(new LimitBounds(10, 60, 60), overview.Limits.DeadlineMinutes);
        Assert.Equal(new RunBudget(null, null), overview.DefaultBudget);
        Assert.Equal("glm-5.3:cloud", overview.DefaultModel!.Tag);
        Assert.True(overview.ModelsAllowed >= 1);
    }

    [Fact]
    public async Task Runs_are_counted_by_group_and_listed_newest_change_first()
    {
        using var api = Api("");
        await SeedAsync(api,
            Run("r_work", "src/A.cs", TestGenRunState.Working, 1, attempt: 2, pct: 61.5),
            Run("r_cand", "src/B.cs", TestGenRunState.Candidate, 5, attempt: 3, pct: 90),
            Run("r_acc1", "src/C.cs", TestGenRunState.Accepted, 10),
            Run("r_acc2", "src/D.cs", TestGenRunState.Accepted, 20),
            Run("r_fail", "src/E.cs", TestGenRunState.Failed, 30, reason: "deadline"),
            Run("r_disc", "src/F.cs", TestGenRunState.Discarded, 40));

        var overview = await OverviewAsync(api);

        Assert.Equal(new TestAgentRunCounts(1, 1, 2, 1, 1, 6), overview.Runs);
        Assert.Equal(["r_work", "r_cand", "r_acc1", "r_acc2", "r_fail", "r_disc"], overview.Recent.Select(r => r.Id));
        var working = overview.Recent[0];
        Assert.Equal(("src/A.cs", 2, 5, 61.5, 85), (working.Path, working.Attempt, working.MaxAttempts, working.LastPct!.Value, working.TargetPct));
        Assert.Equal("deadline", overview.Recent[4].Reason);
    }

    [Fact]
    public async Task Each_recent_run_says_how_long_it_took()
    {
        using var api = Api("");
        var accepted = Run("r_acc", "src/A.cs", TestGenRunState.Accepted, 1);
        accepted.CreatedAt = DateTime.UtcNow.AddMinutes(-60);
        accepted.FinishedAt = accepted.CreatedAt.AddMinutes(7);
        var working = Run("r_work", "src/B.cs", TestGenRunState.Working, 2);
        working.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        var unknown = Run("r_old", "src/C.cs", TestGenRunState.Failed, 3);
        await SeedAsync(api, accepted, working, unknown);

        var recent = (await OverviewAsync(api)).Recent.ToDictionary(r => r.Id);

        Assert.Equal(7 * 60_000L, recent["r_acc"].DurationMs);
        Assert.Equal(accepted.FinishedAt.Value, recent["r_acc"].FinishedAt!.Value.UtcDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Null(recent["r_work"].FinishedAt);
        Assert.InRange(recent["r_work"].DurationMs!.Value, 2 * 60_000L, 3 * 60_000L);
        Assert.Equal(working.CreatedAt, recent["r_work"].StartedAt.UtcDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Null(recent["r_old"].DurationMs);
    }

    [Fact]
    public async Task Each_recent_run_says_what_it_cost()
    {
        using var api = Api("");
        var accepted = Run("r_acc", "src/A.cs", TestGenRunState.Accepted, 1);
        accepted.Tokens = 2_760_003;
        accepted.CostUsd = 0.291;
        accepted.BudgetCostUsd = 0.5;
        var working = Run("r_work", "src/B.cs", TestGenRunState.Working, 2);
        working.Tokens = 300_000;
        working.CostUsd = 0.0421;
        var nothing = Run("r_none", "src/C.cs", TestGenRunState.Failed, 3);
        await SeedAsync(api, accepted, working, nothing);

        var recent = (await OverviewAsync(api)).Recent.ToDictionary(r => r.Id);

        Assert.Equal((2_760_003L, 0.291, new RunBudget(null, 0.5)), (recent["r_acc"].Tokens, recent["r_acc"].CostUsd, recent["r_acc"].Budget));
        // The configured models are priced at the lab's estimate.
        Assert.True(recent["r_acc"].CostIsEstimate);
        Assert.Equal(0.0421, recent["r_work"].CostUsd);
        Assert.Equal((0L, 0.0), (recent["r_none"].Tokens, recent["r_none"].CostUsd));
    }

    [Fact]
    public async Task At_most_ten_recent_runs_are_listed()
    {
        using var api = Api("");
        await SeedAsync(api, [.. Enumerable.Range(0, 12).Select(i => Run($"r_{i}", $"src/F{i}.cs", TestGenRunState.Accepted, i))]);

        var overview = await OverviewAsync(api);

        Assert.Equal(TestAgentOverview.RecentRuns, overview.Recent.Count);
        Assert.Equal(12, overview.Runs.Total);
    }

    [Fact]
    public async Task The_secret_never_leaves()
    {
        await using var agent = await AgentAsync();
        using var api = Api(await AgentUrlAsync(agent));

        var body = await Admin(api).GetStringAsync(Route, Ct);

        Assert.Contains("maf-lab-assistant", body);
        Assert.DoesNotContain(Secret, body);
    }

    [Fact]
    public async Task An_advisor_is_refused()
    {
        using var api = Api("");

        var response = await api.ClientFor("bob", "firm-a", Role.ADVISOR).GetAsync(Route, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_second_look_within_the_cache_window_does_not_ask_the_agent_again()
    {
        var agent = await AgentAsync();
        using var api = Api(await AgentUrlAsync(agent), new() { ["TestAgent:ProbeCacheFor"] = "00:05:00" });
        Assert.True((await OverviewAsync(api)).Status.Reachable);

        await agent.DisposeAsync();

        // The agent is gone, but the answer is the one checked a moment ago, with the time it was checked.
        var again = await OverviewAsync(api);
        Assert.True(again.Status.Reachable);
        Assert.NotNull(again.Card);
    }
}
