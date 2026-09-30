using System.Text.Json;
using Maf.Lab.TestGen;

namespace Maf.Lab.Tests;

/// <summary>The activity entry the agent sends and the api reads (add-run-activity-view).</summary>
public sealed class TestGenActivityContractTests
{
    public static TheoryData<TestGenActivity> Entries => new()
    {
        new TestGenActivity(TestGenKinds.Activity, 1, DateTimeOffset.UnixEpoch, 1, ActivityType.Phase, Phase: AttemptPhase.Generating),
        new TestGenActivity(TestGenKinds.Activity, 2, DateTimeOffset.UnixEpoch, 1, ActivityType.Tool,
            Tool: new ToolActivity("run_tests", null, ToolOutcome.Ok, "build ok, 12 passed, 1 failed, 72.1%")),
        new TestGenActivity(TestGenKinds.Activity, 3, DateTimeOffset.UnixEpoch, 1, ActivityType.Attempt,
            Result: new AttemptActivity(69.2, 72.1, "ok", new TestCounts(12, 1, 0), ["T.Fails: expected 1"], 0)),
        new TestGenActivity(TestGenKinds.Activity, 4, DateTimeOffset.UnixEpoch, 2, ActivityType.Text, Text: "Looking at "),
        new TestGenActivity(TestGenKinds.Activity, 5, DateTimeOffset.UnixEpoch, 2, ActivityType.Text, Continues: 4, Text: "the file.",
            Truncated: true),
        new TestGenActivity(TestGenKinds.Activity, 6, DateTimeOffset.UnixEpoch, 2, ActivityType.Stopped,
            Stop: new StoppedActivity(StopReason.Budget, 2, 0, NotStarted: 3)),
    };

    [Theory]
    [MemberData(nameof(Entries))]
    public void An_entry_survives_the_wire(TestGenActivity entry)
    {
        var wire = JsonSerializer.SerializeToElement(entry, TestGenKinds.Json);
        var back = wire.Deserialize<TestGenActivity>(TestGenKinds.Json)!;

        Assert.Equal(TestGenKinds.Activity, wire.GetProperty("kind").GetString());
        Assert.Equal(JsonSerializer.Serialize(entry, TestGenKinds.Json), JsonSerializer.Serialize(back, TestGenKinds.Json));
    }

    [Fact]
    public void Absent_parts_are_left_out()
    {
        var wire = JsonSerializer.SerializeToElement(
            new TestGenActivity(TestGenKinds.Activity, 1, DateTimeOffset.UnixEpoch, 1, ActivityType.Phase, Phase: "building"),
            TestGenKinds.Json);

        Assert.False(wire.TryGetProperty("tool", out _));
        Assert.False(wire.TryGetProperty("text", out _));
    }
}
