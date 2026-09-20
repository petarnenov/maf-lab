using Maf.Lab.Api.Topology;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Agent.Streaming;

public sealed class RunStopOptions
{
    /// <summary>The compose service this API is a replica of; empty disables asking the others.</summary>
    public string Service { get; set; } = "api";

    /// <summary>How long to wait for a sibling to answer. A stop is supposed to be quick or not at all.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Stops a run wherever it is running.
///
/// A run belongs to the replica that took its request, and the balancer sends the next request round-robin —
/// so with two replicas a stop lands on the wrong one every single time. Session affinity would fix it and the
/// rest of this API deliberately does without. Instead the replica that was asked, and does not have the run,
/// asks the others directly: the service name resolves to one address per replica, which is the same fact the
/// balancer is built on. The caller's own token goes with the question, so a sibling authorises it exactly as
/// this one would.
/// </summary>
public sealed class RunStopper(
    RunRegistry runs,
    IServiceResolver services,
    IHttpClientFactory http,
    IOptions<RunStopOptions> options,
    ILogger<RunStopper> logger)
{
    public const string LocalOnlyQuery = "localOnly";

    /// <summary>True when the run was found and asked to stop, here or on a sibling.</summary>
    public async Task<bool> StopAsync(string runId, string bearerToken, bool localOnly, CancellationToken ct)
    {
        if (runs.Stop(runId))
        {
            return true;
        }
        if (localOnly || string.IsNullOrWhiteSpace(options.Value.Service))
        {
            return false;
        }

        var addresses = await services.ResolveAsync(options.Value.Service, ct);
        if (addresses.Count == 0)
        {
            return false;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.Value.Timeout);

        foreach (var address in addresses)
        {
            if (await AskAsync(address, runId, bearerToken, deadline.Token))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<bool> AskAsync(string address, string runId, string bearerToken, CancellationToken ct)
    {
        try
        {
            var client = http.CreateClient("run-stop");
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"http://{address}:8080/api/chat/{Uri.EscapeDataString(runId)}/stop?{LocalOnlyQuery}=true");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearerToken}");
            using var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("asking {Address} to stop a run failed ({Error})", address, ex.GetType().Name);
            return false;
        }
    }
}
