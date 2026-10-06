using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Maf.Lab.Api.Coverage;
using Maf.Lab.Api.Storage;
using Maf.Lab.Hosting.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;

namespace Maf.Lab.Tests;

/// <summary>
/// A host that stops ends its background work before it disposes the services that work uses (stop-anything): nothing
/// it started is left writing through a disposed database or client.
/// </summary>
public sealed class HostShutdownTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stopped_host_waits_for_the_runs_its_follower_holds()
    {
        using var agent = new BlackHole();
        var repo = await TempGitRepo.CreateAsync(new Dictionary<string, string> { ["README.md"] = "lab\n" }, Ct);
        var logs = new RecordingLoggerProvider();
        var api = CoverageApi.Create(repo, extra: new Dictionary<string, string?>
        {
            // An agent that accepts and never answers: the run is caught being followed when the host stops.
            ["TestAgent:BaseUrl"] = $"http://127.0.0.1:{agent.Port}/",
            ["TestAgent:ClientId"] = "maf-lab-assistant",
            ["TestAgent:ClientSecret"] = "assistant-secret",
            ["TestAgent:FollowerPollEvery"] = "00:00:00.050",
        });
        var coverage = api.ConfigureTestServices;
        api.ConfigureTestServices = s =>
        {
            coverage?.Invoke(s);
            s.AddSingleton<ILoggerProvider>(logs);
        };
        var follower = api.Services.GetRequiredService<RunFollower>();
        await using (var db = await api.Services.GetRequiredService<IDbContextFactory<MafDbContext>>().CreateDbContextAsync(Ct))
        {
            db.TestGenRuns.Add(new TestGenRunRow
            {
                Id = "r_held", Path = "src/Lab/Calc.cs", Toolchain = "dotnet", CommitSha = "0000000", TargetPct = 85,
                Model = "glm-5.3:cloud", TaskId = "t-held", State = TestGenRunState.Working, MaxAttempts = 5,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, CreatedBy = "alice",
            });
            await db.SaveChangesAsync(Ct);
        }
        await agent.Reached.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Equal(1, follower.InFlight);

        await api.DisposeAsync();

        Assert.Equal(0, follower.InFlight);
        Assert.DoesNotContain(logs.Lines, l => l.Contains(nameof(ObjectDisposedException), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_disposed_heartbeat_waits_for_the_beat_in_flight()
    {
        var beat = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beats = 0;
        var store = Substitute.For<IDatabase>();
        store.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), RunOwnerHeartbeat.Ttl).Returns(_ =>
        {
            Interlocked.Increment(ref beats);
            started.TrySetResult();
            return beat.Task;
        });
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(store);
        using var stopped = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopped.Returns(stopped.Token);
        var heartbeat = new RunOwnerHeartbeat(redis, lifetime);
        await heartbeat.StartAsync(Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await stopped.CancelAsync();
        var disposing = heartbeat.DisposeAsync().AsTask();
        // The beat has not answered: disposing is still waiting on it, so the multiplexer is not yet disposed under it.
        Assert.False(disposing.IsCompleted);

        beat.SetResult(true);
        await disposing.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(1, Volatile.Read(ref beats));
    }

    /// <summary>Accepts connections and never says a word: whatever talks to it waits.</summary>
    private sealed class BlackHole : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly ConcurrentBag<TcpClient> held = [];
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlackHole()
        {
            listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        /// <summary>Completes once something has connected: whatever did is now waiting on it.</summary>
        public Task Reached => reached.Task;

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    held.Add(await listener.AcceptTcpClientAsync());
                    reached.TrySetResult();
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
            {
                // Stopped with the test.
            }
        }

        public void Dispose()
        {
            listener.Stop();
            foreach (var client in held)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>Every line the host logs, with its exception's type, so a test can say what was never logged.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> lines = new();

        public IEnumerable<string> Lines => lines;

        public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, lines);

        public void Dispose()
        {
        }

        private sealed class Recorder(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception?.GetType().Name}");
        }
    }
}
