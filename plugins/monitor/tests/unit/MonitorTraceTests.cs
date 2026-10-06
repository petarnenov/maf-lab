using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.Storage;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Domain.Tracing;
using Maf.Lab.Plugins.Monitor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>The monitor's stored trace: who may read it, and its retention apart from the conversation's.</summary>
public class MonitorTraceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Stored_trace_is_readable_by_owner_and_same_firm_admin_for_review_turns_only()
    {
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel());
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var done = (await ApiFactory.ChatAsync(adam, "what is the procedure when a fee schedule is missing"))[^1].Data;
        var turnId = done.GetProperty("runId").GetString()!;
        var url = $"/api/turns/{turnId}/trace";

        var own = await adam.GetFromJsonAsync<TurnTraceDocument>(url, Json, Ct);
        Assert.Equal(turnId, own!.TurnId);
        Assert.Equal(TraceKinds.TurnStart, own.Events[0].Kind);
        Assert.Equal(TraceKinds.TurnEnd, own.Events[^1].Kind);

        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("rita", "firm-a", Role.USER).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.ClientFor("bob", "firm-b", Role.TENANT_ADMIN).GetAsync(url, Ct)).StatusCode);
        var alice = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(url, Ct)).StatusCode); // not in the review queue yet

        await adam.PostAsJsonAsync("/api/feedback", new Maf.Lab.Domain.Feedback.FeedbackRequest(done.GetProperty("threadId").GetString()!, turnId!, "wrong_answer", null), Ct);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync(url, Ct)).StatusCode);
    }

    [Fact]
    public async Task Retention_deletes_old_traces_only_and_logs_stay_free_of_trace_content()
    {
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel("ANSWER-MARKER-777."));
        var adam = api.ClientFor("adam", "firm-a", Role.USER);
        var turnId = (await ApiFactory.ChatAsync(adam, "how do I fix ZEBRA-TRACE-42?"))[^1]
            .Data.GetProperty("runId").GetString()!;

        await using (var ctx = MonitorPluginSupport.Db(api))
        {
            var stored = await ctx.Set<TurnDiagnosticsRow>().SingleAsync(t => t.TurnId == turnId, Ct);
            Assert.Contains("ZEBRA-TRACE-42", stored.Json);
            Assert.Contains("ANSWER-MARKER-777", stored.Json);
            ctx.Set<TurnDiagnosticsRow>().Add(new TurnDiagnosticsRow { TurnId = "t_old", ConversationId = "c", UserId = "adam", TenantId = "firm-a", CreatedAt = DateTime.UtcNow.AddDays(-8), Json = "[]" });
            await ctx.SaveChangesAsync(Ct);
        }
        Assert.DoesNotContain(api.Logs.Messages, m => m.Contains("ZEBRA-TRACE-42") || m.Contains("ANSWER-MARKER-777"));

        var removed = await api.Services.GetRequiredService<TraceRetentionService>().PurgeAsync(Ct);
        Assert.Equal(1, removed);
        await using var check = MonitorPluginSupport.Db(api);
        Assert.False(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == "t_old", Ct));
        Assert.True(await check.Set<TurnDiagnosticsRow>().AnyAsync(t => t.TurnId == turnId, Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await adam.GetAsync("/api/turns/t_old/trace", Ct)).StatusCode);
    }

    [Fact]
    public void The_two_retentions_are_two_settings_and_moving_one_leaves_the_other()
    {
        using var api = MonitorPluginSupport.Api(ApiFactory.ProceduralModel(), settings: new Dictionary<string, string?> { ["Tracing:RetentionDays"] = "3" });

        var traces = api.Services.GetRequiredService<IOptions<TracingOptions>>().Value;
        var messages = api.Services.GetRequiredService<IOptions<MessageRetentionOptions>>().Value;

        Assert.Equal(3, traces.RetentionDays);
        // Unmoved by the other: what was said and how it was worked out are kept for their own reasons.
        Assert.Equal(90, messages.RetentionDays);
    }
}
