using System.Net;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Plugins.Jev;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The Jev transport (jev-client-reuse): one client for every request, retries of transient failures inside the
/// caller's budget, a log line per attempt with its status code, and one warm-up request at start-up.
/// </summary>
public class JevClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyDictionary<string, object> Questions = new Dictionary<string, object>
    {
        ["q"] = new JevNoulQuestion("Is `text` about fees?"),
    };

    private static JevCredential Credential(string? key = FakeJev.TestKey) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = key }).Build(),
        LoggerFactory.Create(_ => { }).CreateLogger<JevCredential>());

    /// <summary>The handler chain the host builds: retry outermost, then the auth handler, then the transport.</summary>
    private static (JevClient Client, CountingJevClientFactory Factory, List<TimeSpan> Delays) Build(HttpMessageHandler transport,
        CapturingLoggerProvider logs, JevOptions? options = null, bool realDelay = false)
    {
        var o = Options.Create(options ?? new JevOptions());
        var credential = Credential();
        var delays = new List<TimeSpan>();
        var loggers = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var retry = new JevRetryHandler(o, loggers.CreateLogger<JevRetryHandler>())
        {
            InnerHandler = new JevAuthHandler(credential) { InnerHandler = transport },
            Delay = realDelay ? Task.Delay : (d, _) => { delays.Add(d); return Task.CompletedTask; },
        };
        var factory = new CountingJevClientFactory(new HttpClient(retry) { BaseAddress = new Uri("https://jev.test/") });
        return (new JevClient(factory, credential, o), factory, delays);
    }

    [Fact]
    public async Task Every_request_goes_through_the_one_client_created_once()
    {
        var jev = new FakeJev();
        var (client, factory, _) = Build(jev, new CapturingLoggerProvider());

        await client.AskAsync(new { text = "a" }, Questions, 2, Ct);
        await client.AskAsync(new { text = "b" }, Questions, 2, Ct);

        Assert.Equal(1, factory.Created);
        Assert.Equal(2, jev.Requests.Count);
    }

    [Fact]
    public void Every_caller_resolves_the_same_client_and_the_warm_up_is_registered()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = FakeJev.TestKey })
            .Build();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostApplicationLifetime>(new StartedLifetime());
        new JevPlugin().ConfigureProvider(services, configuration);
        services.AddDecisionCallers(configuration);
        using var sp = services.BuildServiceProvider();

        var one = sp.GetRequiredService<JevClient>();
        Assert.Same(one, sp.GetRequiredService<JevClient>());
        Assert.IsType<JevDecisionEngine>(sp.GetRequiredService<Maf.Lab.Plugins.Abstractions.IDecisionEngine>());
        // The classifier, the guard and the answer check are singletons over the one engine and its one client.
        Assert.NotNull(sp.GetRequiredService<IIntentClassifier>());
        Assert.NotNull(sp.GetRequiredService<DecisionGuard>());
        Assert.NotNull(sp.GetRequiredService<DecisionAnswerCheck>());
        Assert.Contains(sp.GetServices<IHostedService>(), s => s is JevWarmup);
    }

    [Fact]
    public async Task The_host_pipeline_retries_a_transient_status()
    {
        var jev = new FakeJev { Status = HttpStatusCode.ServiceUnavailable };
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [JevCredential.EnvironmentVariable] = FakeJev.TestKey,
            ["Jev:Endpoint"] = "https://jev.test",
            ["Jev:RetryDelayMs"] = "1",
        }).Build();
        services.AddLogging();
        services.AddJevClient(configuration);
        services.AddHttpClient(JevClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => jev);
        using var sp = services.BuildServiceProvider();

        var outcome = await sp.GetRequiredService<JevClient>().AskAsync(new { text = "a" }, Questions, 2, Ct);

        Assert.Equal("rejected (503)", outcome.Failure);
        Assert.Equal(2, jev.Requests.Count);
        Assert.All(jev.Requests, r => Assert.Equal($"Bearer {FakeJev.TestKey}", r.Authorization));
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_the_retry_answers()
    {
        var logs = new CapturingLoggerProvider();
        var transport = new Scripted(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var (client, _, delays) = Build(transport, logs);

        var outcome = await client.AskAsync(new { text = "a" }, Questions, 2, Ct);

        Assert.Null(outcome.Failure);
        Assert.NotNull(outcome.Response);
        Assert.Equal(2, transport.Sent);
        Assert.Single(delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task A_non_transient_status_is_sent_once(HttpStatusCode status)
    {
        var transport = new Scripted(status, HttpStatusCode.OK);
        var (client, _, delays) = Build(transport, new CapturingLoggerProvider());

        var outcome = await client.AskAsync(new { text = "a" }, Questions, 2, Ct);

        Assert.Equal($"rejected ({(int)status})", outcome.Failure);
        Assert.Equal(1, transport.Sent);
        Assert.Empty(delays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Retries_stop_after_the_configured_number(int retries)
    {
        var transport = new Scripted((HttpStatusCode)529);
        var (client, _, delays) = Build(transport, new CapturingLoggerProvider(), new JevOptions { MaxRetries = retries });

        var outcome = await client.AskAsync(new { text = "a" }, Questions, 2, Ct);

        Assert.Equal("rejected (529)", outcome.Failure);
        Assert.Equal(1 + retries, transport.Sent);
        Assert.Equal(retries, delays.Count);
    }

    [Fact]
    public async Task A_transport_error_is_retried()
    {
        var transport = new Scripted(new HttpRequestException("connection refused by jev.internal"), HttpStatusCode.OK);
        var logs = new CapturingLoggerProvider();
        var (client, _, _) = Build(transport, logs);

        var outcome = await client.AskAsync(new { text = "a" }, Questions, 2, Ct);

        Assert.Null(outcome.Failure);
        Assert.Equal(2, transport.Sent);
        var failed = Assert.Single(logs.Messages, m => m.Contains("Jev attempt 1/2 failed"));
        Assert.Contains("HttpRequestException", failed);
        Assert.DoesNotContain("jev.internal", failed);
    }

    [Fact]
    public async Task A_retry_waits_the_servers_retry_after()
    {
        var transport = new Scripted(HttpStatusCode.TooManyRequests, HttpStatusCode.OK) { RetryAfter = TimeSpan.FromMilliseconds(250) };
        var (client, _, delays) = Build(transport, new CapturingLoggerProvider());

        await client.AskAsync(new { text = "a" }, Questions, 2, Ct);

        Assert.Equal(TimeSpan.FromMilliseconds(250), Assert.Single(delays));
    }

    [Fact]
    public async Task No_attempt_is_sent_once_the_budget_is_spent()
    {
        var transport = new Scripted(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var (client, _, _) = Build(transport, new CapturingLoggerProvider(), new JevOptions { RetryDelayMs = 10_000 }, realDelay: true);

        var started = DateTime.UtcNow;
        var outcome = await client.AskAsync(new { text = "a" }, Questions, 0.3, Ct);

        Assert.StartsWith("timed out", outcome.Failure);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
        await Task.Delay(100, Ct);
        Assert.Equal(1, transport.Sent);
    }

    [Fact]
    public async Task Every_attempt_is_logged_with_its_status_code_and_no_content()
    {
        var logs = new CapturingLoggerProvider();
        var (client, _, _) = Build(new Scripted(HttpStatusCode.TooManyRequests, HttpStatusCode.OK), logs);

        await client.AskAsync(new { text = "secret question about fees" }, Questions, 2, Ct);

        var lines = logs.Messages.Where(m => m.Contains(nameof(JevRetryHandler))).ToList();
        Assert.Contains(lines, m => m.Contains("Jev attempt 1/2 → 429"));
        Assert.Contains(lines, m => m.Contains("Jev retrying after 429"));
        Assert.Contains(lines, m => m.Contains("Jev attempt 2/2 → 200"));
        Assert.All(logs.Messages, m =>
        {
            Assert.DoesNotContain(FakeJev.TestKey, m);
            Assert.DoesNotContain("secret question", m);
            Assert.DoesNotContain("about fees", m);
        });
    }

    private static (JevWarmup Warmup, FakeJev Jev, CapturingLoggerProvider Logs) Warmup(JevOptions options, string? key = FakeJev.TestKey,
        FakeJev? jev = null)
    {
        jev ??= new FakeJev();
        var logs = new CapturingLoggerProvider();
        var loggers = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var credential = Credential(key);
        var client = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        var o = Options.Create(options);
        var warmup = new JevWarmup(new JevClient(new CountingJevClientFactory(client), credential, o), o, new StartedLifetime(),
            loggers.CreateLogger<JevWarmup>());
        return (warmup, jev, logs);
    }

    private static async Task RunAsync(JevWarmup warmup)
    {
        await warmup.StartAsync(Ct);
        await warmup.ExecuteTask!;
        await warmup.StopAsync(Ct);
    }

    [Fact]
    public async Task Warm_up_sends_one_fixed_request_and_logs_its_outcome()
    {
        var (warmup, jev, logs) = Warmup(new JevOptions());

        await RunAsync(warmup);

        var (authorization, body) = Assert.Single(jev.Requests);
        Assert.Equal($"Bearer {FakeJev.TestKey}", authorization);
        Assert.Contains("\"text\":\"warm-up\"", body);
        Assert.Contains(logs.Messages, m => m.Contains("Jev warm-up answered by jev-1.13.0"));
    }

    [Fact]
    public async Task Warm_up_is_skipped_without_a_key()
    {
        var (warmup, jev, _) = Warmup(new JevOptions(), key: null);

        await RunAsync(warmup);

        Assert.Empty(jev.Requests);
    }

    [Fact]
    public async Task Warm_up_is_skipped_when_disabled()
    {
        var (warmup, jev, _) = Warmup(new JevOptions { WarmUp = false });

        await RunAsync(warmup);

        Assert.Empty(jev.Requests);
    }

    [Fact]
    public async Task A_failed_warm_up_is_logged_and_does_not_throw()
    {
        var (warmup, jev, logs) = Warmup(new JevOptions(), jev: new FakeJev { Status = HttpStatusCode.Unauthorized });

        await RunAsync(warmup);

        Assert.Single(jev.Requests);
        var line = Assert.Single(logs.Messages, m => m.Contains("Jev warm-up failed"));
        Assert.Contains("rejected (401)", line);
    }
}

internal sealed class CountingJevClientFactory(HttpClient client) : IHttpClientFactory
{
    public int Created { get; private set; }

    public HttpClient CreateClient(string name)
    {
        Created++;
        return client;
    }
}

/// <summary>Answers each request with the next scripted step (a status or an exception); the last step repeats.</summary>
file sealed class Scripted(params object[] steps) : HttpMessageHandler
{
    private int _sent;

    public int Sent => _sent;

    public TimeSpan? RetryAfter { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var step = steps[Math.Min(Interlocked.Increment(ref _sent), steps.Length) - 1];
        if (step is Exception ex)
        {
            throw ex;
        }
        var status = (HttpStatusCode)step;
        var response = new HttpResponseMessage(status);
        if (status == HttpStatusCode.OK)
        {
            response.Content = new StringContent("""{"model":"jev-1.13.0","answers":{"q":{"type":"noul","noul":0.9}}}""",
                System.Text.Encoding.UTF8, "application/json");
        }
        if (RetryAfter is { } after)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(after);
        }
        return Task.FromResult(response);
    }
}

file sealed class StartedLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => new(canceled: true);
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication()
    {
    }
}
