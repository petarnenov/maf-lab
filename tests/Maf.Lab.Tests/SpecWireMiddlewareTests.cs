using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.A2A;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Maf.Lab.Tests;

/// <summary>
/// The middleware that puts <see cref="SpecWire"/> on the wire, against endpoints that stand in for the SDK and
/// write exactly what a test tells them to: which dialect a request is rewritten into, which dialect an answer
/// comes back in, and what reaches the caller as it is written rather than at the end.
/// </summary>
public class SpecWireMiddlewareTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string SpecRpc = """
        {"jsonrpc":"2.0","id":1,"method":"message/send","params":{"message":{"kind":"message","messageId":"m-1","role":"user","parts":[{"kind":"text","text":"hi"}]}}}
        """;

    /// <summary>The same message over the HTTP+JSON transport, which carries no envelope at all.</summary>
    private const string SpecMessage = """
        {"message":{"kind":"message","messageId":"m-1","role":"user","parts":[{"kind":"text","text":"hi"}]}}
        """;

    /// <summary>The message as the preview SDK spells it: no kind, ROLE_USER, a part with no kind.</summary>
    private const string SdkMessage = """
        {"message":{"messageId":"m-1","role":"ROLE_USER","parts":[{"text":"hi"}]}}
        """;

    /// <summary>A host whose /a2a endpoints are the stand-ins this file writes the SDK's half with.</summary>
    private static async Task<SpecWireHost> HostAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseA2ASpecWire();
        MapStandIns(app);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First();
        return new SpecWireHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private sealed class SpecWireHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// The endpoints the real SDK would be. Each writes the dialect it was handed, or a fixed answer, so a test
    /// can see both what the middleware did to the request and what it did to the response.
    /// </summary>
    private static void MapStandIns(IEndpointRouteBuilder app)
    {
        // Echoes the request body it was handed, inside a JSON-RPC envelope that carries no result: what the echo
        // says shows the request translation, and whether a `result` appears shows the response repair.
        app.MapPost("/a2a/echo", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var handed = await reader.ReadToEndAsync(context.RequestAborted);
            await WriteJsonAsync(context, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["echo"] = handed });
        });

        // A fixed answer in the SDK's own dialect: a bare task, wrapped in nothing.
        app.MapPost("/a2a/task", async (HttpContext context) =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(new JsonObject
            {
                ["id"] = "t-1",
                ["status"] = new JsonObject { ["state"] = "TASK_STATE_WORKING" },
            }.ToJsonString(), context.RequestAborted);
        });

        // Whatever it is handed, it reports — the request translation, seen from inside.
        app.MapPost("/a2a/tasks/t-1/pushNotificationConfigs", async (HttpContext context) =>
        {
            var handed = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            await WriteJsonAsync(context, new JsonObject { ["seen"] = JsonSerializer.SerializeToNode(handed) });
        });

        // The listing, in the SDK's shape: a wrapper array of split configurations.
        app.MapGet("/a2a/tasks/t-1/pushNotificationConfigs", async (HttpContext context) =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(new JsonObject
            {
                ["configs"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["taskId"] = "t-1",
                        ["configId"] = "c-1",
                        ["config"] = new JsonObject { ["id"] = "c-1", ["url"] = "https://hook.test" },
                    },
                },
            }.ToJsonString(), context.RequestAborted);
        });

        // Three events, the last written without the blank line that ends a frame — the tail a caller receives
        // when the source stops mid-frame.
        app.MapPost("/a2a/message:stream", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/event-stream";
            var body = context.Response.Body;
            await body.WriteAsync(Frame("task", "TASK_STATE_SUBMITTED"), context.RequestAborted);
            await body.WriteAsync(Frame("statusUpdate", "TASK_STATE_WORKING"), context.RequestAborted);
            var tail = Frame("statusUpdate", "TASK_STATE_COMPLETED", blankLine: false);
            await body.WriteAsync(tail, 0, tail.Length, context.RequestAborted);
        });

        // One complete event, pushed out with an explicit flush, and then a tail that is not JSON at all.
        app.MapPost("/a2a/tasks/t-1:subscribe", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/event-stream";
            var body = context.Response.Body;
            await body.WriteAsync(Frame("task", "TASK_STATE_SUBMITTED"), context.RequestAborted);
            await body.FlushAsync(context.RequestAborted);
            await body.WriteAsync(Encoding.UTF8.GetBytes("data: not-json\n"), context.RequestAborted);
        });

        // One complete event and nothing after it: the stream ends on a frame boundary, so there is no tail.
        app.MapPost("/a2a/tasks/t-2:subscribe", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.Body.WriteAsync(Frame("task", "TASK_STATE_SUBMITTED"), context.RequestAborted);
        });

        // Writes through every member a response body has that is not "write the bytes", over a document that is
        // not JSON — which must leave exactly as it was written.
        app.MapPost("/a2a/probe", (HttpContext context) =>
        {
            var body = context.Response.Body;
            body.Write("hello"u8);
            var bytes = Encoding.UTF8.GetBytes(
                $"len={body.Length},pos={body.Position}/{body.CanRead}/{body.CanSeek}/{body.CanWrite}");
            body.Write(bytes, 0, bytes.Length);
            body.Flush();
            return Task.CompletedTask;
        });

        // Tries everything a response body must refuse, and reports what threw.
        app.MapPost("/a2a/unsupported", (HttpContext context) =>
        {
            var body = context.Response.Body;
            var threw = new List<string>();
            try { body.Read(new byte[1], 0, 1); }
            catch (NotSupportedException) { threw.Add("read"); }
            try { _ = body.Seek(0, SeekOrigin.Begin); }
            catch (NotSupportedException) { threw.Add("seek"); }
            try { body.SetLength(0); }
            catch (NotSupportedException) { threw.Add("length"); }
            try { body.Position = 0; }
            catch (NotSupportedException) { threw.Add("position"); }
            context.Response.ContentType = "text/plain";
            var bytes = Encoding.UTF8.GetBytes(string.Join(",", threw));
            body.Write(bytes, 0, bytes.Length);
            return Task.CompletedTask;
        });

        // Writes nothing at all: an answer with no body has nothing to translate.
        app.MapPost("/a2a/empty", (HttpContext context) => Task.CompletedTask);

        // Flushes a document mid-write: a non-streaming answer has nothing to drain, so the flush must be
        // accepted without ending the response.
        app.MapPost("/a2a/flush", async (HttpContext context) =>
        {
            var body = context.Response.Body;
            await body.WriteAsync(Encoding.UTF8.GetBytes("hi"), context.RequestAborted);
            await body.FlushAsync(context.RequestAborted);
        });

        // The token endpoint is ours: the same echo, reached without the middleware.
        app.MapPost("/a2a/token", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var handed = await reader.ReadToEndAsync(context.RequestAborted);
            await WriteJsonAsync(context, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["echo"] = handed });
        });
    }

    private static async Task WriteJsonAsync(HttpContext context, JsonObject envelope)
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(envelope.ToJsonString(), context.RequestAborted);
    }

    /// <summary>One SSE frame as the SDK writes it: <c>data: {envelope}</c>, ended by a blank line unless it is the tail.</summary>
    private static byte[] Frame(string wrapper, string state, bool blankLine = true)
    {
        var payload = wrapper == "task"
            ? new JsonObject { ["id"] = "t-1", ["status"] = new JsonObject { ["state"] = state } }
            : new JsonObject { ["taskId"] = "t-1", ["status"] = new JsonObject { ["state"] = state } };
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["result"] = new JsonObject { [wrapper] = payload },
        };
        return Encoding.UTF8.GetBytes($"data: {envelope.ToJsonString()}\n{(blankLine ? "\n" : "")}");
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string path, string body)
    {
        using var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<string> TextAsync(HttpClient client, string path, string body)
    {
        using var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(Ct);
    }

    /// <summary>The <c>data:</c> payloads of an event stream, in the order they were written.</summary>
    private static async Task<List<string>> EventsAsync(HttpClient client, string path, string body)
    {
        using var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(Ct);
        return [.. text.Split('\n')
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => line[6..])];
    }

    private static JsonElement ResultOf(string @event) =>
        JsonDocument.Parse(@event).RootElement.GetProperty("result");

    [Fact]
    public async Task A_malformed_body_is_handed_to_the_endpoint_untouched_and_answered_as_json_rpc()
    {
        await using var host = await HostAsync();

        var answer = await JsonAsync(host.Client, "/a2a/message:send", "{oops");

        // Nothing to translate: the endpoint saw the bytes as they arrived, and the answer is still a
        // well-formed JSON-RPC response — an envelope with nothing to say carries an empty result.
        Assert.Equal("{oops", answer.GetProperty("echo").GetString());
        Assert.Equal(JsonValueKind.Object, answer.GetProperty("result").ValueKind);
        Assert.Empty(answer.GetProperty("result").EnumerateObject());
    }

    [Fact]
    public async Task A_spec_spelled_rpc_request_is_rewritten_into_the_sdk_s_dialect()
    {
        await using var host = await HostAsync();

        var answer = await JsonAsync(host.Client, "/a2a/echo", SpecRpc);

        var echo = answer.GetProperty("echo").GetString()!;
        Assert.Contains("\"method\":\"SendMessage\"", echo);
        Assert.Contains("\"role\":\"ROLE_USER\"", echo);
        Assert.DoesNotContain("\"kind\"", echo);
        // The repair applies in either dialect: an envelope with nothing in it still carries a result.
        Assert.Equal(JsonValueKind.Object, answer.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task An_sdk_spelled_body_over_http_json_is_left_alone_and_answered_as_it_was_written()
    {
        await using var host = await HostAsync();

        var answer = await JsonAsync(host.Client, "/a2a/message:send", SdkMessage);

        // The SDK's own spelling went in, the SDK's own answer came out: nothing was translated, only enveloped.
        Assert.Equal("TASK_STATE_WORKING", answer.GetProperty("status").GetProperty("state").GetString());
        Assert.False(answer.TryGetProperty("kind", out _));
    }

    [Fact]
    public async Task A_stream_over_http_json_is_translated_event_by_event()
    {
        await using var host = await HostAsync();

        var events = await EventsAsync(host.Client, "/a2a/message:stream", SpecMessage);

        Assert.Equal(3, events.Count);
        var first = ResultOf(events[0]);
        Assert.Equal("task", first.GetProperty("kind").GetString());
        Assert.Equal("submitted", first.GetProperty("status").GetProperty("state").GetString());
        Assert.False(ResultOf(events[1]).GetProperty("final").GetBoolean());
        var last = ResultOf(events[2]);
        Assert.Equal("status-update", last.GetProperty("kind").GetString());
        Assert.Equal("completed", last.GetProperty("status").GetProperty("state").GetString());
        Assert.True(last.GetProperty("final").GetBoolean());
        // Not one of them kept the SDK's spelling of a state.
        Assert.DoesNotContain(events, e => e.Contains("TASK_STATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_resubscription_over_http_json_streams_what_is_complete_and_keeps_the_tail()
    {
        await using var host = await HostAsync();

        var events = await EventsAsync(host.Client, "/a2a/tasks/t-1:subscribe", """{"id":"t-1"}""");

        Assert.Equal(2, events.Count);
        var first = ResultOf(events[0]);
        Assert.Equal("task", first.GetProperty("kind").GetString());
        Assert.Equal("submitted", first.GetProperty("status").GetProperty("state").GetString());
        // The tail was not JSON: it left exactly as it was written.
        Assert.Equal("not-json", events[1]);
    }

    [Fact]
    public async Task A_stream_that_ends_on_a_frame_boundary_writes_no_remainder()
    {
        await using var host = await HostAsync();

        var events = await EventsAsync(host.Client, "/a2a/tasks/t-2:subscribe", """{"id":"t-1"}""");

        // One event, and nothing after it: the source stopped on a frame boundary, so there was no tail to write.
        var first = ResultOf(Assert.Single(events));
        Assert.Equal("task", first.GetProperty("kind").GetString());
        Assert.Equal("submitted", first.GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_cancellation_over_http_json_is_answered_in_the_specified_shape()
    {
        await using var host = await HostAsync();

        var answer = await JsonAsync(host.Client, "/a2a/tasks/t-1:cancel", """{"id":"t-1"}""");

        Assert.Equal("task", answer.GetProperty("kind").GetString());
        Assert.Equal("working", answer.GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_push_configuration_set_over_http_json_is_split_the_way_the_sdk_wants_it()
    {
        await using var host = await HostAsync();

        var answer = await JsonAsync(host.Client, "/a2a/tasks/t-1/pushNotificationConfigs",
            """{"taskId":"t-1","pushNotificationConfig":{"id":"c-1","url":"https://hook.test"}}""");

        var seen = answer.GetProperty("seen");
        Assert.Equal("t-1", seen.GetProperty("taskId").GetString());
        // The specification names the whole object; the SDK wants it split, with an id it will not invent.
        Assert.Equal("c-1", seen.GetProperty("configId").GetString());
        Assert.Equal("https://hook.test", seen.GetProperty("config").GetProperty("url").GetString());
        Assert.False(seen.TryGetProperty("pushNotificationConfig", out _));
    }

    [Fact]
    public async Task Listing_push_configurations_over_http_json_is_answered_in_the_specified_shape()
    {
        await using var host = await HostAsync();

        var answer = await GetJsonAsync(host.Client, "/a2a/tasks/t-1/pushNotificationConfigs");

        var config = answer.GetProperty("configs")[0];
        Assert.Equal("c-1", config.GetProperty("configId").GetString());
        Assert.Equal("https://hook.test", config.GetProperty("pushNotificationConfig").GetProperty("url").GetString());
        Assert.False(config.TryGetProperty("config", out _));
    }

    [Fact]
    public async Task The_translating_body_is_write_only_and_holds_the_document_until_the_end()
    {
        await using var host = await HostAsync();

        var text = await TextAsync(host.Client, "/a2a/probe", "{}");

        // Write-only, and nothing reached the wire until the endpoint was done: the document left as one piece,
        // and a document that is not JSON left untouched.
        Assert.Equal("hellolen=5,pos=5/False/False/True", text);
    }

    [Fact]
    public async Task Reading_repositioning_or_resizing_the_translating_body_is_refused()
    {
        await using var host = await HostAsync();

        var text = await TextAsync(host.Client, "/a2a/unsupported", "{}");

        // A response body is not a place to read from or move around in: every one of them is refused.
        Assert.Equal("read,seek,length,position", text);
    }

    [Fact]
    public async Task An_endpoint_that_writes_nothing_leaves_an_empty_answer()
    {
        await using var host = await HostAsync();

        using var response = await host.Client.PostAsync("/a2a/empty",
            new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

        response.EnsureSuccessStatusCode();
        // Nothing was written, so nothing was translated: the answer is empty rather than an empty JSON document.
        Assert.Equal("", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_flush_on_a_document_answer_is_accepted_without_ending_the_response()
    {
        await using var host = await HostAsync();

        var text = await TextAsync(host.Client, "/a2a/flush", "{}");

        // A document has nothing to drain, so the flush was a no-op that did not close the answer: what was
        // written left at the end, untouched because it is not JSON.
        Assert.Equal("hi", text);
    }

    [Fact]
    public async Task The_token_endpoint_is_ours_and_is_not_translated()
    {
        await using var host = await HostAsync();

        var answer = await JsonAsync(host.Client, "/a2a/token", SpecRpc);

        // The specification's request went in as it was written, and no repair was applied to the answer either:
        // the middleware never touched this endpoint.
        Assert.Contains("\"method\":\"message/send\"", answer.GetProperty("echo").GetString());
        Assert.False(answer.TryGetProperty("result", out _));
    }
}