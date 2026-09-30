using Maf.Lab.Hosting.Cli;
using Maf.Lab.Indexing.Pipeline;

namespace Maf.Lab.Tests;

public class ConsoleProgressTests
{
    [Fact]
    public void Determinate_bar_shows_done_of_total_and_a_percentage()
    {
        var (bar, _, _) = Plain();
        bar.SetTotal(8);
        bar.Advance(2);

        Assert.Contains("[#######---------------------] 2/8 25%", bar.Render());
    }

    [Fact]
    public void Indeterminate_bar_shows_the_step_and_elapsed_time()
    {
        var (bar, _, time) = Plain();
        bar.Step("reading and chunking the corpus");
        time.Advance(TimeSpan.FromSeconds(3));

        var line = bar.Render();
        Assert.Contains("<=>", line);
        Assert.Contains("3.0s · reading and chunking the corpus", line);
        Assert.DoesNotContain("%", line);
    }

    [Fact]
    public void Plain_output_has_no_control_characters_and_at_most_one_line_every_five_seconds()
    {
        var (bar, output, time) = Plain();
        bar.SetTotal(100);
        for (var i = 0; i < 100; i++)
        {
            bar.Advance();
            time.Advance(TimeSpan.FromMilliseconds(100));
        }
        bar.Succeed();

        var text = output.ToString();
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain('\u001b', text);
        // 10 s of work: the first line, one after 5 s and the final line.
        Assert.Equal(3, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void A_slow_item_still_moves_the_terminal_line_on_every_tick()
    {
        var output = new StringWriter();
        var time = new ManualTime();
        var bar = new ConsoleProgress("index", output, interactive: true, time, heartbeat: false);
        bar.SetTotal(3);
        bar.Working("shared/src/Big.cs");
        var before = output.ToString();

        time.Advance(TimeSpan.FromSeconds(1));
        bar.Tick();

        Assert.NotEqual(before, output.ToString());
        Assert.Contains("1.0s · shared/src/Big.cs", output.ToString());
    }

    [Theory]
    [InlineData("succeed", "✓ index maf_chunks: done 3/5 in 2.0s — ok")]
    [InlineData("fail", "✗ index maf_chunks: failed 3/5 in 2.0s — RpcException")]
    [InlineData("cancel", "✗ index maf_chunks: cancelled 3/5 in 2.0s")]
    public void The_work_ends_in_one_line_that_says_how(string outcome, string expected)
    {
        var (bar, output, time) = Plain("index maf_chunks");
        bar.SetTotal(5);
        bar.Advance(3);
        time.Advance(TimeSpan.FromSeconds(2));
        switch (outcome)
        {
            case "succeed": bar.Succeed("ok"); break;
            case "fail": bar.Fail("RpcException"); break;
            default: bar.Cancel(); break;
        }
        bar.Succeed("a second outcome is ignored");

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected, lines[^1]);
        Assert.Single(lines, l => l.StartsWith('✓') || l.StartsWith('✗'));
    }

    [Fact]
    public void Terminal_bar_is_cleared_before_the_final_line()
    {
        var output = new StringWriter();
        var bar = new ConsoleProgress("index", output, interactive: true, new ManualTime(), heartbeat: false);
        bar.SetTotal(1);
        bar.Advance();
        bar.Succeed();

        var text = output.ToString();
        var final = text[(text.LastIndexOf('\r') + 1)..];
        Assert.StartsWith("✓ index: done 1/1", final);
    }

    [Fact]
    public void Pipeline_reports_drive_stage_total_and_count()
    {
        var (bar, _, _) = Plain();
        var progress = new IndexProgressBar(bar);

        progress.Report(new IndexProgress("reading and chunking the corpus", 0, null));
        Assert.Contains("<=>", bar.Render());

        progress.Report(new IndexProgress("indexing", 0, 4));
        progress.Report(new IndexProgress("indexing", 1, 4));
        progress.Report(new IndexProgress("indexing", 1, 4, "shared/docs/fees.md"));
        Assert.Contains("1/4 25%", bar.Render());
        Assert.Contains("shared/docs/fees.md", bar.Render());

        progress.Report(new IndexProgress("indexing", 2, 4));
        Assert.Contains("2/4 50%", bar.Render());
        Assert.DoesNotContain("fees.md", bar.Render());
    }

    private static (ConsoleProgress Bar, StringWriter Output, ManualTime Time) Plain(string label = "index")
    {
        var output = new StringWriter();
        var time = new ManualTime();
        return (new ConsoleProgress(label, output, interactive: false, time, heartbeat: false), output, time);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
