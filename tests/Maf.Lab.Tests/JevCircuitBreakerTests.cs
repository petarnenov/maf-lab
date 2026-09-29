using System.Net;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Tests;

/// <summary>
/// The circuit breaker in front of every Jev request (add-jev-circuit-breaker): consecutive transient failures open it,
/// an open circuit skips Jev without sending anything, one probe after the open period closes or re-opens it, and only
/// state changes are logged.
/// </summary>
public class JevCircuitBreakerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, object> Questions = new Dictionary<string, object>
    {
        ["q"] = new JevNoulQuestion("Is `text` about fees?"),
    };

    /// <summary>A breaker that opens on the first transient failure and stays open for the test, for callers' tests.</summary>
    internal static JevCircuitBreaker OpensOnFirstFailure() => new(
        Options.Create(new JevOptions { Breaker = new JevBreakerOptions { FailureThreshold = 1, OpenSeconds = 600 } }),
        TimeProvider.System, LoggerFactory.Create(_ => { }).CreateLogger<JevCircuitBreaker>());

    private sealed record Rig(JevClient Client, FakeJev Jev, MovableTime Time, CapturingLoggerProvider Logs);

    /// <summary>The host's chain without retries (so a request is one attempt), behind a breaker on a movable clock.</summary>
    private static Rig Build(int threshold = 3, double openSeconds = 30, string? key = FakeJev.TestKey)
    {
        var o = Options.Create(new JevOptions { MaxRetries = 0, Breaker = new JevBreakerOptions { FailureThreshold = threshold, OpenSeconds = openSeconds } });
        var logs = new CapturingLoggerProvider();
        var loggers = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var credential = new JevCredential(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [JevCredential.EnvironmentVariable] = key }).Build(),
            loggers.CreateLogger<JevCredential>());
        var jev = new FakeJev();
        var time = new MovableTime(Start);
        var breaker = new JevCircuitBreaker(o, time, loggers.CreateLogger<JevCircuitBreaker>());
        var http = new HttpClient(new JevAuthHandler(credential) { InnerHandler = jev }) { BaseAddress = new Uri("https://jev.test/") };
        return new Rig(new JevClient(new CountingJevClientFactory(http), credential, o, breaker), jev, time, logs);
    }

    private static Task<JevOutcome> Ask(Rig rig, double timeoutSeconds = 2) =>
        rig.Client.AskAsync(new { text = "warm" }, Questions, timeoutSeconds, Ct);

    private static async Task FailTimes(Rig rig, int times, HttpStatusCode status = HttpStatusCode.ServiceUnavailable)
    {
        rig.Jev.Status = status;
        for (var i = 0; i < times; i++)
        {
            await Ask(rig);
        }
    }

    [Fact]
    public async Task Consecutive_transient_failures_open_the_circuit_and_nothing_is_sent_while_open()
    {
        var rig = Build();
        await FailTimes(rig, 3);

        var skipped = await Ask(rig);

        Assert.Equal(3, rig.Jev.Requests.Count);
        Assert.True(skipped.Skipped);
        Assert.Null(skipped.Response);
        Assert.Equal(JevClient.CircuitOpen, skipped.Failure);
        Assert.Equal(0, skipped.DurationMs);
    }

    [Fact]
    public async Task Consecutive_timeouts_open_the_circuit()
    {
        var rig = Build();
        rig.Jev.Hang = TimeSpan.FromSeconds(2);
        for (var i = 0; i < 3; i++)
        {
            Assert.StartsWith("timed out", (await Ask(rig, timeoutSeconds: 0.05)).Failure);
        }

        Assert.True((await Ask(rig, timeoutSeconds: 0.05)).Skipped);
        Assert.Equal(3, rig.Jev.Requests.Count);
    }

    [Fact]
    public async Task A_success_resets_the_count()
    {
        var rig = Build();
        await FailTimes(rig, 2);
        rig.Jev.Status = null;
        Assert.NotNull((await Ask(rig)).Response);
        await FailTimes(rig, 2);

        var next = await Ask(rig);

        Assert.False(next.Skipped);
        Assert.Equal(6, rig.Jev.Requests.Count);
    }

    [Fact]
    public async Task A_successful_probe_after_the_open_period_closes_the_circuit()
    {
        var rig = Build();
        await FailTimes(rig, 3);
        rig.Time.SetUtcNow(Start.AddSeconds(29));
        Assert.True((await Ask(rig)).Skipped);

        rig.Time.SetUtcNow(Start.AddSeconds(31));
        rig.Jev.Status = null;
        var probe = await Ask(rig);
        var after = await Ask(rig);

        Assert.NotNull(probe.Response);
        Assert.NotNull(after.Response);
        Assert.Equal(5, rig.Jev.Requests.Count);
    }

    [Fact]
    public async Task A_failed_probe_opens_the_circuit_again_for_the_open_period()
    {
        var rig = Build();
        await FailTimes(rig, 3);
        rig.Time.SetUtcNow(Start.AddSeconds(31));

        var probe = await Ask(rig);
        var skipped = await Ask(rig);
        rig.Time.SetUtcNow(Start.AddSeconds(60));
        var stillSkipped = await Ask(rig);

        Assert.Equal("rejected (503)", probe.Failure);
        Assert.True(skipped.Skipped);
        Assert.True(stillSkipped.Skipped);
        Assert.Equal(4, rig.Jev.Requests.Count);
    }

    [Fact]
    public async Task Only_one_probe_is_in_flight()
    {
        var rig = Build();
        await FailTimes(rig, 3);
        rig.Time.SetUtcNow(Start.AddSeconds(31));
        rig.Jev.Status = null;
        rig.Jev.Hang = TimeSpan.FromMilliseconds(300);

        var probe = Ask(rig);
        var second = await Ask(rig);

        Assert.True(second.Skipped);
        Assert.NotNull((await probe).Response);
        Assert.Equal(4, rig.Jev.Requests.Count);
    }

    [Fact]
    public async Task A_non_transient_rejection_never_opens_the_circuit()
    {
        var rig = Build();
        await FailTimes(rig, 5, HttpStatusCode.Unauthorized);

        var next = await Ask(rig);

        Assert.False(next.Skipped);
        Assert.Equal("rejected (401)", next.Failure);
        Assert.Equal(6, rig.Jev.Requests.Count);
    }

    [Fact]
    public async Task No_key_fails_as_before_and_leaves_the_breaker_alone()
    {
        var rig = Build(key: null);
        for (var i = 0; i < 5; i++)
        {
            var outcome = await Ask(rig);
            Assert.Equal("no key", outcome.Failure);
            Assert.False(outcome.Skipped);
        }
        Assert.DoesNotContain(rig.Logs.Messages, m => m.Contains("circuit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_neutral()
    {
        var rig = Build();
        await FailTimes(rig, 2);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Client.AskAsync(new { text = "x" }, Questions, 2, cancelled.Token));

        // Still two failures, not three: the next call is sent.
        Assert.False((await Ask(rig)).Skipped);
    }

    [Fact]
    public async Task Threshold_zero_disables_the_breaker()
    {
        var rig = Build(threshold: 0);
        await FailTimes(rig, 10);

        Assert.False((await Ask(rig)).Skipped);
        Assert.Equal(11, rig.Jev.Requests.Count);
    }

    [Fact]
    public async Task State_changes_are_logged_once_and_skipped_calls_not_at_all()
    {
        var rig = Build();
        await FailTimes(rig, 3);
        for (var i = 0; i < 40; i++)
        {
            await Ask(rig);
        }
        rig.Time.SetUtcNow(Start.AddSeconds(31));
        rig.Jev.Status = null;
        await Ask(rig);

        var circuit = rig.Logs.Messages.Where(m => m.Contains("circuit", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, circuit.Count);
        Assert.Contains("opened after 3 consecutive failures (last: rejected (503))", circuit[0]);
        Assert.Contains("half-open", circuit[1]);
        Assert.Contains("40 calls skipped", circuit[2]);
        Assert.DoesNotContain(rig.Logs.Messages, m => m.Contains(FakeJev.TestKey, StringComparison.Ordinal) || m.Contains("warm", StringComparison.Ordinal));
    }

    [Fact]
    public void The_breaker_binds_from_configuration_and_the_host_injects_it()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [JevCredential.EnvironmentVariable] = FakeJev.TestKey,
            ["Jev:Breaker:FailureThreshold"] = "5",
            ["Jev:Breaker:OpenSeconds"] = "12.5",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJevClient(configuration);
        using var sp = services.BuildServiceProvider();

        var breaker = sp.GetRequiredService<IOptions<JevOptions>>().Value.Breaker;
        Assert.Equal(5, breaker.FailureThreshold);
        Assert.Equal(12.5, breaker.OpenSeconds);
        Assert.Same(sp.GetRequiredService<JevCircuitBreaker>(), sp.GetRequiredService<JevCircuitBreaker>());
        Assert.NotNull(sp.GetRequiredService<JevClient>());
    }

    [Fact]
    public void Defaults_are_three_failures_and_thirty_seconds()
    {
        var o = new JevOptions();
        Assert.Equal(3, o.Breaker.FailureThreshold);
        Assert.Equal(30, o.Breaker.OpenSeconds);
    }
}
