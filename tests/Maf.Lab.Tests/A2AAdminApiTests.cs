using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.A2A;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestSupport;

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
            SimulatedStepMs = stepMs,
            ExtraSettings = new Dictionary<string, string?> { ["Compliance:BaseUrl"] = "" },
        };

    private static async Task<HttpClient> PartnerAsync(ApiFactory api)
    {
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

        var activity = await ActivityAsync(api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN));

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

        var activity = await ActivityAsync(api.ClientFor("bob", "firm-b", Role.FIRM_ADMIN));

        Assert.Empty(activity!.Inbound);
        Assert.Empty(activity.Deliveries);
    }

    [Fact]
    public async Task No_message_content_is_anywhere_in_it()
    {
        using var api = Api();
        await StartRunAsync(await PartnerAsync(api));

        var response = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN).GetStringAsync("/api/admin/a2a", Ct);

        Assert.DoesNotContain("start a billing run", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_advisor_does_not_get_the_screen()
    {
        using var api = Api();

        var response = await api.ClientFor("adam", "firm-a", Role.ADVISOR).GetAsync("/api/admin/a2a", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_running_task_can_be_cancelled_by_the_firm_it_concerns()
    {
        using var api = Api(stepMs: 3_000);
        var admin = api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN);

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
        var taskId = await RunningTaskAsync(api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN));

        var response = await api.ClientFor("bob", "firm-b", Role.FIRM_ADMIN)
            .PostAsync($"/api/admin/a2a/tasks/{taskId}/cancel", null, Ct);
        await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(20), Ct));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_task_that_never_existed_is_not_found()
    {
        using var api = Api();
        await StartRunAsync(await PartnerAsync(api));

        var response = await api.ClientFor("alice", "firm-a", Role.FIRM_ADMIN)
            .PostAsync("/api/admin/a2a/tasks/nothing/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
