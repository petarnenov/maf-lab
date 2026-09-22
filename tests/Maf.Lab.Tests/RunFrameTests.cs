using System.Net.Http.Json;
using System.Text.Json;
using AGUI.Abstractions;
using Maf.Lab.Api.Agent.Streaming;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>What crossed the AG-UI wire for a turn: recorded as it went out, kept with the turn's trace.</summary>
public class RunFrameTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static TraceEvent Trace(int seq, string kind, object data) => new(
        seq, seq * 10, kind, kind, null, JsonSerializer.SerializeToElement(data, Json), false);

    [Fact]
    public void Recorder_numbers_frames_in_order_and_keeps_a_trace_event_by_reference()
    {
        var recorder = new RunFrameRecorder();
        recorder.Add(new RunStartedEvent { ThreadId = "c1", RunId = "r1" });
        recorder.Add(AGUIStream.Trace(Trace(1, TraceKinds.TurnStart, new { turnId = "t_1", question = "q" })));
        recorder.Add(new TextMessageContentEvent { MessageId = "m1", Delta = "hello" });
        recorder.Add(AGUIStream.Trace(Trace(2, TraceKinds.Intent, new { intent = "Procedural" })));

        Assert.Equal([1, 2, 3, 4], recorder.Frames.Select(f => f.Seq));
        Assert.Equal(
            ["RUN_STARTED", "CUSTOM", "TEXT_MESSAGE_CONTENT", "CUSTOM"],
            recorder.Frames.Select(f => f.Type));

        // A trace frame points at the trace event; the event's own data is not copied into it.
        var traceFrames = recorder.Frames.Where(f => f.Name == AGUIStream.TraceEvent).ToList();
        Assert.Equal([1, 2], traceFrames.Select(f => f.TraceSeq!.Value));
        Assert.All(traceFrames, f => Assert.Null(f.Payload));
        Assert.DoesNotContain("Procedural", JsonSerializer.Serialize(recorder.Frames, Json));

        // Everything else keeps what it carried, and its size on the wire.
        var delta = recorder.Frames[2];
        Assert.Equal("hello", delta.Payload!.Value.GetProperty("delta").GetString());
        Assert.True(delta.Bytes > 0);

        // The turn the run recorded, learnt from its first trace event, is where the frames will be stored.
        Assert.Equal("t_1", recorder.TurnId);
    }

    [Fact]
    public void Recorder_caps_the_run_and_marks_what_it_left_without_a_payload()
    {
        var recorder = new RunFrameRecorder();
        for (var i = 0; i < 40; i++)
        {
            recorder.Add(new TextMessageContentEvent { MessageId = "m1", Delta = new string('x', 10_000) });
        }

        Assert.Equal(40, recorder.Frames.Count);
        var kept = recorder.Frames.Where(f => !f.Truncated).ToList();
        var dropped = recorder.Frames.Where(f => f.Truncated).ToList();
        Assert.NotEmpty(kept);
        Assert.NotEmpty(dropped);
        // A frame past the cap still says where it was, when, what type and how big — only its payload is gone.
        Assert.All(dropped, f =>
        {
            Assert.Null(f.Payload);
            Assert.True(f.Bytes > 10_000);
            Assert.True(f.AtMs >= 0);
        });
        Assert.True(JsonSerializer.Serialize(recorder.Frames, Json).Length < RunFrameRecorder.MaxBytes * 2);
    }

    [Fact]
    public async Task A_turn_keeps_every_frame_its_run_streamed_and_serves_them_with_its_trace()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-FRAMES-9."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var events = await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");
        var turnId = events[^1].Data.GetProperty("result").GetProperty("turnId").GetString()!;

        var doc = await adam.GetFromJsonAsync<TurnTraceDocument>($"/api/turns/{turnId}/trace", Json, Ct);
        var frames = doc!.AguiFrames;
        Assert.NotNull(frames);

        // Every event the stream produced, in the order it produced them, terminal event included.
        Assert.Equal(events.Select(e => e.Name), frames!.Select(f => f.Type));
        Assert.Equal(Enumerable.Range(1, events.Count), frames.Select(f => f.Seq));
        Assert.Equal("RUN_FINISHED", frames[^1].Type);
        Assert.Contains(frames, f => f.Type == "TEXT_MESSAGE_START");
        Assert.Contains(frames, f => f.Type == "TOOL_CALL_END");

        // One frame per trace event, carrying its sequence number instead of a second copy of it.
        var traceFrames = frames.Where(f => f.Name == AGUIStream.TraceEvent).ToList();
        Assert.Equal(doc.Events.Select(e => (int?)e.Seq), traceFrames.Select(f => f.TraceSeq));
        Assert.All(traceFrames, f => Assert.Null(f.Payload));

        // Recorded, never logged.
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains("ANSWER-FRAMES-9"));
    }

    [Fact]
    public async Task A_turn_recorded_before_the_frames_were_kept_reports_none_rather_than_an_empty_run()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var turnId = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1]
            .Data.GetProperty("result").GetProperty("turnId").GetString()!;

        await using (var ctx = ChatApiTests.Db(api))
        {
            await ctx.TurnTraces.Where(t => t.TurnId == turnId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AguiJson, (string?)null), Ct);
        }

        var doc = await adam.GetFromJsonAsync<TurnTraceDocument>($"/api/turns/{turnId}/trace", Json, Ct);
        Assert.Null(doc!.AguiFrames);
        Assert.NotEmpty(doc.Events);
    }

    [Fact]
    public async Task An_answer_to_a_confirmation_records_no_turn_and_so_stores_no_frames()
    {
        var model = new ScriptedChatClient((messages, options, _) =>
        {
            var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            return last.Contains("adjust", StringComparison.OrdinalIgnoreCase)
                && !ScriptedChatClient.HasResult(messages, FeeAdjustmentTool.Name)
                ? ScriptedChatClient.Call(FeeAdjustmentTool.Name, new()
                {
                    ["accountId"] = "A-1042",
                    ["amount"] = -200m,
                    ["reason"] = "the client was overcharged in Q2",
                })
                : ScriptedChatClient.Text("Done.");
        });
        using var api = new ApiFactory(model, new FakeToolSource())
        {
            ExtraSettings = new Dictionary<string, string?> { ["Compliance:BaseUrl"] = "" },
        };
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);

        var proposed = await ApiFactory.ChatAsync(adam, "adjust the fee on A-1042 down by 200");
        var interrupt = ApiFactory.InterruptOf(proposed);
        Assert.NotNull(interrupt);
        var turnId = proposed[^1].Data.GetProperty("result").GetProperty("turnId").GetString()!;

        string? before;
        await using (var ctx = ChatApiTests.Db(api))
        {
            before = (await ctx.TurnTraces.SingleAsync(t => t.TurnId == turnId, Ct)).AguiJson;
        }
        Assert.NotNull(before);

        await ApiFactory.ResumeAsync(adam, ApiFactory.ThreadOf(proposed), interrupt.Value.GetProperty("id").GetString()!, approve: true);

        // The answer is a run of its own. It records no turn, so its frames have nowhere to go — and the
        // proposing turn's frames are left exactly as its own run wrote them.
        await using var check = ChatApiTests.Db(api);
        Assert.Equal(1, await check.TurnTraces.CountAsync(t => t.AguiJson != null, Ct));
        Assert.Equal(before, (await check.TurnTraces.SingleAsync(t => t.TurnId == turnId, Ct)).AguiJson);
    }

    [Fact]
    public async Task Retention_takes_the_frames_with_the_trace()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var turnId = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1]
            .Data.GetProperty("result").GetProperty("turnId").GetString()!;

        await using (var ctx = ChatApiTests.Db(api))
        {
            Assert.NotNull((await ctx.TurnTraces.SingleAsync(t => t.TurnId == turnId, Ct)).AguiJson);
            await ctx.TurnTraces.Where(t => t.TurnId == turnId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddDays(-8)), Ct);
        }

        Assert.Equal(1, await api.Services.GetRequiredService<TraceRetentionService>().PurgeAsync(Ct));
        await using var check = ChatApiTests.Db(api);
        Assert.False(await check.TurnTraces.AnyAsync(t => t.TurnId == turnId, Ct));
    }
}
