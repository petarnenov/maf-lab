using System.Text.Json;
using AGUI.Abstractions;
using Maf.Lab.Api.Agent.Streaming;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Storage;
using Maf.Lab.TestGen;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

/// <summary>
/// A run's activity record and how it reads as AG-UI (test-generation-runs: Activity record, Progress to the browser).
/// </summary>
public sealed class RunActivityTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _dir = Directory.CreateTempSubdirectory("maf-activity-").FullName;
    private IDbContextFactory<MafDbContext> _db = null!;

    public async ValueTask InitializeAsync()
    {
        _db = new TestDbFactory(Path.Combine(_dir, "a.db"));
        await using var db = await _db.CreateDbContextAsync(Ct);
        await DatabaseInitializer.InitializeAsync(db, Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class TestDbFactory(string path) : IDbContextFactory<MafDbContext>
    {
        public MafDbContext CreateDbContext() => new(new DbContextOptionsBuilder<MafDbContext>()
            .UseSqlite($"Data Source={path}").AddInterceptors(new SqlitePragmaInterceptor()).Options);
    }

    private static TestGenActivity Entry(long seq, string type, int attempt = 1) =>
        new(TestGenKinds.Activity, seq, DateTimeOffset.UnixEpoch.AddSeconds(seq), attempt, type);

    private static TestGenActivity Phase(long seq, string phase, int attempt = 1) => Entry(seq, ActivityType.Phase, attempt) with { Phase = phase };

    private static TestGenActivity Text(long seq, string text, long? continues = null, string type = ActivityType.Text) =>
        Entry(seq, type) with { Text = text, Continues = continues };

    private static TestGenActivity Tool(long seq, string name, string? path, string outcome, string summary) =>
        Entry(seq, ActivityType.Tool) with { Tool = new ToolActivity(name, path, outcome, summary) };

    private async Task<List<TestGenRunActivityRow>> RowsAsync(string runId)
    {
        await using var db = await _db.CreateDbContextAsync(Ct);
        return await db.TestGenRunActivity.AsNoTracking().Where(a => a.RunId == runId).OrderBy(a => a.Seq).ToListAsync(Ct);
    }

    [Fact]
    public async Task A_replayed_update_is_stored_once()
    {
        var store = new RunActivityStore(_db);
        var entries = Enumerable.Range(40, 6).Select(i => Phase(i, AttemptPhase.Building)).ToList();

        Assert.True(await store.AddAsync("r1", entries, Ct));
        Assert.False(await store.AddAsync("r1", entries, Ct));
        // Another replica, its own store over the same database, sees the same replay.
        Assert.False(await new RunActivityStore(_db).AddAsync("r1", entries[2..], Ct));

        Assert.Equal([40L, 41L, 42L, 43L, 44L, 45L], (await RowsAsync("r1")).Select(r => r.Seq));
    }

    [Fact]
    public async Task Chunks_join_their_entry_once_each_and_in_order()
    {
        var store = new RunActivityStore(_db);

        await store.AddAsync("r1", [Text(1, "Looking "), Text(2, "at the ", continues: 1)], Ct);
        await store.AddAsync("r1", [Text(2, "at the ", continues: 1), Text(3, "file.", continues: 1)], Ct);

        var row = Assert.Single(await RowsAsync("r1"));
        Assert.Equal(("Looking at the file.", 3L), (row.Text, row.LastSeq));
    }

    [Fact]
    public async Task Past_the_cap_the_oldest_entries_go_and_the_run_says_so()
    {
        await using (var db = await _db.CreateDbContextAsync(Ct))
        {
            db.TestGenRuns.Add(new TestGenRunRow
            {
                Id = "r1", Path = "src/Lab/Calc.cs", Toolchain = "dotnet", CommitSha = new string('a', 40), Model = "m",
                State = TestGenRunState.Working, CreatedBy = "alice",
            });
            await db.SaveChangesAsync(Ct);
        }
        var store = new RunActivityStore(_db);

        await store.AddAsync("r1", Enumerable.Range(1, RunActivityStore.MaxEntries + 5).Select(i => Phase(i, "building")).ToList(), Ct);

        var rows = await RowsAsync("r1");
        Assert.Equal(RunActivityStore.MaxEntries, rows.Count);
        Assert.Equal(6L, rows[0].Seq);
        await using var check = await _db.CreateDbContextAsync(Ct);
        Assert.True((await check.TestGenRuns.SingleAsync(r => r.Id == "r1", Ct)).ActivityDropped);
    }

    private static RunSummary Summary(string state = TestGenRunState.Working, string? phase = null, string? reason = null) =>
        new("r1", "src/Lab/Calc.cs", state, reason, 1, 5, 40, 85, "m", 0, 0, null, DateTime.UnixEpoch, DateTime.UnixEpoch, phase);

    private async Task<List<BaseEvent>> ProjectAsync(RunActivityProjection view, IReadOnlyList<TestGenActivity> entries)
    {
        await new RunActivityStore(_db).AddAsync("r1", entries, Ct);
        return view.Entries(await new RunActivityStore(_db).ChangedSinceAsync("r1", view.Cursor, Ct), dropped: false).ToList();
    }

    [Fact]
    public async Task A_phase_is_a_step_and_a_new_phase_finishes_the_last()
    {
        var view = new RunActivityProjection("r1");

        var events = await ProjectAsync(view, [Phase(1, AttemptPhase.Generating, 2), Phase(2, AttemptPhase.Building, 2)]);

        Assert.Collection(events,
            e => Assert.Equal("attempt 2: generating", Assert.IsType<StepStartedEvent>(e).StepName),
            e => Assert.Equal("attempt 2: generating", Assert.IsType<StepFinishedEvent>(e).StepName),
            e => Assert.Equal("attempt 2: building", Assert.IsType<StepStartedEvent>(e).StepName));
    }

    [Fact]
    public async Task The_baseline_is_its_own_step()
    {
        var view = new RunActivityProjection("r1");

        var events = await ProjectAsync(view, [Phase(1, AttemptPhase.Measuring, 0)]);

        Assert.Equal("baseline: measuring", Assert.IsType<StepStartedEvent>(Assert.Single(events)).StepName);
    }

    [Fact]
    public void A_change_of_phase_is_a_new_state_snapshot_and_no_change_is_none()
    {
        var view = new RunActivityProjection("r1");

        var first = Assert.IsType<StateSnapshotEvent>(Assert.Single(view.Summary(Summary(phase: AttemptPhase.Generating))));
        Assert.Empty(view.Summary(Summary(phase: AttemptPhase.Generating)));
        var next = Assert.IsType<StateSnapshotEvent>(Assert.Single(view.Summary(Summary(phase: AttemptPhase.Building))));

        Assert.Equal("generating", first.Snapshot.GetProperty("phase").GetString());
        Assert.Equal("building", next.Snapshot.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task A_tool_call_is_the_protocols_four_events_under_one_id()
    {
        var view = new RunActivityProjection("r1");

        var events = await ProjectAsync(view, [Tool(1, "run_tests", null, ToolOutcome.Ok, "build ok, 12 passed, 1 failed, 72.1%")]);

        var start = Assert.IsType<ToolCallStartEvent>(events[0]);
        Assert.Equal("run_tests", start.ToolCallName);
        Assert.IsType<ToolCallArgsEvent>(events[1]);
        Assert.IsType<ToolCallEndEvent>(events[2]);
        var result = Assert.IsType<ToolCallResultEvent>(events[3]);
        Assert.All(new[] { ((ToolCallArgsEvent)events[1]).ToolCallId, ((ToolCallEndEvent)events[2]).ToolCallId, result.ToolCallId },
            id => Assert.Equal(start.ToolCallId, id));
        var content = JsonDocument.Parse(result.Content!.ToString()).RootElement;
        Assert.Equal(("ok", "build ok, 12 passed, 1 failed, 72.1%"),
            (content.GetProperty("outcome").GetString(), content.GetProperty("summary").GetString()));
    }

    [Fact]
    public async Task Text_streams_as_one_message_that_grows_and_ends_when_something_else_happens()
    {
        var view = new RunActivityProjection("r1");

        var first = await ProjectAsync(view, [Text(1, "Looking ")]);
        var more = await ProjectAsync(view, [Text(2, "at it.", continues: 1), Tool(3, "read_file", "src/Lab/Calc.cs", ToolOutcome.Ok, "6 lines")]);

        Assert.IsType<TextMessageStartEvent>(first[0]);
        Assert.Equal("Looking ", Assert.IsType<TextMessageContentEvent>(first[1]).Delta);
        Assert.Equal("at it.", Assert.IsType<TextMessageContentEvent>(more[0]).Delta);
        Assert.IsType<TextMessageEndEvent>(more[1]);
        Assert.IsType<ToolCallStartEvent>(more[2]);
    }

    [Fact]
    public async Task Reasoning_uses_the_protocols_reasoning_events()
    {
        var view = new RunActivityProjection("r1");

        var events = (await ProjectAsync(view, [Text(1, "Which lines?", type: ActivityType.Reasoning)]))
            .Concat(view.Ended(Summary(TestGenRunState.Candidate))).ToList();

        Assert.Collection(events.Take(5),
            e => Assert.IsType<ReasoningStartEvent>(e),
            e => Assert.IsType<ReasoningMessageStartEvent>(e),
            e => Assert.Equal("Which lines?", Assert.IsType<ReasoningMessageContentEvent>(e).Delta),
            e => Assert.IsType<ReasoningMessageEndEvent>(e),
            e => Assert.IsType<ReasoningEndEvent>(e));
    }

    [Fact]
    public async Task An_attempts_result_is_a_custom_event()
    {
        var view = new RunActivityProjection("r1");

        var events = await ProjectAsync(view,
            [Entry(1, ActivityType.Attempt, 2) with { Result = new AttemptActivity(69.2, 72.1, "ok", new TestCounts(12, 1, 0), [], 0) }]);

        var custom = Assert.IsType<CustomEvent>(Assert.Single(events));
        Assert.Equal(RunActivityProjection.AttemptEvent, custom.Name);
        var value = JsonSerializer.SerializeToElement(custom.Value, AGUIStream.Json);
        Assert.Equal((2, 72.1), (value.GetProperty("attempt").GetInt32(), value.GetProperty("after").GetDouble()));
    }

    [Theory]
    [InlineData(TestGenRunState.Failed, "runner_unavailable")]
    [InlineData(TestGenRunState.Canceled, null)]
    public void A_failed_or_canceled_run_ends_in_error_with_its_reason(string state, string? reason)
    {
        var view = new RunActivityProjection("r1");

        var error = Assert.IsType<RunErrorEvent>(view.Ended(Summary(state, reason: reason)).Last());

        Assert.Equal(reason ?? state, error.Code);
    }

    [Fact]
    public void A_candidate_ends_finished_with_its_summary()
    {
        var view = new RunActivityProjection("r1");

        var finished = Assert.IsType<RunFinishedEvent>(view.Ended(Summary(TestGenRunState.Candidate)).Last());

        Assert.Equal(("testgen:r1", "r1"), (finished.ThreadId, finished.RunId));
        Assert.Equal("candidate", JsonSerializer.SerializeToElement(finished.Result, AGUIStream.Json).GetProperty("state").GetString());
    }
}
