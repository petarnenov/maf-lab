using Maf.Lab.TestAgent;
using Maf.Lab.TestGen;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

/// <summary>The agent writes within its tool-round cap: it is told the cap, nudged near it, and told when it wrote nothing (explain-run-outcome).</summary>
public sealed class TestAgentRoundsTests
{
    private sealed class Recorder : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static readonly TestGenRequest Request = new(TestGenKinds.Request, "r_1", new string('a', 40), "src/Lab/Calc.cs", "dotnet",
        85, 5, "glm-5.3:cloud", new ModelPrice(0.6, 2.2), TestGenBudget.Unlimited);

    [Fact]
    public void The_attempt_states_its_round_cap()
    {
        Assert.Contains("You have 12 tool rounds in this attempt", Instructions.Attempt(Request with { ToolRoundsPerAttempt = 12 }, 1, 40, null));
    }

    [Fact]
    public void A_task_without_limits_has_the_defaults()
    {
        var input = Instructions.Attempt(Request, 1, 40, null);

        Assert.Contains("You have 40 tool rounds in this attempt", input);
        Assert.Contains("run_tests at most 2 times", input);
        Assert.Contains("at most 3 suspected bugs", input);
    }

    [Fact]
    public void A_task_without_bug_reports_is_told_so()
    {
        Assert.Contains("This run reports no suspected bugs", Instructions.Attempt(Request with { MaxSuspectedBugs = 0 }, 1, 40, null));
    }

    [Fact]
    public void A_task_without_test_runs_is_told_not_to_run_them()
    {
        Assert.Contains("Do not call run_tests", Instructions.Attempt(Request with { TestRunsPerAttempt = 0 }, 1, 40, null));
    }

    [Fact]
    public void The_rules_name_the_substitution_library_and_forbid_new_packages()
    {
        Assert.Contains("Substitute.For<IDatabase>()", Instructions.System);
        Assert.Contains("never add a package", Instructions.System);
    }

    [Fact]
    public void An_attempt_that_wrote_nothing_is_fed_back()
    {
        var result = FakeCoverageRunner.Result("<coverage/>", targetPct: 0) with { Uncovered = [[11, 37]] };

        var feedback = Instructions.Feedback(result, [], wroteNothing: 1);

        Assert.Contains("Attempt 1 wrote no test file", feedback);
        Assert.Contains("Lines still uncovered: 11-37", feedback);
        Assert.DoesNotContain("wrote no test", Instructions.Feedback(result, []));
    }

    [Fact]
    public async Task The_model_is_nudged_once_when_three_rounds_remain()
    {
        var inner = new Recorder();
        var nudge = new RoundNudgeChatClient(inner, roundCap: 12);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 12; i++)
        {
            await nudge.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: ct);
        }

        var nudged = inner.Calls.Select((m, i) => (Call: i + 1, Nudged: m.Any(x => x.Text == Instructions.Nudge(3)))).Where(c => c.Nudged);
        Assert.Equal([10], nudged.Select(c => c.Call));
    }

    [Fact]
    public async Task A_new_attempt_counts_its_rounds_from_zero()
    {
        var inner = new Recorder();
        var nudge = new RoundNudgeChatClient(inner, roundCap: 4);
        var ct = TestContext.Current.CancellationToken;

        await nudge.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: ct);
        await nudge.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: ct);
        nudge.BeginAttempt();
        await nudge.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: ct);
        await nudge.GetResponseAsync([new ChatMessage(ChatRole.User, "go")], cancellationToken: ct);

        // With a cap of 4, the second call of each attempt is the one with 3 rounds left.
        Assert.Equal([false, true, false, true], inner.Calls.Select(m => m.Count == 2));
    }
}
