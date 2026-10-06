using System.Text.Json;
using Maf.Lab.Api.Agent.Streaming;
using Maf.Lab.Domain.Tracing;

namespace Maf.Lab.Tests;

/// <summary>What crossed the AG-UI wire for a run, as the core records it for an observer that wants it (introduce-plugins 5.3).</summary>
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
}
