using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maf.Lab.Api.A2A;

namespace Maf.Lab.Tests;

/// <summary>
/// What a caller following a task sees: events in the 1.0 shape, and a task that keeps going — and stays complete —
/// when the caller's connection does not.
/// </summary>
public class A2AStreamingTests
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

    /// <summary>Reads SSE frames, stopping when <paramref name="until"/> has seen enough of them.</summary>
    private static async Task<List<JsonElement>> EventsAsync(HttpClient client, object request,
        Func<List<JsonElement>, bool>? until = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/a2a") { Content = JsonContent.Create(request) };
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = new List<JsonElement>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }
            events.Add(JsonDocument.Parse(line[6..]).RootElement.GetProperty("result"));
            if (until?.Invoke(events) == true)
            {
                break; // The caller has gone away mid-task, as a dropped connection would.
            }
        }
        return events;
    }

    private static string State(JsonElement @event) =>
        @event.GetProperty("status").GetProperty("state").GetString()!;

    /// <summary>The states an event stream went through; an artifact update carries no state of its own.</summary>
    private static IEnumerable<string> States(IEnumerable<JsonElement> events) =>
        events.Where(e => e.TryGetProperty("status", out _)).Select(State);

    [Fact]
    public async Task A_streamed_run_reports_every_stage_in_the_specified_shape()
    {
        using var api = new ApiFactory(ApiFactory.ProceduralModel());
        var client = await PartnerClientAsync(api);

        var events = await EventsAsync(client, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "message/stream",
            @params = Message("start a billing run for firm-a 2026-06"),
        });

        Assert.Equal("task", events[0].GetProperty("kind").GetString());
        Assert.Equal("submitted", State(events[0]));

        var artifact = Assert.Single(events, e => e.GetProperty("kind").GetString() == "artifact-update");
        var part = artifact.GetProperty("artifact").GetProperty("parts")[0];
        Assert.Equal("data", part.GetProperty("kind").GetString());

        var last = events[^1];
        Assert.Equal("status-update", last.GetProperty("kind").GetString());
        Assert.Equal("completed", State(last));
        Assert.True(last.GetProperty("final").GetBoolean());
        // Everything before the end is explicitly not final, so a caller knows when to stop reading.
        Assert.All(events.Where(e => e.GetProperty("kind").GetString() == "status-update").SkipLast(1),
            e => Assert.False(e.GetProperty("final").GetBoolean()));
        Assert.Equal("agent", last.GetProperty("status").GetProperty("message").GetProperty("role").GetString());
    }

    [Fact]
    public async Task A_dropped_stream_loses_nothing_the_resubscription_cannot_recover()
    {
        // Slow enough that the caller can disappear in the middle of the run.
        using var api = new ApiFactory(ApiFactory.ProceduralModel()) { SimulatedStepMs = 120 };
        var client = await PartnerClientAsync(api);

        var seen = await EventsAsync(client, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "message/stream",
            @params = Message("start a billing run for firm-a 2026-06"),
        }, until: events => events.Count == 2);
        var taskId = seen[0].GetProperty("id").GetString()!;
        Assert.Equal("working", State(seen[1]));

        // Resubscribing: the first event is the whole task as it stands, so nothing before it has to be replayed.
        var resumed = await EventsAsync(client, new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tasks/resubscribe",
            @params = new { id = taskId },
        });

        Assert.Equal("task", resumed[0].GetProperty("kind").GetString());
        Assert.Equal(taskId, resumed[0].GetProperty("id").GetString());

        // The run was never the caller's to interrupt: it finishes, and the transitions are all accounted for.
        var states = States(seen).Concat(States(resumed.Skip(1))).ToList();
        Assert.Equal("submitted", states[0]);
        Assert.Contains("working", states);
        Assert.Equal("completed", states[^1]);
        Assert.True(resumed[^1].GetProperty("final").GetBoolean());
        Assert.Equal("completed", States(resumed).Last());

        // …and what it started from was the task as it stood, not the task as it began.
        Assert.Equal("working", State(resumed[0]));
    }
}
