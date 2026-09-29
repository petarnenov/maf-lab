using Microsoft.Extensions.Options;

namespace Maf.Lab.Retrieval.Jev;

/// <summary>
/// One Jev request once the host has started, so the first turn finds DNS resolved, TLS negotiated and a kept-alive
/// connection in the shared client's pool (jev-client-reuse). Background only: it waits for
/// <see cref="IHostApplicationLifetime.ApplicationStarted"/>, never throws, and its outcome is one log line. The state
/// is fixed text — never user content — and the request goes through <see cref="JevClient"/> directly, so no turn trace
/// records it and the Jev statistics do not count it.
/// </summary>
public sealed class JevWarmup(JevClient jev, IOptions<JevOptions> options, IHostApplicationLifetime lifetime, ILogger<JevWarmup> logger)
    : BackgroundService
{
    internal const string QuestionId = "warm_up";

    private static readonly IReadOnlyDictionary<string, object> Questions = new Dictionary<string, object>
    {
        [QuestionId] = new JevNoulQuestion("Is `text` a greeting?"),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.WarmUp || o.WarmUpTimeoutSeconds <= 0)
        {
            logger.LogDebug("Jev warm-up skipped: disabled");
            return;
        }
        if (!jev.IsConfigured)
        {
            logger.LogDebug("Jev warm-up skipped: no key");
            return;
        }
        try
        {
            await WaitForStartAsync(stoppingToken);
            var outcome = await jev.AskAsync(new WarmUpState("warm-up"), Questions, o.WarmUpTimeoutSeconds, stoppingToken);
            if (outcome.Response is { } response)
            {
                logger.LogInformation("Jev warm-up answered by {Model} in {DurationMs:F0} ms", response.Model ?? jev.Model, outcome.DurationMs);
            }
            else
            {
                logger.LogWarning("Jev warm-up failed: {Reason} after {DurationMs:F0} ms", outcome.Failure, outcome.DurationMs);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down before the warm-up finished: nothing to report.
        }
    }

    private Task WaitForStartAsync(CancellationToken ct)
    {
        if (lifetime.ApplicationStarted.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onStart = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        var onStop = ct.Register(() => started.TrySetCanceled(ct));
        return started.Task.ContinueWith(t =>
        {
            onStart.Dispose();
            onStop.Dispose();
            return t;
        }, TaskScheduler.Default).Unwrap();
    }

    private sealed record WarmUpState(string Text);
}
