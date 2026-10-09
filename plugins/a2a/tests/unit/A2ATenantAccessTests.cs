using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.A2A;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Plugins.A2A;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Tests;

public sealed class A2ATenantAccessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ApiFactory Host(bool multiple = false, int delay = 1) => new(ApiFactory.ProceduralModel())
    {
        BootstrapTenantPlugins = false,
        InstalledPlugins = A2APluginSupport.Installed,
        SimulatedStepMs = delay,
        ExtraSettings = multiple ? new Dictionary<string, string?>
        {
            ["A2A:Partners:acme-portal:Firms:1"] = "firm-b",
            ["A2A:Partners:other-portal:Secret"] = "other-secret",
            ["A2A:Partners:other-portal:Firms:0"] = "firm-a",
            ["A2A:Partners:other-portal:Firms:1"] = "firm-b",
            ["A2A:Partners:other-portal:Scopes:0"] = A2AScopes.BillingRead,
        } : new Dictionary<string, string?>(),
    };

    private static Task<PluginChange> Allow(ApiFactory api, string tenant, string plugin, bool enabled = true) =>
        api.Services.GetRequiredService<IPluginEntitlements>().AllowAsync(
            new Principal("operator", TenantId.Firm(tenant), Role.PLATFORM_ADMIN), plugin, enabled, enabled, Ct);

    private static async Task<HttpClient> Partner(ApiFactory api, string id = "acme-portal", string secret = "s3cret")
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync(AgentCardFactory.TokenPath,
            new A2AEndpoints.TokenRequest(id, secret), Ct);
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(Ct))!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object Send(string text, string method = "message/send") => new
    {
        jsonrpc = "2.0", id = 1, method,
        @params = new { message = new { kind = "message", messageId = Guid.NewGuid().ToString("N"), role = "user",
            parts = new[] { new { kind = "text", text } } } },
    };

    private static async Task<JsonElement> Result(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/a2a", body, Ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(json.TryGetProperty("error", out _), json.GetRawText());
        return json.GetProperty("result");
    }

    [Fact]
    public async Task Partner_cannot_call_disabled_A2A_but_can_discover_its_security_scheme()
    {
        using var api = Host();
        using var partner = await Partner(api);
        using var discovery = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await discovery.GetAsync(AgentCardFactory.WellKnownPath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await partner.PostAsJsonAsync("/a2a", Send("status of run 4417"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await partner.PostAsJsonAsync("/a2a/message:send", new { message = new
            { messageId = "m", role = "user", parts = new[] { new { kind = "text", text = "status of run 4417" } } } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await partner.GetAsync("/a2a/card", Ct)).StatusCode);
        Assert.Empty(api.Tools.Invocations);
        Assert.Empty(api.Chat.Requests);
    }

    [Fact]
    public async Task Billing_disabled_for_a_partner_tenant_hides_skills_and_refuses_work_before_tools_or_model()
    {
        using var api = Host();
        await Allow(api, "firm-a", A2APlugin.PluginName);
        using var partner = await Partner(api);
        var card = await Result(partner, new { jsonrpc = "2.0", id = 1, method = "agent/getAuthenticatedExtendedCard", @params = new { } });
        Assert.Empty(card.GetProperty("skills").EnumerateArray());
        var httpCard = await partner.GetFromJsonAsync<JsonElement>("/a2a/card", Ct);
        Assert.Empty(httpCard.GetProperty("skills").EnumerateArray());
        var status = await Result(partner, Send("status of run 4417"));
        Assert.Equal(AssistantAgentHandler.OutOfScope, status.GetProperty("parts")[0].GetProperty("text").GetString());
        var question = await Result(partner, Send("How do I fix a missing fee schedule?"));
        Assert.Equal(AssistantAgentHandler.OutOfScope, question.GetProperty("parts")[0].GetProperty("text").GetString());
        var run = await Result(partner, Send("start a billing run for firm-a 2026-06"));
        Assert.Equal("rejected", run.GetProperty("status").GetProperty("state").GetString());
        Assert.False(run.TryGetProperty("artifacts", out _));
        Assert.Empty(api.Tools.Invocations);
        Assert.Empty(api.Chat.Requests);

        await Allow(api, "firm-a", StandInDomains.BillingManifest.Name);
        var enabled = await Result(partner, new { jsonrpc = "2.0", id = 1, method = "agent/getAuthenticatedExtendedCard", @params = new { } });
        Assert.Contains(AssistantAgentCard.PrivateSkillId, enabled.GetRawText());
        await Allow(api, "firm-a", StandInDomains.BillingManifest.Name, false);
        Assert.Empty((await partner.GetFromJsonAsync<JsonElement>("/a2a/card", Ct)).GetProperty("skills").EnumerateArray());
    }

    [Fact]
    public async Task Multi_firm_partner_selects_an_enabled_reader_and_cannot_start_work_for_its_disabled_firm()
    {
        using var api = Host(multiple: true);
        await Allow(api, "firm-b", A2APlugin.PluginName);
        await Allow(api, "firm-b", StandInDomains.BillingManifest.Name);
        using var partner = await Partner(api);
        var refused = await Result(partner, Send("start a billing run for firm-a 2026-06"));
        Assert.Equal("rejected", refused.GetProperty("status").GetProperty("state").GetString());
        Assert.Empty(api.Tools.Invocations);
        // Both firms are enabled before the named firm-b request: choosing the first enabled firm would be wrong.
        await Allow(api, "firm-a", A2APlugin.PluginName);
        await Allow(api, "firm-a", StandInDomains.BillingManifest.Name);
        var permitted = await Result(partner, Send("start a billing run for firm-b 2026-06"));
        Assert.Equal("completed", permitted.GetProperty("status").GetProperty("state").GetString());
        Assert.Equal("firm-b", permitted.GetProperty("artifacts")[0].GetProperty("parts")[0].GetProperty("data").GetProperty("firmId").GetString());
        var taskId = permitted.GetProperty("id").GetString()!;
        await using (var db = ChatApiTests.Db(api))
        {
            var row = await db.Set<A2ATaskRow>().SingleAsync(task => task.Id == taskId, Ct);
            Assert.Equal("firm-b", row.TenantId);
            Assert.Equal("acme-portal", row.PartnerId);
            Assert.Equal("firm-b", (await db.Audit.SingleAsync(audit => audit.Kind == Maf.Lab.Api.Compliance.AuditKinds.A2ARequest
                && audit.Arguments.Contains(taskId), Ct)).TenantId);
        }
        await Allow(api, "firm-a", A2APlugin.PluginName);
        using var adminA = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        Assert.DoesNotContain(taskId, await adminA.GetStringAsync("/api/admin/a2a", Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await adminA.PostAsync($"/api/admin/a2a/tasks/{taskId}/cancel", null, Ct)).StatusCode);
    }

    private static async Task AssertTaskHidden(HttpClient caller, string taskId)
    {
        var listed = await Result(caller, new { jsonrpc = "2.0", id = 1, method = "tasks/list", @params = new { } });
        Assert.DoesNotContain(taskId, listed.GetRawText());
        foreach (var method in new[] { "tasks/get", "tasks/cancel", "tasks/resubscribe", "tasks/pushNotificationConfig/get",
            "tasks/pushNotificationConfig/list", "tasks/pushNotificationConfig/delete" })
        {
            var response = await caller.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method,
                @params = new { id = taskId, pushNotificationConfigId = "push" } }, Ct);
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.Contains("error", body);
            Assert.DoesNotContain("billing-run-result", body);
            Assert.DoesNotContain("firm-a", body);
        }
        var push = await caller.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method = "tasks/pushNotificationConfig/set",
            @params = new { taskId, pushNotificationConfig = new { url = "http://localhost:9/hook" } } }, Ct);
        Assert.Contains("error", await push.Content.ReadAsStringAsync(Ct));
        var continuation = await caller.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method = "message/send",
            @params = new { message = new { kind = "message", messageId = "continue", role = "user", taskId,
                parts = new[] { new { kind = "text", text = "2026-07" } } } } }, Ct);
        Assert.Contains("error", await continuation.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Tasks_and_push_configs_are_private_to_the_partner_and_each_enabled_registered_tenant()
    {
        using var api = Host(multiple: true);
        foreach (var tenant in new[] { "firm-a", "firm-b" })
        {
            await Allow(api, tenant, A2APlugin.PluginName);
            await Allow(api, tenant, StandInDomains.BillingManifest.Name);
        }
        using var owner = await Partner(api);
        var task = await Result(owner, Send("start a billing run for firm-a 2026-06"));
        var taskId = task.GetProperty("id").GetString()!;
        await Result(owner, new { jsonrpc = "2.0", id = 1, method = "tasks/pushNotificationConfig/set",
            @params = new { taskId, pushNotificationConfig = new { id = "push", url = "http://localhost:9/hook", token = "private-token" } } });
        using var other = await Partner(api, "other-portal", "other-secret");
        await AssertTaskHidden(other, taskId);
        await Allow(api, "firm-a", A2APlugin.PluginName, false);
        await AssertTaskHidden(owner, taskId);
        await Allow(api, "firm-a", A2APlugin.PluginName);
        await Allow(api, "firm-a", StandInDomains.BillingManifest.Name, false);
        await AssertTaskHidden(owner, taskId);
        await using var db = ChatApiTests.Db(api);
        Assert.Equal("Completed", (await db.Set<A2ATaskRow>().SingleAsync(row => row.Id == taskId, Ct)).State);
        Assert.Equal("private-token", (await db.Set<A2APushConfigRow>().SingleAsync(row => row.TaskId == taskId, Ct)).Token);
    }

    [Fact]
    public async Task A_continuation_cannot_switch_the_persisted_task_to_another_registered_tenant()
    {
        using var api = Host(multiple: true);
        foreach (var tenant in new[] { "firm-a", "firm-b" })
        {
            await Allow(api, tenant, A2APlugin.PluginName);
            await Allow(api, tenant, StandInDomains.BillingManifest.Name);
        }
        using var partner = await Partner(api);
        // No tenant in the history: only the persisted owner can prevent this continuation from switching firms.
        var asked = await Result(partner, Send("start a billing run"));
        var taskId = asked.GetProperty("id").GetString()!;
        Assert.Equal("input-required", asked.GetProperty("status").GetProperty("state").GetString());
        var continued = await Result(partner, new { jsonrpc = "2.0", id = 1, method = "message/send",
            @params = new { message = new { kind = "message", messageId = "resume", role = "user", taskId,
                metadata = new { partnerId = "other-portal", tenantId = "firm-b" },
                parts = new[] { new { kind = "text", text = "start a billing run for firm-b 2026-06" } } } } });
        Assert.Equal("rejected", continued.GetProperty("status").GetProperty("state").GetString());
        Assert.Empty(api.Tools.Invocations);
        await using var db = ChatApiTests.Db(api);
        var row = await db.Set<A2ATaskRow>().SingleAsync(task => task.Id == taskId, Ct);
        Assert.Equal("firm-a", row.TenantId);
        Assert.Equal("acme-portal", row.PartnerId);
        Assert.All(await db.Audit.Where(audit => audit.Arguments.Contains(taskId)).ToListAsync(Ct), audit => Assert.Equal("firm-a", audit.TenantId));
    }

    [Fact]
    public async Task A_long_run_keeps_its_snapshot_and_the_next_request_observes_the_withdrawal()
    {
        using var api = Host(delay: 50);
        await Allow(api, "firm-a", A2APlugin.PluginName);
        await Allow(api, "firm-a", StandInDomains.BillingManifest.Name);
        using var partner = await Partner(api);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/a2a") { Content = JsonContent.Create(Send("start a billing run for firm-a 2026-06", "message/stream")) };
        using var response = await partner.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        var withdrew = false;
        var completed = false;
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var item = JsonSerializer.Deserialize<JsonElement>(line[6..]).GetProperty("result");
            if (!item.TryGetProperty("status", out var status)) continue;
            if (!withdrew && status.GetProperty("state").GetString() == "working")
            {
                await Allow(api, "firm-a", StandInDomains.BillingManifest.Name, false);
                withdrew = true;
            }
            completed |= status.GetProperty("state").GetString() == "completed";
        }
        Assert.True(withdrew);
        Assert.True(completed);
        Assert.Contains("search_billing_runs", api.Tools.Invocations);
        var next = await Result(partner, Send("start a billing run for firm-a 2026-06"));
        Assert.Equal("rejected", next.GetProperty("status").GetProperty("state").GetString());
    }
}
