using Maf.Lab.Domain.SharedState;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Maf.Lab.Hosting;

public sealed class SharedStateOptions
{
    public const string Section = "SharedState";

    /// <summary>Where the shared store is. Empty means this service does not use one.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>How long a run's state is kept after the run has ended, so a client can still come back to it.</summary>
    public TimeSpan RunGrace { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long a processed idempotency key is remembered — as long as a caller could reasonably retry.</summary>
    public TimeSpan IdempotencyWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How long message content is kept. The compliance retention, and not the trace's.</summary>
    public TimeSpan ConversationRetention { get; set; } = TimeSpan.FromDays(90);
}

/// <summary>
/// The one place every replica reads. Stateless does not mean without state; it means the state is somewhere all
/// the replicas can see, which for this system is here.
///
/// A service that needs it and cannot reach it does not start. A replica that started anyway would answer some
/// requests correctly and lose others, which is worse than not being in the pool at all.
/// </summary>
public static class SharedState
{
    /// <summary>Configured address, or null when this service keeps nothing shared.</summary>
    public static string? AddressOf(IConfiguration configuration)
    {
        var value = configuration[$"{SharedStateOptions.Section}:ConnectionString"];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Registers the connection to the shared store, when one is configured.
    ///
    /// Whether this service may run without it is not decided here but by <see cref="RequireSharedState{T}"/>:
    /// what a service needs is a store, not a particular one. That is what lets a test put its own in place and
    /// still prove that a service with no store at all refuses to start.
    /// </summary>
    public static IHostApplicationBuilder AddSharedState(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<SharedStateOptions>(
            builder.Configuration.GetSection(SharedStateOptions.Section));
        // Always registered, so anything that reports health can ask. It answers for a service that keeps
        // nothing shared as well as for one that does.
        builder.Services.AddSingleton<SharedStateHealth>();

        var address = AddressOf(builder.Configuration);
        if (address is null)
        {
            return builder;
        }

        // One multiplexer for the process: it is the connection pool, and a second would just be a second pool.
        // AbortOnConnectFail is left off so a store that comes back is reconnected to without a restart.
        var options = ConfigurationOptions.Parse(address);
        options.AbortOnConnectFail = false;
        options.ClientName = InstanceIdentity.Name;

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        builder.Services.AddSingleton<IRunStateStore, Stores.RedisRunStateStore>();
        builder.Services.AddSingleton<IIdempotencyStore, Stores.RedisIdempotencyStore>();
        return builder;
    }

    /// <summary>
    /// Says that this service cannot serve without <typeparamref name="T"/>, and refuses to start without it.
    /// A replica that started anyway would answer some requests correctly and lose others, which is worse than
    /// not being in the pool: the balancer already routes around a replica that is not there.
    /// </summary>
    public static IHostApplicationBuilder RequireSharedState<T>(this IHostApplicationBuilder builder)
        where T : class
    {
        builder.Services.AddSingleton<IHostedService>(sp => new SharedStateRequirement<T>(sp));
        return builder;
    }
}

/// <summary>Fails the host's start when the store a service declared it needs is not there.</summary>
internal sealed class SharedStateRequirement<T>(IServiceProvider services) : IHostedService
    where T : class
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Not registered, or registered over a connection that is not configured — both are the same answer to
        // the only question that matters: can this replica reach the state it must share?
        var resolved = Resolve();
        if (resolved is null)
        {
            throw new InvalidOperationException(
                $"This service needs {typeof(T).Name} from the shared state store. " +
                $"Configure {SharedStateOptions.Section}:ConnectionString, or register one.");
        }
        return Task.CompletedTask;
    }

    private T? Resolve()
    {
        try
        {
            return services.GetService<T>();
        }
        catch (InvalidOperationException)
        {
            // Registered, but something it is built from is not.
            return null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Whether this replica can see the shared store. The balancer routes around a replica that says it cannot, and
/// the same replica says it can again once the store answers — without being restarted.
/// </summary>
public sealed class SharedStateHealth(IServiceProvider services)
{
    /// <summary>False only when there is a store to reach and it cannot be reached.</summary>
    public async Task<(bool Ok, string? Reason)> CheckAsync(CancellationToken ct)
    {
        if (services.GetService<IConnectionMultiplexer>() is not { } redis)
        {
            // Nothing shared here. A service that needed one would not have started.
            return (true, null);
        }
        try
        {
            var database = redis.GetDatabase();
            await database.PingAsync().WaitAsync(TimeSpan.FromSeconds(2), ct);
            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type, never the address or the credentials: a health endpoint is read by anyone who can reach it.
            return (false, $"shared state unreachable ({ex.GetType().Name})");
        }
    }
}
