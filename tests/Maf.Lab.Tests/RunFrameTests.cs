using System.Net.Http.Json;
using System.Text.Json;
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

    private static JsonElement Event(object e) => JsonSerializer.SerializeToElement(e, Json);

    [Fact]
    public void Recorder_numbers_frames_in_order_and_keeps_what_each_carried()
    {
        var recorder = new RunFrameRecorder();
        recorder.Add(Event(new { type = "RUN_STARTED", threadId = "c1", runId = "r1" }), 50);
        recorder.Add(Event(new { type = "STEP_STARTED", stepName = "screening the question" }), 60);
        recorder.Add(Event(new { type = "TEXT_MESSAGE_CONTENT", messageId = "m1", delta = "hello" }), 70);

        Assert.Equal([1, 2, 3], recorder.Frames.Select(f => f.Seq));
        Assert.Equal(["RUN_STARTED", "STEP_STARTED", "TEXT_MESSAGE_CONTENT"], recorder.Frames.Select(f => f.Type));
        var delta = recorder.Frames[2];
        Assert.Equal("hello", delta.Payload!.Value.GetProperty("delta").GetString());
        Assert.Equal(70, delta.Bytes);
        // A custom event's name and a trace event's sequence belonged to events that no longer exist (agui-protocol-only).
        Assert.All(recorder.Frames, f => { Assert.Null(f.Name); Assert.Null(f.TraceSeq); });
    }

    [Fact]
    public void Recorder_caps_the_run_and_marks_what_it_left_without_a_payload()
    {
        var recorder = new RunFrameRecorder();
        for (var i = 0; i < 40; i++)
        {
            recorder.Add(Event(new { type = "TEXT_MESSAGE_CONTENT", messageId = "m1", delta = new string('x', 10_000) }), 10_050);
        }

        Assert.Equal(40, recorder.Frames.Count);
        var dropped = recorder.Frames.Where(f => f.Truncated).ToList();
        Assert.Contains(recorder.Frames, f => !f.Truncated);
        Assert.NotEmpty(dropped);
        // A frame past the cap still says where it was, when, what type and how big — only its payload is gone.
        Assert.All(dropped, f =>
        {
            Assert.Null(f.Payload);
            Assert.True(f.Bytes > 10_000);
            Assert.Equal("TEXT_MESSAGE_CONTENT", f.Type);
        });
        Assert.True(JsonSerializer.Serialize(recorder.Frames, Json).Length < RunFrameRecorder.MaxBytes * 2);
    }

    [Fact]
    public async Task A_turn_keeps_every_frame_its_run_streamed_and_serves_them_with_its_trace()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel("ANSWER-FRAMES-9."));
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var events = await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing");
        var turnId = events[^1].Data.GetProperty("runId").GetString()!;

        var doc = await adam.GetFromJsonAsync<TurnTraceDocument>($"/api/turns/{turnId}/trace", Json, Ct);
        var frames = doc!.AguiFrames;
        Assert.NotNull(frames);

        // Every event the stream produced, in the order it produced them, terminal event included — and only the
        // protocol's own: no custom event, and no trace on the wire.
        Assert.Equal(events.Select(e => e.Name), frames!.Select(f => f.Type));
        Assert.Equal(Enumerable.Range(1, events.Count), frames.Select(f => f.Seq));
        Assert.Equal("RUN_FINISHED", frames[^1].Type);
        Assert.Contains(frames, f => f.Type == "TEXT_MESSAGE_START");
        Assert.Contains(frames, f => f.Type == "TOOL_CALL_END");
        Assert.Contains(frames, f => f.Type == "STEP_STARTED");
        Assert.DoesNotContain(frames, f => f.Type == "CUSTOM");
        Assert.NotEmpty(doc.Events);

        // Recorded, never logged.
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains("ANSWER-FRAMES-9"));
    }

    [Fact]
    public async Task A_turn_recorded_before_the_frames_were_kept_reports_none_rather_than_an_empty_run()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.ADVISOR);
        var turnId = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1]
            .Data.GetProperty("runId").GetString()!;

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
        var turnId = proposed[^1].Data.GetProperty("runId").GetString()!;

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
            .Data.GetProperty("runId").GetString()!;

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
