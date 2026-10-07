using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Monitor;
using Maf.Lab.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>What crossed the AG-UI wire for a turn: kept by the monitor with the turn's trace, and served with it.</summary>
public class MonitorFrameTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_turn_keeps_every_frame_its_run_streamed_and_serves_them_with_its_trace()
    {
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel("ANSWER-FRAMES-9."));
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
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
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var turnId = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1]
            .Data.GetProperty("runId").GetString()!;

        await using (var ctx = MonitorPluginSupport.Db(api))
        {
            await ctx.Set<TurnDiagnosticsRow>().Where(t => t.TurnId == turnId)
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
        using var api = MonitorPluginSupport.Api(model, new FakeToolSource(), new Dictionary<string, string?>());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);

        var proposed = await ApiFactory.ChatAsync(adam, "adjust the fee on A-1042 down by 200");
        var interrupt = ApiFactory.InterruptOf(proposed);
        Assert.NotNull(interrupt);
        var turnId = proposed[^1].Data.GetProperty("runId").GetString()!;

        string? before;
        await using (var ctx = MonitorPluginSupport.Db(api))
        {
            before = (await ctx.Set<TurnDiagnosticsRow>().SingleAsync(t => t.TurnId == turnId, Ct)).AguiJson;
        }
        Assert.NotNull(before);

        await ApiFactory.ResumeAsync(adam, ApiFactory.ThreadOf(proposed), interrupt.Value.GetProperty("id").GetString()!, approve: true);

        // The answer is a run of its own. It records no turn, so its frames have nowhere to go — and the
        // proposing turn's frames are left exactly as its own run wrote them.
        await using var check = MonitorPluginSupport.Db(api);
        Assert.Equal(1, await check.Set<TurnDiagnosticsRow>().CountAsync(t => t.AguiJson != null, Ct));
        Assert.Equal(before, (await check.Set<TurnDiagnosticsRow>().SingleAsync(t => t.TurnId == turnId, Ct)).AguiJson);
    }

    [Fact]
    public async Task Retention_takes_the_frames_with_the_trace()
    {
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var turnId = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1]
            .Data.GetProperty("runId").GetString()!;

        await using (var ctx = MonitorPluginSupport.Db(api))
        {
            Assert.NotNull((await ctx.Set<TurnDiagnosticsRow>().SingleAsync(t => t.TurnId == turnId, Ct)).AguiJson);
            await ctx.Set<TurnDiagnosticsRow>().Where(t => t.TurnId == turnId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddDays(-8)), Ct);
        }

        Assert.Equal(1, await api.Services.GetRequiredService<TraceRetentionService>().PurgeAsync(Ct));
        await using var check = MonitorPluginSupport.Db(api);
        Assert.False(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == turnId, Ct));
    }
}
