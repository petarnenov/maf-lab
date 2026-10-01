using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Storage;

namespace Maf.Lab.Tests;

/// <summary>
/// What a run cost as the agents page lists it (show-test-run-cost): the amount the run recorded — priced at the rates
/// sent to the agent at start, which is what its cost cap counted — never repriced, with whether that price is an
/// estimate read from the allowlist.
/// </summary>
public sealed class RunCostTests
{
    private static readonly DateTime Ten = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(Ten.AddMinutes(30));

    private static AgentModelOption Model(string tag, double input, double output, bool estimate) =>
        new() { Tag = tag, DisplayName = tag, InputPerMTok = input, OutputPerMTok = output, PriceIsEstimate = estimate };

    private static readonly AgentModelOption[] Allowlist =
    [
        Model("glm-5.3:cloud", 0.6, 2.2, estimate: true),
        Model("listed:cloud", 1.0, 3.0, estimate: false),
        Model("local-free", 0, 0, estimate: false),
    ];

    private static TestGenRunRow Run(string state, string model = "glm-5.3:cloud", long tokens = 0, double cost = 0)
    {
        var row = CoverageStorageTests.Run("r_1", "src/A.cs", state);
        row.Model = model;
        row.CreatedAt = Ten;
        row.UpdatedAt = Ten;
        row.Tokens = tokens;
        row.CostUsd = cost;
        return row;
    }

    [Fact]
    public void A_finished_run_shows_its_recorded_cost_tokens_and_budget()
    {
        var run = Run(TestGenRunState.Accepted, tokens: 2_760_003, cost: 0.291);
        run.FinishedAt = Ten.AddMinutes(20);
        run.BudgetCostUsd = 0.5;

        var listed = TestAgentOverview.Recent(run, Now, Allowlist);

        Assert.Equal(2_760_003, listed.Tokens);
        Assert.Equal(0.291, listed.CostUsd);
        Assert.True(listed.CostIsEstimate);
        Assert.Equal(new RunBudget(null, 0.5), listed.Budget);
    }

    [Fact]
    public void A_running_run_shows_what_it_has_spent_so_far()
    {
        var listed = TestAgentOverview.Recent(Run(TestGenRunState.Working, tokens: 300_000, cost: 0.0421), Now, Allowlist);

        Assert.Null(listed.FinishedAt);
        Assert.Equal(0.0421, listed.CostUsd);
    }

    [Fact]
    public void A_run_that_made_no_model_call_cost_nothing()
    {
        var listed = TestAgentOverview.Recent(Run(TestGenRunState.Failed), Now, Allowlist);

        Assert.Equal(0, listed.Tokens);
        Assert.Equal(0, listed.CostUsd);
        Assert.Equal(new RunBudget(null, null), listed.Budget);
    }

    [Fact]
    public void A_model_priced_at_zero_costs_zero_and_is_not_an_estimate()
    {
        var listed = TestAgentOverview.Recent(Run(TestGenRunState.Accepted, "local-free", tokens: 500_000), Now, Allowlist);

        Assert.Equal(500_000, listed.Tokens);
        Assert.Equal(0, listed.CostUsd);
        Assert.False(listed.CostIsEstimate);
    }

    [Fact]
    public void A_list_price_is_not_an_estimate()
    {
        var listed = TestAgentOverview.Recent(Run(TestGenRunState.Accepted, "listed:cloud", 1_000, 0.0021), Now, Allowlist);

        Assert.False(listed.CostIsEstimate);
    }

    [Fact]
    public void A_model_no_longer_on_the_allowlist_reads_as_an_estimate()
    {
        var listed = TestAgentOverview.Recent(Run(TestGenRunState.Accepted, "retired:cloud", 1_000, 0.0021), Now, Allowlist);

        Assert.True(listed.CostIsEstimate);
        Assert.Equal(0.0021, listed.CostUsd);
    }

    [Fact]
    public void The_recorded_cost_is_kept_when_the_price_changes_later()
    {
        // 1M input tokens at $0.29 when the run started; the allowlist now says $0.60.
        var listed = TestAgentOverview.Recent(Run(TestGenRunState.Accepted, tokens: 1_000_000, cost: 0.29), Now, Allowlist);

        Assert.Equal(0.29, listed.CostUsd);
    }
}
