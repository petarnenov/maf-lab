using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Jev;

/// <summary>
/// What one request to Jev came to: the answer, or why there is none, and how long it took. <paramref name="Skipped"/>
/// is true when an open circuit sent nothing (<see cref="JevClient.CircuitOpen"/>): the caller treats it as any other
/// missing answer and records no request.
/// </summary>
public sealed record JevOutcome(JevResponse? Response, string? Failure, double DurationMs, bool Skipped = false);

/// <summary>
/// One request to Jev's System One endpoint, bounded by a budget and never throwing: a timeout, an error status, a
/// transport failure or a missing key comes back as a <see cref="JevOutcome.Failure"/> the caller can act on and record.
/// Shared by everything that asks Jev (intent classification, screening and the answer check in the api, the relevance
/// judge in retrieval), so the key, the wire shape and the timeout race exist once. It is a singleton holding one
/// <see cref="HttpClient"/> for the life of the process, whose kept-alive connection every request reuses
/// (jev-client-reuse). Every request passes the process's <see cref="JevCircuitBreaker"/>, which skips Jev while it is
/// failing (add-jev-circuit-breaker); a client built without one sends every request.
/// </summary>
public sealed class JevClient(IHttpClientFactory http, JevCredential credential, IOptions<JevOptions> options, JevCircuitBreaker? breaker = null)
{
    public const string HttpClientName = "jev";

    /// <summary>The failure reason of a call an open circuit skipped.</summary>
    public const string CircuitOpen = Maf.Lab.Plugins.Abstractions.DecisionFailures.CircuitOpen;

    // Created once: the named client's handler is never rotated (see AddJevClient), so this instance and its pooled
    // connections are what every request goes through.
    private readonly HttpClient _http = http.CreateClient(HttpClientName);

    public bool IsConfigured => credential.IsConfigured;

    public string Model => options.Value.Model;

    public async Task<JevOutcome> AskAsync(object state, IReadOnlyDictionary<string, object> questions, double timeoutSeconds, CancellationToken ct)
    {
        if (!credential.IsConfigured)
        {
            return new JevOutcome(null, Maf.Lab.Plugins.Abstractions.DecisionFailures.NoKey, 0);
        }
        var ticket = breaker?.Enter() ?? new JevCircuitTicket(true, false);
        if (!ticket.Allowed)
        {
            return new JevOutcome(null, CircuitOpen, 0, Skipped: true);
        }
        // Neutral until shown otherwise: the caller's own cancellation leaves by the exception and says nothing about Jev.
        var result = JevCallResult.Neutral;
        string? failure = null;
        try
        {
            var (outcome, verdict) = await SendAsync(state, questions, timeoutSeconds, ct);
            (result, failure) = (verdict, outcome.Failure);
            return outcome;
        }
        finally
        {
            breaker?.Exit(ticket, result, failure);
        }
    }

    private async Task<(JevOutcome Outcome, JevCallResult Result)> SendAsync(object state, IReadOnlyDictionary<string, object> questions,
        double timeoutSeconds, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var call = PostAsync(state, questions, cts.Token);

            // The token is the transport's cue to stop; the race is what the caller actually waits on, so a client
            // that ignores cancellation delays the answer by the budget and no longer.
            if (await Task.WhenAny(call, Task.Delay(timeout, ct)) != call)
            {
                Forget(call);
                return (new JevOutcome(null, $"timed out after {timeoutSeconds}s", sw.Elapsed.TotalMilliseconds), JevCallResult.Failure);
            }
            var (status, body) = await call;
            if (body is null)
            {
                // The retry handler's notion of transient decides what counts against Jev, so the two cannot drift apart.
                var transient = JevRetryHandler.IsTransient((HttpStatusCode)status);
                return (new JevOutcome(null, $"rejected ({status})", sw.Elapsed.TotalMilliseconds),
                    transient ? JevCallResult.Failure : JevCallResult.Neutral);
            }
            return (new JevOutcome(body, null, sw.Elapsed.TotalMilliseconds), JevCallResult.Success);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (new JevOutcome(null, $"timed out after {timeoutSeconds}s", sw.Elapsed.TotalMilliseconds), JevCallResult.Failure);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transport error is Jev being unreachable; anything else (an unreadable body) is not evidence either way.
            return (new JevOutcome(null, ex.GetType().Name, sw.Elapsed.TotalMilliseconds),
                ex is HttpRequestException ? JevCallResult.Failure : JevCallResult.Neutral);
        }
    }

    private async Task<(int Status, JevResponse? Body)> PostAsync(object state, IReadOnlyDictionary<string, object> questions, CancellationToken ct)
    {
        var request = new JevRequest(options.Value.Model, state, questions);
        // Buffered with a Content-Length rather than streamed chunked: not every server in the path (the CI stub, for
        // one) reads a chunked request.
        using var content = new StringContent(JsonSerializer.Serialize(request, JevRequest.Json), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("v1/systemone", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            return ((int)response.StatusCode, null);
        }
        return ((int)response.StatusCode, await response.Content.ReadFromJsonAsync<JevResponse>(JevRequest.Json, ct));
    }

    /// <summary>Keeps an abandoned call from surfacing as an unobserved exception.</summary>
    private static void Forget(Task task) => _ = task.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
}

public static class JevClientServiceCollectionExtensions
{
    /// <summary>
    /// Options, the credential, the named client that alone carries the key, and the start-up warm-up. Idempotent: the
    /// api registers both the retrieval core and the intent classifier, and each asks for this. The credential reads the
    /// configuration it is given here, so a host that does not put <see cref="IConfiguration"/> in its container still
    /// gets the key.
    /// </summary>
    /// <remarks>
    /// The named client's handler is pinned (infinite factory lifetime) and pooled by <see cref="SocketsHttpHandler"/>
    /// itself: kept alive between requests, recycled after <see cref="JevOptions.PooledConnectionLifetimeMinutes"/> so a
    /// DNS change is still seen. The retry handler sits outside the auth handler, so every attempt is authorised, logged
    /// and traced on its own.
    /// </remarks>
    public static IServiceCollection AddJevClient(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(JevCredential)))
        {
            return services;
        }
        services.Configure<JevOptions>(configuration.GetSection(JevOptions.Section));
        services.AddSingleton(sp => new JevCredential(configuration, sp.GetRequiredService<ILogger<JevCredential>>()));
        services.TryAddTransient<JevAuthHandler>();
        services.TryAddTransient<JevRetryHandler>();
        services.AddHttpClient(JevClient.HttpClientName, (sp, client) =>
            {
                var endpoint = sp.GetRequiredService<IOptions<JevOptions>>().Value.Endpoint;
                client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
            })
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(sp.GetRequiredService<IOptions<JevOptions>>().Value.PooledConnectionLifetimeMinutes),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                EnableMultipleHttp2Connections = true,
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddHttpMessageHandler<JevRetryHandler>()
            .AddHttpMessageHandler<JevAuthHandler>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<JevCircuitBreaker>();
        services.TryAddSingleton<JevClient>();
        services.AddHostedService<JevWarmup>();
        return services;
    }
}
