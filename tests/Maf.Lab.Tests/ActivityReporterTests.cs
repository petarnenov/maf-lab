using System.Text;
using Maf.Lab.TestAgent;
using Maf.Lab.TestGen;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maf.Lab.Tests;

/// <summary>How the agent reports what it is doing (test-generation-agent: Activity reporting).</summary>
public sealed class ActivityReporterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (ActivityReporter Reporter, List<TestGenActivity> Sent, ManualTime Time) Reporter()
    {
        var sent = new List<TestGenActivity>();
        var time = new ManualTime();
        return (new ActivityReporter((entries, _) =>
        {
            sent.AddRange(entries);
            return Task.CompletedTask;
        }, time, NullLogger.Instance), sent, time);
    }

    [Fact]
    public async Task A_resumed_task_numbers_on_from_where_it_stopped()
    {
        var sent = new List<TestGenActivity>();
        var reporter = new ActivityReporter((entries, _) =>
        {
            sent.AddRange(entries);
            return Task.CompletedTask;
        }, new ManualTime(), NullLogger.Instance, startAfter: 41) { Attempt = 3 };

        await reporter.ResumedAsync(Ct);
        await reporter.PhaseAsync(AttemptPhase.Generating, Ct);

        Assert.Equal([42L, 43L], sent.Select(e => e.Seq));
        Assert.Equal(ActivityType.Resumed, sent[0].Type);
        Assert.Equal(3, sent[0].Attempt);
        Assert.Equal(43, reporter.Seq);
    }

    [Fact]
    public async Task Entries_are_numbered_in_order_under_their_attempt()
    {
        var (reporter, sent, _) = Reporter();
        reporter.Attempt = 2;

        await reporter.PhaseAsync(AttemptPhase.Generating, Ct);
        await reporter.ToolAsync(new ToolActivity("read_file", "src/Lab/Calc.cs", ToolOutcome.Ok, "6 lines"), Ct);
        await reporter.AttemptAsync(new AttemptActivity(40, 70, "ok", new TestCounts(3, 0, 0),
            Enumerable.Range(1, 15).Select(i => $"error {i}").ToList(), 0), Ct);

        Assert.Equal([1L, 2L, 3L], sent.Select(e => e.Seq));
        Assert.All(sent, e => Assert.Equal(2, e.Attempt));
        Assert.Equal([ActivityType.Phase, ActivityType.Tool, ActivityType.Attempt], sent.Select(e => e.Type));
        Assert.Equal(TestGenActivity.MaxErrors, sent[2].Result!.Errors.Count);
    }

    [Fact]
    public async Task A_reply_that_streams_for_a_while_goes_out_as_chunks_of_one_entry()
    {
        var (reporter, sent, time) = Reporter();

        // Eight seconds of words, one every half second: a chunk at least every two seconds.
        for (var i = 0; i < 16; i++)
        {
            await reporter.TextAsync(ActivityType.Text, $"word{i} ", Ct);
            time.Now += TimeSpan.FromMilliseconds(500);
        }
        await reporter.EndTextAsync(Ct);

        Assert.True(sent.Count >= 3, $"{sent.Count} chunk(s)");
        var root = sent[0].Seq;
        Assert.Null(sent[0].Continues);
        Assert.All(sent.Skip(1), e => Assert.Equal(root, e.Continues));
        Assert.Equal(string.Concat(Enumerable.Range(0, 16).Select(i => $"word{i} ")), string.Concat(sent.Select(e => e.Text)));
    }

    [Fact]
    public async Task Text_after_a_tool_call_is_a_new_entry_and_reasoning_is_its_own()
    {
        var (reporter, sent, _) = Reporter();

        await reporter.TextAsync(ActivityType.Reasoning, "Which lines are uncovered?", Ct);
        await reporter.TextAsync(ActivityType.Text, "I will read the file.", Ct);
        await reporter.ToolAsync(new ToolActivity("read_file", "src/Lab/Calc.cs", ToolOutcome.Ok, "6 lines"), Ct);
        await reporter.TextAsync(ActivityType.Text, "Now the test.", Ct);
        await reporter.EndTextAsync(Ct);

        // What was buffered goes out before the tool call that ended the reply.
        Assert.Equal([ActivityType.Text, ActivityType.Reasoning, ActivityType.Tool, ActivityType.Text], sent.Select(e => e.Type));
        Assert.All(sent.Where(e => e.Type is ActivityType.Text or ActivityType.Reasoning), e => Assert.Null(e.Continues));
        Assert.Equal("Now the test.", sent[^1].Text);
    }

    [Fact]
    public async Task A_long_reply_is_cut_at_the_cap_and_marked_truncated()
    {
        var (reporter, sent, _) = Reporter();

        for (var i = 0; i < 10; i++)
        {
            await reporter.TextAsync(ActivityType.Text, new string('x', 1000), Ct);
        }
        await reporter.EndTextAsync(Ct);

        Assert.Equal(TestGenActivity.MaxTextBytes, sent.Sum(e => Encoding.UTF8.GetByteCount(e.Text!)));
        Assert.True(sent[^1].Truncated);
    }

    [Fact]
    public async Task An_entry_that_cannot_be_sent_does_not_stop_the_ones_after_it()
    {
        var attempts = 0;
        var reporter = new ActivityReporter((_, _) =>
        {
            attempts++;
            throw new HttpRequestException("queue closed");
        }, TimeProvider.System, NullLogger.Instance);

        await reporter.PhaseAsync(AttemptPhase.Generating, Ct);
        await reporter.PhaseAsync(AttemptPhase.Building, Ct);

        Assert.Equal((2, 2), (attempts, reporter.Failures));
    }
}
