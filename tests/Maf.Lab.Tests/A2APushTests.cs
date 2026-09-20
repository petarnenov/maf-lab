using Maf.Lab.A2A;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.A2A;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>
/// Push notifications against a webhook that really listens: one delivery per transition, carrying the caller's
/// own token — and a task that completes anyway when the receiver is down.
/// </summary>
public class A2APushTests
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

    private static object Message(string text) => new
    {
        message = new
        {
            kind = "message",
            messageId = Guid.NewGuid().ToString("N"),
            role = "user",
            parts = new[] { new { kind = "text", text } },
        },
    };

    private static async Task<JsonElement> RpcAsync(HttpClient client, string method, object @params)
    {
        var response = await client.PostAsJsonAsync("/a2a", new { jsonrpc = "2.0", id = 1, method, @params }, Ct);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct));
        Assert.False(body.TryGetProperty("error", out var error), $"{method}: {error}");
        return body.GetProperty("result");
    }

    /// <summary>The push dispatcher uses a real HttpClient, so the receiver is a real listening socket.</summary>
    private static async Task<(WebApplication Receiver, string Url, ConcurrentQueue<(string? Token, JsonElement Body)> Received)>
        ReceiverAsync(HttpStatusCode answer = HttpStatusCode.NoContent)
    {
        var received = new ConcurrentQueue<(string? Token, JsonElement Body)>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var receiver = builder.Build();
        receiver.MapPost("/hook", async (HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            received.Enqueue((context.Request.Headers["X-A2A-Notification-Token"].FirstOrDefault(), body));
            return Results.StatusCode((int)answer);
        });
        await receiver.StartAsync(Ct);
        var address = receiver.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First();
        return (receiver, $"{address}/hook", received);
    }

    private static async Task<string> StartedRunAsync(HttpClient client, ApiFactory api, string webhook, string? token)
    {
        // A task first, then its configuration, then the work: a configuration belongs to a task that exists.
        var task = await RpcAsync(client, "message/send", Message("start a billing run for firm-a"));
        var taskId = task.GetProperty("id").GetString()!;
        Assert.Equal("input-required", task.GetProperty("status").GetProperty("state").GetString());

        await RpcAsync(client, "tasks/pushNotificationConfig/set", new
        {
            taskId,
            pushNotificationConfig = new { url = webhook, token },
        });

        await RpcAsync(client, "message/send", new
        {
            message = new
            {
                kind = "message",
                messageId = Guid.NewGuid().ToString("N"),
                role = "user",
                parts = new[] { new { kind = "text", text = "2026-06" } },
                taskId,
            },
        });
        return taskId;
    }

    [Fact]
    public async Task Every_transition_is_delivered_once_with_the_registered_token()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);
        var (receiver, url, received) = await ReceiverAsync();
        await using var _ = receiver;

        var taskId = await StartedRunAsync(client, api, url, "shhh");

        var deliveries = received.ToList();
        Assert.NotEmpty(deliveries);
        Assert.All(deliveries, delivery => Assert.Equal("shhh", delivery.Token));

        // One delivery per transition, in order, and each carries the task in the specified shape.
        var states = deliveries.Select(d => d.Body.GetProperty("status").GetProperty("state").GetString()).ToList();
        Assert.Equal(states, states.Distinct());
        Assert.Equal("completed", states[^1]);
        Assert.All(deliveries, delivery =>
        {
            Assert.Equal("task", delivery.Body.GetProperty("kind").GetString());
            Assert.Equal(taskId, delivery.Body.GetProperty("id").GetString());
        });

        await using var db = ChatApiTests.Db(api);
        var recorded = await db.A2APushDeliveries.Where(d => d.TaskId == taskId).ToListAsync(Ct);
        Assert.Equal(deliveries.Count, recorded.Count);
        Assert.All(recorded, row => Assert.True(row.Delivered));
    }

    [Fact]
    public async Task A_task_completes_even_when_the_receiver_is_unreachable_and_the_failure_is_recorded()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);

        // Port 9 is the discard service: nothing answers there, so every attempt fails.
        var taskId = await StartedRunAsync(client, api, "http://127.0.0.1:9/hook", null);

        var task = await RpcAsync(client, "tasks/get", new { id = taskId });
        Assert.Equal("completed", task.GetProperty("status").GetProperty("state").GetString());

        await using var db = ChatApiTests.Db(api);
        var failures = await db.A2APushDeliveries.Where(d => d.TaskId == taskId).ToListAsync(Ct);
        Assert.NotEmpty(failures);
        Assert.All(failures, row =>
        {
            Assert.False(row.Delivered);
            Assert.NotNull(row.Error);
            // Tried, and tried again the configured number of times, before being written off.
            Assert.Equal(3, row.Attempts);
        });
    }

    [Fact]
    public async Task A_receiver_that_answers_with_an_error_is_retried_and_then_recorded()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);
        var (receiver, url, received) = await ReceiverAsync(HttpStatusCode.InternalServerError);
        await using var _ = receiver;

        var taskId = await StartedRunAsync(client, api, url, "shhh");

        await using var db = ChatApiTests.Db(api);
        var rows = await db.A2APushDeliveries.Where(d => d.TaskId == taskId).ToListAsync(Ct);
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal("HTTP 500", row.Error));
        Assert.Equal(rows.Sum(r => r.Attempts), received.Count);
    }
}
