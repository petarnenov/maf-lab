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

    /// <summary>Runs a job to completion. <paramref name="onProgress"/> sees every poll's state and queue position.</summary>
    public async Task<RunnerResult> RunAsync(RunnerRequest request, CancellationToken ct, Action<RunnerJob>? onProgress = null)
    {
        var job = await SendAsync(HttpMethod.Post, "runs", request, ct);
        while (true)
        {
            onProgress?.Invoke(job);
            if (job is { State: RunnerJobState.Done, Result: { } result })
            {
                return result;
            }
            await Task.Delay(_poll, ct);
            job = await SendAsync(HttpMethod.Get, $"runs/{Uri.EscapeDataString(job.Id)}", null, ct);
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
