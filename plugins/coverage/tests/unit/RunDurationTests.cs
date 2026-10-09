using System.Text.Json;
using Maf.Lab.Plugins.Coverage;
using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

/// <summary>
/// When a run's work ended, and how long it took as the agents page lists it (show-test-run-duration): the work time,
/// from start to the first state that is not running, never the wait for a person's decision.
/// </summary>
public sealed class RunDurationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTime Ten = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    private static TestGenRunRow Run(string id, string state, DateTime? finishedAt = null)
    {
        var row = CoverageStorageTests.Run(id, $"src/{id}.cs", state);
        row.CreatedAt = Ten;
        row.UpdatedAt = Ten;
        row.FinishedAt = finishedAt;
        return row;
    }

    [Theory]
    [InlineData(TestGenRunState.Candidate)]
    [InlineData(TestGenRunState.Failed)]
    [InlineData(TestGenRunState.Canceled)]
    [InlineData(TestGenRunState.CompletedNoChange)]
    [InlineData(TestGenRunState.VerificationFailed)]
    public void Leaving_the_running_states_stamps_the_end(string state)
    {
        var run = Run("r_1", state);

        TestGenRuns.StampFinish(run, Ten.AddMinutes(7));

        Assert.Equal(Ten.AddMinutes(7), run.FinishedAt);
    }

    [Theory]
    [InlineData(TestGenRunState.Submitted)]
    [InlineData(TestGenRunState.Working)]
    [InlineData(TestGenRunState.Verifying)]
    public void A_running_run_has_no_end(string state)
    {
        var run = Run("r_1", state);

        TestGenRuns.StampFinish(run, Ten.AddMinutes(7));

        Assert.Null(run.FinishedAt);
    }

    [Fact]
    public void A_candidate_accepted_later_keeps_its_candidate_time()
    {
        var run = Run("r_1", TestGenRunState.Candidate);
        TestGenRuns.StampFinish(run, Ten.AddMinutes(7));

        run.State = TestGenRunState.Accepted;
        TestGenRuns.StampFinish(run, Ten.AddMinutes(90));

        Assert.Equal(Ten.AddMinutes(7), run.FinishedAt);
    }

    [Fact]
    public void A_finished_run_lasts_from_its_start_to_its_end_whatever_happened_after()
    {
        var run = Run("r_1", TestGenRunState.Accepted, finishedAt: Ten.AddMinutes(7));
        run.UpdatedAt = Ten.AddMinutes(90);

        var listed = TestAgentOverview.Recent(run, new DateTimeOffset(Ten.AddHours(5)), []);

        Assert.Equal(new DateTimeOffset(Ten), listed.StartedAt);
        Assert.Equal(new DateTimeOffset(Ten.AddMinutes(7)), listed.FinishedAt);
        Assert.Equal(7 * 60_000L, listed.DurationMs);
    }

    [Fact]
    public void A_running_run_lasts_until_now()
    {
        var listed = TestAgentOverview.Recent(Run("r_1", TestGenRunState.Working), new DateTimeOffset(Ten.AddSeconds(125)), []);

        Assert.Null(listed.FinishedAt);
        Assert.Equal(125_000L, listed.DurationMs);
    }

    [Fact]
    public void A_stopped_run_without_an_end_has_no_duration()
    {
        var listed = TestAgentOverview.Recent(Run("r_1", TestGenRunState.Failed), new DateTimeOffset(Ten.AddHours(1)), []);

        Assert.Null(listed.FinishedAt);
        Assert.Null(listed.DurationMs);
    }

    [Fact]
    public void A_clock_that_moved_back_never_gives_a_negative_duration()
    {
        var listed = TestAgentOverview.Recent(Run("r_1", TestGenRunState.Working), new DateTimeOffset(Ten.AddSeconds(-3)), []);

        Assert.Equal(0L, listed.DurationMs);
    }

    [Fact]
    public async Task Runs_stored_before_ends_were_recorded_get_one_from_their_updates()
    {
        var factory = await CoverageStorageTests.NewDatabaseAsync();
        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            var accepted = Run("r_acc", TestGenRunState.Accepted);
            accepted.UpdatedAt = Ten.AddMinutes(90);
            var noEvents = Run("r_old", TestGenRunState.Failed);
            noEvents.UpdatedAt = Ten.AddMinutes(3);
            var working = Run("r_work", TestGenRunState.Working);
            var known = Run("r_known", TestGenRunState.Discarded, finishedAt: Ten.AddMinutes(1));
            ctx.Set<TestGenRunRow>().AddRange(accepted, noEvents, working, known);
            ctx.Set<TestGenRunEventRow>().AddRange(
                Event("r_acc", 1, TestGenRunState.Working, Ten.AddMinutes(1)),
                Event("r_acc", 2, TestGenRunState.Verifying, Ten.AddMinutes(6)),
                Event("r_acc", 3, TestGenRunState.Candidate, Ten.AddMinutes(7)),
                Event("r_acc", 4, TestGenRunState.Accepted, Ten.AddMinutes(90)),
                Event("r_work", 1, TestGenRunState.Working, Ten.AddMinutes(1)),
                Event("r_known", 1, TestGenRunState.Candidate, Ten.AddMinutes(5)));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = await factory.CreateDbContextAsync(Ct))
        {
            await DatabaseInitializer.InitializeAsync((MafDbContext)ctx, Ct);
        }

        await using var check = await factory.CreateDbContextAsync(Ct);
        var ends = await check.Set<TestGenRunRow>().AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.FinishedAt, Ct);
        Assert.Equal(Ten.AddMinutes(7), ends["r_acc"]);
        Assert.Equal(Ten.AddMinutes(3), ends["r_old"]);
        Assert.Null(ends["r_work"]);
        Assert.Equal(Ten.AddMinutes(1), ends["r_known"]);
    }

    private static TestGenRunEventRow Event(string runId, int seq, string state, DateTime at) => new()
    {
        RunId = runId,
        Seq = seq,
        At = at,
        Json = JsonSerializer.Serialize(new { id = runId, state }, Json),
    };
}
