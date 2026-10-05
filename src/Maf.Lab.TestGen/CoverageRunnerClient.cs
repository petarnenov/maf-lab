using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Maf.Lab.TestGen;

/// <summary>The runner could not be reached or answered with something other than a job.</summary>
public sealed class RunnerUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Submits a job to the coverage runner and waits for it by polling. Used by the api (refresh, verification) and by
/// the test agent (attempts). The token comes from the caller: each signs its own service token.
/// </summary>
public sealed class CoverageRunnerClient(HttpClient http, Func<CancellationToken, Task<string>> token, TimeSpan? pollEvery = null)
{
    public const string ScopeRun = "runner.run";
    public const string Audience = "maf-lab-coverage-runner";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _poll = pollEvery ?? TimeSpan.FromSeconds(2);

    /// <summary>How long the cancel of a job may take: the work that asked for it has already stopped.</summary>
    public static readonly TimeSpan CancelWithin = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs a job to completion. <paramref name="onProgress"/> sees every poll's state and queue position. When
    /// <paramref name="ct"/> fires, the job is cancelled on the runner too (stop-anything): the caller stopping does
    /// not leave a build running for nobody.
    /// </summary>
    public async Task<RunnerResult> RunAsync(RunnerRequest request, CancellationToken ct, Action<RunnerJob>? onProgress = null)
    {
        var job = await SendAsync(HttpMethod.Post, "runs", request, ct);
        try
        {
            while (true)
            {
                onProgress?.Invoke(job);
                if (job is { State: RunnerJobState.Done, Result: { } result })
                {
                    return result;
                }
                if (job.State == RunnerJobState.Canceled)
                {
                    throw new OperationCanceledException("The runner job was cancelled.");
                }
                await Task.Delay(_poll, ct);
                job = await SendAsync(HttpMethod.Get, $"runs/{Uri.EscapeDataString(job.Id)}", null, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await CancelAsync(job.Id);
            throw;
        }
    }

    /// <summary>Cancels a job on the runner, within <see cref="CancelWithin"/>; a cancel that fails goes no further.</summary>
    private async Task CancelAsync(string jobId)
    {
        using var within = new CancellationTokenSource(CancelWithin);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"runs/{Uri.EscapeDataString(jobId)}/cancel");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(within.Token));
            using var response = await http.SendAsync(message, within.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // The runner is gone or slow: its own time limit still ends the job.
        }
    }

    private async Task<RunnerJob> SendAsync(HttpMethod method, string path, RunnerRequest? body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(ct));
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, options: Json);
        }
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new RunnerUnavailableException("The coverage runner could not be reached.", ex);
        }
        using (response)
        {
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted))
            {
                throw new RunnerUnavailableException($"The coverage runner answered {(int)response.StatusCode}.");
            }
            return await response.Content.ReadFromJsonAsync<RunnerJob>(Json, ct)
                ?? throw new RunnerUnavailableException("The coverage runner answered with no job.");
        }
    }
}
