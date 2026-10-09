extern alias service;
using Maf.Lab.Plugins.Coverage;
using service::Maf.Lab.TestAgent;
using Maf.Lab.TestGen;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

/// <summary>A run's caps are optional; an unset one never stops the run (explain-run-outcome).</summary>
public sealed class TestGenBudgetTests
{
    private static TestGenRequest Request(TestGenBudget? budget) =>
        new(TestGenKinds.Request, "r_1", new string('a', 40), "src/Lab/Calc.cs", "dotnet", 85, 5, "glm-5.3:cloud",
            new ModelPrice(0.6, 2.2), budget);

    [Fact]
    public void A_task_without_caps_is_valid()
    {
        Assert.Null(Request(TestGenBudget.Unlimited).Problem());
        Assert.Null(Request(null).Problem());
    }

    [Fact]
    public void A_task_with_only_a_cost_cap_is_valid()
    {
        Assert.Null(Request(new TestGenBudget(MaxCostUsd: 0.5)).Problem());
    }

    [Theory]
    [InlineData(0L, null)]
    [InlineData(-1L, null)]
    [InlineData(null, 0.0)]
    [InlineData(null, -0.5)]
    public void A_cap_that_is_not_positive_is_rejected(long? tokens, double? cost)
    {
        Assert.NotNull(Request(new TestGenBudget(tokens, cost)).Problem());
    }

    [Fact]
    public void Usage_without_caps_is_never_spent()
    {
        var usage = new RunUsage(TestGenBudget.Unlimited, new ModelPrice(0.6, 2.2));
        usage.Add(new UsageDetails { InputTokenCount = 9_000_000, OutputTokenCount = 1_000_000 });

        Assert.False(usage.Spent);
        Assert.False(usage.WouldExceed(5_000_000, 1_000_000));
    }

    [Fact]
    public void Usage_is_held_to_the_cap_that_is_set()
    {
        var usage = new RunUsage(new TestGenBudget(MaxCostUsd: 1.0), new ModelPrice(0.6, 2.2));
        usage.Add(new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 0 });

        Assert.False(usage.Spent);
        Assert.True(usage.WouldExceed(1_000_000, 0));
    }
}
