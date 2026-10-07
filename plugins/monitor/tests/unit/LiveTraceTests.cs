using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Plugins.Monitor;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Tests;

/// <summary>
/// A run's trace no longer travels on its stream (agui-protocol-only): the monitor reads it from the trace API while the
/// run is live, from any replica, under the turn's own access rules.
/// </summary>
public class LiveTraceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_owner_reads_a_runs_trace_and_then_only_what_came_after()
    {
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing", runId: "r_live");

        var all = await adam.GetFromJsonAsync<LiveTraceDocument>("/api/runs/r_live/trace", Json, Ct);
        Assert.True(all!.Ended);
        Assert.Equal("r_live", all.TurnId);
        Assert.Equal(TraceKinds.TurnStart, all.Events[0].Kind);
        Assert.Equal(Enumerable.Range(1, all.Events.Count), all.Events.Select(e => e.Seq));

        // The same events the stored turn keeps.
        var stored = await adam.GetFromJsonAsync<TurnTraceDocument>("/api/turns/r_live/trace", Json, Ct);
        Assert.Equal(stored!.Events.Select(e => e.Kind), all.Events.Select(e => e.Kind));

        var rest = await adam.GetFromJsonAsync<LiveTraceDocument>("/api/runs/r_live/trace?after=3", Json, Ct);
        Assert.Equal(all.Events.Skip(3).Select(e => e.Seq), rest!.Events.Select(e => e.Seq));
    }

    [Fact]
    public async Task Another_user_and_another_firm_are_told_nothing()
    {
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel());
        await ApiFactory.ChatAsync(api.ClientFor("adam", "firm-a", Role.USER), "what is the procedure when a fee schedule is missing",
            runId: "r_private");

        Assert.Equal(HttpStatusCode.NotFound,
            (await api.ClientFor("rita", "firm-a", Role.TENANT_ADMIN).GetAsync("/api/runs/r_private/trace", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await api.ClientFor("bob", "firm-b", Role.USER).GetAsync("/api/runs/r_private/trace", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await api.ClientFor("adam", "firm-a", Role.USER).GetAsync("/api/runs/r_unknown/trace", Ct)).StatusCode);
    }

    [Fact]
    public async Task An_answer_to_a_question_records_no_turn_but_its_trace_is_readable_while_kept()
    {
        var chat = new ScriptedChatClient((messages, _, _) =>
            messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text?.Contains("adjust", StringComparison.OrdinalIgnoreCase) == true
            && !ScriptedChatClient.HasResult(messages, FeeAdjustmentTool.Name)
                ? ScriptedChatClient.Call(FeeAdjustmentTool.Name, new()
                {
                    ["accountId"] = "A-1042", ["amount"] = -200m, ["reason"] = "overcharged in Q2",
                })
                : ScriptedChatClient.Text("Done."));
        using var api = MonitorPluginSupport.Api(chat, new FakeToolSource(), settings: new Dictionary<string, string?>());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var proposed = await ApiFactory.ChatAsync(adam, "adjust the fee on A-1042 down by 200");
        var resumed = await ApiFactory.ResumeAsync(adam, ApiFactory.ThreadOf(proposed),
            ApiFactory.InterruptOf(proposed)!.Value.GetProperty("id").GetString()!, approve: true);
        var runId = resumed.First(e => e.Name == "RUN_STARTED").Data.GetProperty("runId").GetString();

        var doc = await adam.GetFromJsonAsync<LiveTraceDocument>($"/api/runs/{runId}/trace", Json, Ct);

        Assert.True(doc!.Ended);
        Assert.Null(doc.TurnId);
    }

    [Fact]
    public async Task A_run_is_there_to_ask_about_from_before_its_first_event()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new FakeToolSource
        {
            BeforeSearchExecutes = async () =>
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            },
        };
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel(), tools);
        var adam = api.ClientFor("adam", "firm-a", Role.USER);

        var run = ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing", runId: "r_early");
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var live = await adam.GetFromJsonAsync<LiveTraceDocument>("/api/runs/r_early/trace", Json, Ct);
        release.TrySetResult();
        await run;

        Assert.False(live!.Ended);
        Assert.NotEmpty(live.Events);
    }
}
