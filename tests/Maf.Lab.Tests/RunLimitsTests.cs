using Maf.Lab.TestGen;

namespace Maf.Lab.Tests;

/// <summary>A run's limits and its cost estimate (show-run-limits-in-picker).</summary>
public sealed class RunLimitsTests
{
    private static TestGenRequest Request(int attempts = 10, int? rounds = null, int? testRuns = null) =>
        new(TestGenKinds.Request, "r_1", new string('a', 40), "src/Lab/Calc.cs", "dotnet", 85, attempts, "glm-5.3:cloud",
            new ModelPrice(0.6, 2.2), null, rounds, testRuns);

    [Fact]
    public void The_defaults_are_the_maxima()
    {
        Assert.Equal((1, 10, 10), (RunLimits.Attempts.Min, RunLimits.Attempts.Max, RunLimits.Attempts.Default));
        Assert.Equal((1, 40, 40), (RunLimits.ToolRoundsPerAttempt.Min, RunLimits.ToolRoundsPerAttempt.Max, RunLimits.ToolRoundsPerAttempt.Default));
        Assert.Equal((0, 2, 2), (RunLimits.TestRunsPerAttempt.Min, RunLimits.TestRunsPerAttempt.Max, RunLimits.TestRunsPerAttempt.Default));
        Assert.Equal(TestGenRequest.AttemptLimit, RunLimits.Attempts.Max);
    }

    [Fact]
    public void A_task_without_limits_takes_the_defaults()
    {
        var request = Request();

        Assert.Null(request.Problem());
        Assert.Equal((40, 2), (request.ToolRounds, request.TestRuns));
    }

    [Theory]
    [InlineData(10, 20, 0)]
    [InlineData(1, 1, 2)]
    public void Limits_within_bounds_are_accepted(int attempts, int rounds, int testRuns)
    {
        Assert.Null(Request(attempts, rounds, testRuns).Problem());
    }

    [Theory]
    [InlineData(11, null, null, "maxAttempts must be from 1 to 10.")]
    [InlineData(0, null, null, "maxAttempts must be from 1 to 10.")]
    [InlineData(10, 41, null, "toolRoundsPerAttempt must be from 1 to 40.")]
    [InlineData(10, 0, null, "toolRoundsPerAttempt must be from 1 to 40.")]
    [InlineData(10, null, 3, "testRunsPerAttempt must be from 0 to 2.")]
    public void Limits_out_of_bounds_are_rejected(int attempts, int? rounds, int? testRuns, string problem)
    {
        Assert.Equal(problem, Request(attempts, rounds, testRuns).Problem());
    }

    [Fact]
    public void The_estimate_matches_measured_runs()
    {
        // The example the web's limits.test.ts pins too: a 3.7 KB file on glm-5.3:cloud with the default limits. Measured
        // attempts on this file cost $0.076–0.10 each.
        var (input, output) = AttemptEstimate.PerAttempt(3_745, RunLimits.ToolRoundsPerAttempt.Default);

        Assert.Equal((126_748L, 6_000L), (input, output));
        Assert.Equal(0.0892, AttemptEstimate.Cost(input, output, 0.6, 2.2));
    }

    [Fact]
    public void Fewer_rounds_than_typical_lower_the_estimate_and_more_do_not_raise_it()
    {
        var typical = AttemptEstimate.PerAttempt(3_745, AttemptEstimate.TypicalRounds).Input;

        Assert.True(AttemptEstimate.PerAttempt(3_745, 10).Input < typical);
        Assert.Equal(typical, AttemptEstimate.PerAttempt(3_745, 40).Input);
    }
}
