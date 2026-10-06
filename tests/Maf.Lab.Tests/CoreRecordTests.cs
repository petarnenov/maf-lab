using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Domain.History;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

/// <summary>
/// The turn's core record (introduce-plugins 5.3): what the core keeps with a turn whatever plugin is installed — and,
/// with no observer at all, that nothing else is written: no live trace, no model capture.
/// </summary>
public sealed class CoreRecordTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_core_only_turn_writes_no_live_trace_and_keeps_its_core_record()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { ObserveTurns = false };
        var adam = api.ClientFor("adam", "firm-a", Role.USER);

        var events = await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");
        var runId = events[^1].Data.GetProperty("runId").GetString()!;

        Assert.False(FakeRunTraceStore.All.ContainsKey(runId));
        Assert.False(TestTraceCapture.All.ContainsKey(runId));
        var record = api.RecordOf(runId);
        Assert.NotEmpty(record);
        Assert.All(record, e => Assert.Contains(e.Kind, TurnRecord.Kinds));
        Assert.Contains(record, e => e.Kind == TraceKinds.Intent);
        Assert.Contains(record, e => e.Kind == TraceKinds.Envelope);
        var end = Assert.Single(record, e => e.Kind == TraceKinds.TurnEnd);
        // The statistics read the model calls here, the model capture being nobody's.
        Assert.True(end.Data.GetProperty("modelCalls").GetInt32() >= 1);
    }

    [Fact]
    public async Task The_reasoning_is_kept_with_the_turn_and_comes_back_with_its_conversation()
    {
        var model = new ScriptedChatClient((messages, _, _) =>
            [.. ScriptedChatClient.Thinking("THOUGHT-KEPT weighing the procedure."), .. ScriptedChatClient.Text("Re-run the batch.")]);
        using var api = new ApiFactory(model) { ObserveTurns = false };
        var adam = api.ClientFor("adam", "firm-a", Role.USER);

        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var detail = await adam.GetFromJsonAsync<ConversationDetail>($"/api/conversations/{done.GetProperty("threadId").GetString()}", Json, Ct);

        var turn = Assert.Single(detail!.Turns);
        Assert.Equal("THOUGHT-KEPT weighing the procedure.", turn.Reasoning);
        Assert.NotNull(turn.ReasoningMs);
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains("THOUGHT-KEPT"));
    }
}
