using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Plugins.Jev;

/// <summary>
/// Retries a transient Jev failure — no response, 408, 429, or a 5xx other than 501/505 — up to
/// <see cref="JevOptions.MaxRetries"/> times, and logs every attempt with its status code (or the transport error's type)
/// and duration, and every retry with its reason and delay (jev-client-reuse). The delay waits on the request's token,
/// which the caller cancels when its budget ends, so a retry never outlives the caller's timeout. Logs carry numbers and
/// type names only: never a body, the question, a passage or the key (which exists only in the header set further in).
/// </summary>
public sealed class JevRetryHandler(IOptions<JevOptions> options, ILogger<JevRetryHandler> logger) : DelegatingHandler
{
    /// <summary>Overridable by tests, so a retry test does not sleep; the production delay is <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var maxAttempts = Math.Max(0, o.MaxRetries) + 1;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Jev attempt {Attempt}/{MaxAttempts} failed: {Error} after {DurationMs:F0} ms",
                    attempt, maxAttempts, ex.GetType().Name, sw.Elapsed.TotalMilliseconds);
                if (attempt >= maxAttempts)
                {
                    throw;
                }
                await RetryAfterAsync(ex.GetType().Name, attempt, null, cancellationToken);
                continue;
            }

            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Jev attempt {Attempt}/{MaxAttempts} → {StatusCode} in {DurationMs:F0} ms",
                    attempt, maxAttempts, status, sw.Elapsed.TotalMilliseconds);
                return response;
            }
            logger.LogWarning("Jev attempt {Attempt}/{MaxAttempts} → {StatusCode} in {DurationMs:F0} ms",
                attempt, maxAttempts, status, sw.Elapsed.TotalMilliseconds);
            if (!IsTransient(response.StatusCode) || attempt >= maxAttempts)
            {
                return response;
            }
            var retryAfter = RetryAfter(response);
            response.Dispose();
            await RetryAfterAsync(status.ToString(), attempt, retryAfter, cancellationToken);
        }
    }

    internal static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || ((int)status >= 500 && status is not HttpStatusCode.NotImplemented and not HttpStatusCode.HttpVersionNotSupported);

    private async Task RetryAfterAsync(string reason, int attempt, TimeSpan? retryAfter, CancellationToken ct)
    {
        var delay = retryAfter ?? Backoff(attempt);
        logger.LogWarning("Jev retrying after {Reason} in {DelayMs:F0} ms (attempt {NextAttempt}/{MaxAttempts})",
            reason, delay.TotalMilliseconds, attempt + 1, Math.Max(0, options.Value.MaxRetries) + 1);
        await Delay(delay, ct);
    }

    private TimeSpan Backoff(int attempt)
    {
        var baseMs = Math.Max(0, options.Value.RetryDelayMs) * Math.Pow(2, attempt - 1);
        return TimeSpan.FromMilliseconds(baseMs * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } when date > DateTimeOffset.UtcNow => date - DateTimeOffset.UtcNow,
            _ => null,
        };
}
