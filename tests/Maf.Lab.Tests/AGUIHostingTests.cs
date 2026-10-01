using System.Net;
using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AGUI.Abstractions;
using Maf.Lab.Api.Agent.AGUI;
using AGUI.Server;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>What the official AG-UI server writes, once set up by <see cref="AGUIHosting"/>, is what the client reads.</summary>
public sealed class AGUIHostingTests
{
    [Fact]
    public async Task Run_started_carries_no_null_optional_fields()
    {
        await using var host = await EchoHost.StartAsync();

        var events = await host.RunAsync("hello");

        var started = events.First(e => e.GetProperty("type").GetString() == "RUN_STARTED");
        Assert.All(started.EnumerateObject(), p => Assert.NotEqual(JsonValueKind.Null, p.Value.ValueKind));
        Assert.False(started.TryGetProperty("parentRunId", out _));
    }

    [Fact]
    public async Task No_event_carries_the_model_update_it_came_from()
    {
        await using var host = await EchoHost.StartAsync();

        var events = await host.RunAsync("hello");

        Assert.Contains(events, e => e.GetProperty("type").GetString() == "TEXT_MESSAGE_CONTENT");
        Assert.All(events, e => Assert.False(e.TryGetProperty("rawEvent", out _), e.GetRawText()));
    }

    [Fact]
    public async Task An_interrupt_finishes_the_run_paused_rather_than_in_error()
    {
        await using var host = await EchoHost.StartAsync();

        var events = await host.RunAsync(EchoChatClient.AskTrigger);

        Assert.DoesNotContain(events, e => e.GetProperty("type").GetString() == "RUN_ERROR");
        var finished = Assert.Single(events, e => e.GetProperty("type").GetString() == "RUN_FINISHED");
        var outcome = finished.GetProperty("outcome");
        Assert.Equal("interrupt", outcome.GetProperty("type").GetString());
        Assert.Equal("ask-1", outcome.GetProperty("interrupts")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_caller_without_a_token_is_refused()
    {
        await using var host = await EchoHost.StartAsync();

        using var response = await host.PostAsync("hello", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>A trivial agent behind the official AG-UI server: it says back what it was told.</summary>
internal sealed class EchoHost : IAsyncDisposable
{
    public const string Token = "echo-token";
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private EchoHost(WebApplication app)
    {
        _app = app;
        _client = app.GetTestClient();
    }

    public static async Task<EchoHost> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAGUIHosting();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, BearerOnly>("Test", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var agent = new EchoChatClient().AsAIAgent(new ChatClientAgentOptions { Name = "echo" });
        app.MapAgent("/echo", agent, new AGUIStreamOptions());
        await app.StartAsync();
        return new EchoHost(app);
    }

    public async Task<HttpResponseMessage> PostAsync(string text, string? token = Token)
    {
        var body = JsonSerializer.Serialize(new
        {
            threadId = "t-1",
            runId = $"r-{Guid.NewGuid():N}",
            messages = new[] { new { id = "m-1", role = "user", content = text } },
            tools = Array.Empty<object>(),
            context = Array.Empty<object>(),
            state = new { },
            forwardedProps = new { },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>Every event of one run, as JSON, in order.</summary>
    public async Task<IReadOnlyList<JsonElement>> RunAsync(string text)
    {
        using var response = await PostAsync(text);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        var events = new List<JsonElement>();
        await foreach (var item in SseParser.Create(stream).EnumerateAsync())
        {
            events.Add(JsonDocument.Parse(item.Data).RootElement.Clone());
        }
        return events;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class BearerOnly(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers.Authorization.ToString() == $"Bearer {Token}"
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u-1")], "Test")), "Test"))
                : AuthenticateResult.NoResult());
    }
}

/// <summary>Echoes the last user message; asks a question (an interrupt) when told <see cref="AskTrigger"/>.</summary>
internal sealed class EchoChatClient : IChatClient
{
    public const string AskTrigger = "ask me";

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var said = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        if (said == AskTrigger)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [new InterruptRequestContent("ask-1") { Reason = "input_required", Message = "Which account?" }]);
            yield break;
        }
        yield return new ChatResponseUpdate(ChatRole.Assistant, $"echo: {said}") { MessageId = "a-1" };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
