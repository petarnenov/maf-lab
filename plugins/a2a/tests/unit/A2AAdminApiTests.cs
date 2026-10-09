using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.A2A;
using Maf.Lab.Plugins.A2A;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// What an operator can see of the agents' conversations. The task table has no firm; the audit does, and it is
/// what decides who sees what — so these tests are as much about tenancy as about the screen.
/// </summary>
public class A2AAdminApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>`stepMs` is how long each stage of a simulated billing run takes: slow enough to catch it running.</summary>
    private static ApiFactory Api(int stepMs = 1) =>
        new(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = A2APluginSupport.Installed,
            SimulatedStepMs = stepMs,
            ExtraSettings = new Dictionary<string, string?>(),
        };

    private static async Task<HttpClient> PartnerAsync(ApiFactory api)
    {
        A2APluginSupport.BootstrapPartners(api);
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/a2a/token", new A2AEndpoints.TokenRequest("acme-portal", "s3cret"), Ct);
        var token = (await response.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(Ct))!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Starts work a partner would start and waits for it to finish, because a non-streaming send does.
    /// </summary>
    private static async Task<string> StartRunAsync(HttpClient partner)
    {
        var response = await partner.PostAsJsonAsync("/a2a", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "message/send",
            @params = new
            {
                message = new
                {
                    kind = "message",
                    messageId = Guid.NewGuid().ToString("N"),
                    role = "user",
                    parts = new object[] { new { kind = "text", text = "start a billing run for firm-a for 2026-06" } },
                },
            },
        }, Ct);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct));
        return body.GetProperty("result").GetProperty("id").GetString()!;
    }

    /// <summary>Waits until the screen shows a task that can still be stopped, and answers with its id.</summary>
    private static async Task<string> RunningTaskAsync(HttpClient admin)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var activity = await ActivityAsync(admin);
            if (activity!.Inbound.FirstOrDefault(t => t.Cancellable) is { } running)
            {
                return running.TaskId;
            }
            await Task.Delay(100, Ct);
        }
        throw new InvalidOperationException("no task was ever running");
    }

    private static Task<A2AAdminEndpoints.A2AActivity?> ActivityAsync(HttpClient admin) =>
        admin.GetFromJsonAsync<A2AAdminEndpoints.A2AActivity>("/api/admin/a2a", Json, Ct);

    [Fact]
    public async Task What_a_partner_did_is_visible_to_the_firm_it_concerned()
    {
        using var api = Api();
        var taskId = await StartRunAsync(await PartnerAsync(api));

        var activity = await ActivityAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN));

        var task = Assert.Single(activity!.Inbound, t => t.TaskId == taskId);
        Assert.Equal("acme-portal", task.PartnerId);
        Assert.StartsWith("a2a.", task.Operation);
        Assert.NotEmpty(task.State);
    }

    [Fact]
    public async Task Another_firm_sees_none_of_it()
    {
        using var api = Api();
        await StartRunAsync(await PartnerAsync(api));

        var activity = await ActivityAsync(api.ClientFor("bob", "firm-b", Role.TENANT_ADMIN));

        Assert.Empty(activity!.Inbound);
        Assert.Empty(activity.Deliveries);
    }

    [Fact]
    public async Task No_message_content_is_anywhere_in_it()
    {
        using var api = Api();
        await StartRunAsync(await PartnerAsync(api));

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN).GetStringAsync("/api/admin/a2a", Ct);

        Assert.DoesNotContain("start a billing run", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_advisor_does_not_get_the_screen()
    {
        using var api = Api();

        var response = await api.ClientFor("adam", "firm-a", Role.USER).GetAsync("/api/admin/a2a", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_running_task_can_be_cancelled_by_the_firm_it_concerns()
    {
        using var api = Api(stepMs: 3_000);
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);

        // A non-streaming send waits for the task, so it is left running while the admin catches it.
        var partner = await PartnerAsync(api);
        var running = StartRunAsync(partner);
        var taskId = await RunningTaskAsync(admin);

        var response = await admin.PostAsync($"/api/admin/a2a/tasks/{taskId}/cancel", null, Ct);
        await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(20), Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains("ancel", body.GetProperty("state").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Another_firms_task_is_not_cancellable()
    {
        using var api = Api(stepMs: 3_000);
        var partner = await PartnerAsync(api);
        var running = StartRunAsync(partner);
        var taskId = await RunningTaskAsync(api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN));

        var response = await api.ClientFor("bob", "firm-b", Role.TENANT_ADMIN)
            .PostAsync($"/api/admin/a2a/tasks/{taskId}/cancel", null, Ct);
        await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(20), Ct));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_task_that_never_existed_is_not_found()
    {
        using var api = Api();
        await StartRunAsync(await PartnerAsync(api));

        var response = await api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN)
            .PostAsync("/api/admin/a2a/tasks/nothing/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_plugin_owns_its_existing_table_names()
    {
        using var api = Api();
        await using var context = await api.Services.GetRequiredService<IDbContextFactory<DbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("A2ATasks", context.Model.FindEntityType(typeof(A2ATaskRow))!.GetTableName());
        Assert.Equal("A2APushConfigs", context.Model.FindEntityType(typeof(A2APushConfigRow))!.GetTableName());
        Assert.Equal("A2APushDeliveries", context.Model.FindEntityType(typeof(A2APushDeliveryRow))!.GetTableName());
    }

    [Fact]
    public void A_protocol_host_requires_an_explicit_store_keyspace()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel())
        {
            InstalledPlugins = A2APluginSupport.Installed,
            ExtraSettings = new Dictionary<string, string?> { ["A2A:StoreKeyspace"] = "" },
        };
        var error = Assert.Throws<OptionsValidationException>(() => api.CreateClient());
        Assert.Contains("A2A:StoreKeyspace is required", error.Message);
    }

    [Fact]
    public async Task Plugin_removal_stops_live_tasks_through_the_owning_server()
    {
        using var api = Api(stepMs: 3_000);
        var admin = api.ClientFor("alice", "firm-a", Role.TENANT_ADMIN);
        var running = StartRunAsync(await PartnerAsync(api));
        var taskId = await RunningTaskAsync(admin);
        var work = new A2APlugin().CreateOpenWork(api.Services);
        Assert.Contains(await work.ListOpenAsync(Ct), item => item.Id == taskId);
        await work.CancelAllAsync(Ct);
        await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(20), Ct));
        Assert.DoesNotContain(await work.ListOpenAsync(Ct), item => item.Id == taskId);
        var task = await api.Services.GetRequiredService<global::A2A.ITaskStore>().GetTaskAsync(taskId, Ct);
        Assert.Equal(global::A2A.TaskState.Canceled, task!.Status.State);
    }
}
