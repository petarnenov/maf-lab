using Maf.Lab.A2A;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Maf.Lab.Api.A2A;
using Maf.Lab.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Tests;

/// <summary>
/// The protocol surface as a partner meets it, written the way the 1.0 specification writes it — <c>message/send</c>,
/// <c>"role": "user"</c>, <c>"kind": "text"</c> — never the preview SDK's dialect. Nothing here knows the SDK exists.
/// </summary>
public class A2AProtocolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpClient> PartnerClientAsync(ApiFactory api)
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/a2a/token", new A2AEndpoints.TokenRequest("acme-portal", "s3cret"), Ct);
        var token = (await response.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(Ct))!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> RpcAsync(HttpClient client, string method, object @params)
    {
        var response = await client.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method, @params }, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(body.Length > 0, $"{method} → {(int)response.StatusCode} with an empty body");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static object Message(string text, string? taskId = null) => new
    {
        message = new
        {
            kind = "message",
            messageId = Guid.NewGuid().ToString("N"),
            role = "user",
            parts = new[] { new { kind = "text", text } },
            taskId,
        },
    };

    private static JsonElement Result(JsonElement response)
    {
        Assert.False(response.TryGetProperty("error", out var error), error.ToString());
        return response.GetProperty("result");
    }

    [Fact]
    public async Task Both_transports_need_a_partner_token()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var anonymous = api.CreateClient();

        var rpc = await anonymous.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method = "message/send" }, Ct);
        var rest = await anonymous.GetAsync("/a2a/tasks/whatever", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, rpc.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rest.StatusCode);

        // A chat user's token is not a partner token, whatever its role.
        var user = api.ClientFor("alice", "firm-a", Maf.Lab.Domain.Tenancy.Role.FIRM_ADMIN);
        var asUser = await user.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method = "message/send" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, asUser.StatusCode);
    }

    [Fact]
    public async Task A_question_comes_back_as_a_message_in_the_specified_shape()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);

        var message = Result(await RpcAsync(client, "message/send", Message("status of run 4417")));

        Assert.Equal("message", message.GetProperty("kind").GetString());
        Assert.Equal("agent", message.GetProperty("role").GetString());
        var part = message.GetProperty("parts")[0];
        Assert.Equal("text", part.GetProperty("kind").GetString());
        Assert.Contains("4417", part.GetProperty("text").GetString()!);
    }

    [Fact]
    public async Task A_question_that_is_not_about_a_run_is_answered_by_the_assistant_itself()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);

        var message = Result(await RpcAsync(client, "message/send",
            Message("How do I fix a missing fee schedule?")));

        // The same agent, the same tools: the answer is the assistant's, not a second implementation's.
        Assert.Contains("Procedure: Missing fee schedule", message.GetProperty("parts")[0].GetProperty("text").GetString()!);
        Assert.Contains("search_documents", api.Tools.Invocations);
    }

    [Fact]
    public async Task The_same_question_over_the_http_json_transport_answers_the_same()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);

        var response = await client.PostAsJsonAsync("/a2a/message:send", Message("status of run 4417"), Ct);
        var message = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal("message", message.GetProperty("kind").GetString());
        Assert.Equal("agent", message.GetProperty("role").GetString());
        Assert.Contains("4417", message.GetProperty("parts")[0].GetProperty("text").GetString()!);
    }

    [Fact]
    public async Task The_extended_card_needs_the_token_and_carries_the_private_skill()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var anonymous = api.CreateClient();
        var partner = await PartnerClientAsync(api);

        var refused = await anonymous.PostAsJsonAsync("/a2a",
            new { jsonrpc = "2.0", id = 1, method = "agent/getAuthenticatedExtendedCard", @params = new { } }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        var allowed = await RpcAsync(partner, "agent/getAuthenticatedExtendedCard", new { });
        Assert.Contains(BillingAgentCard.PrivateSkillId, allowed.GetRawText());

        // …and the public card still does not mention it.
        var publicCard = await anonymous.GetStringAsync(AgentCardFactory.WellKnownPath, Ct);
        Assert.DoesNotContain(BillingAgentCard.PrivateSkillId, publicCard);
    }

    [Fact]
    public async Task Push_configuration_can_be_created_read_listed_and_deleted()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);
        var store = api.Services.GetRequiredService<ITaskStore>();
        await store.SaveTaskAsync("task-push", new AgentTask
        {
            Id = "task-push",
            ContextId = "ctx",
            Status = new global::A2A.TaskStatus { State = TaskState.Working },
        }, Ct);

        var created = Result(await RpcAsync(client, "tasks/pushNotificationConfig/set", new
        {
            taskId = "task-push",
            pushNotificationConfig = new { url = "http://localhost:9/hook", token = "shhh" },
        }));
        Assert.Equal("http://localhost:9/hook",
            created.GetProperty("pushNotificationConfig").GetProperty("url").GetString());

        var listed = Result(await RpcAsync(client, "tasks/pushNotificationConfig/list", new { id = "task-push" }));
        Assert.Contains("http://localhost:9/hook", listed.GetRawText());

        string configId;
        await using (var db = ChatApiTests.Db(api))
        {
            var row = Assert.Single(await db.A2APushConfigs.Where(c => c.TaskId == "task-push").ToListAsync(Ct));
            Assert.Equal("shhh", row.Token);
            configId = row.Id;
        }

        var fetched = Result(await RpcAsync(client, "tasks/pushNotificationConfig/get",
            new { id = "task-push", pushNotificationConfigId = configId }));
        Assert.Equal("task-push", fetched.GetProperty("taskId").GetString());

        var deleted = await RpcAsync(client, "tasks/pushNotificationConfig/delete",
            new { id = "task-push", pushNotificationConfigId = configId });
        Assert.False(deleted.TryGetProperty("error", out _), deleted.GetRawText());

        await using var after = ChatApiTests.Db(api);
        Assert.Empty(await after.A2APushConfigs.Where(c => c.TaskId == "task-push").ToListAsync(Ct));
    }

    [Fact]
    public async Task A_configuration_is_scoped_to_its_task()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);

        var refused = await RpcAsync(client, "tasks/pushNotificationConfig/set", new
        {
            taskId = "no-such-task",
            pushNotificationConfig = new { url = "http://localhost:9/hook" },
        });

        Assert.True(refused.TryGetProperty("error", out _), refused.GetRawText());
    }

    [Fact]
    public async Task A_task_is_fetchable_afterwards_and_carries_its_artifact()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);

        var started = Result(await RpcAsync(client, "message/send", Message("start a billing run for firm-a 2026-06")));
        Assert.Equal("task", started.GetProperty("kind").GetString());
        var taskId = started.GetProperty("id").GetString()!;

        // Fetching it afterwards — as another replica would, since the state is in the shared store.
        var task = Result(await RpcAsync(client, "tasks/get", new { id = taskId }));
        Assert.Equal(taskId, task.GetProperty("id").GetString());
        Assert.Equal("completed", task.GetProperty("status").GetProperty("state").GetString());

        var artifact = task.GetProperty("artifacts")[0];
        Assert.Equal("billing-run-result", artifact.GetProperty("name").GetString());
        var part = artifact.GetProperty("parts")[0];
        Assert.Equal("data", part.GetProperty("kind").GetString());
        Assert.True(part.GetProperty("data").GetProperty("simulated").GetBoolean());
    }

    [Fact]
    public async Task A_run_for_another_firm_is_rejected_in_the_specified_state()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);

        var task = Result(await RpcAsync(client, "message/send", Message("start a billing run for firm-b 2026-06")));

        Assert.Equal("rejected", task.GetProperty("status").GetProperty("state").GetString());
        Assert.DoesNotContain("firm-b", task.GetRawText());
    }
}
