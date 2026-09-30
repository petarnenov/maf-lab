using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using A2A;
using Maf.Lab.A2A;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.TestGen;
using Microsoft.Extensions.Options;
using MessageRole = A2A.Role;
using UserRole = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.Api.Coverage;

/// <summary>The agent could not be reached (no configuration, no card, no token, or the transport failed).</summary>
public sealed class AgentUnavailableException(string reason, Exception? inner = null) : Exception(reason, inner);

/// <summary>What the api learns about a task from one update, whatever form the update came in.</summary>
public sealed record TaskObservation(string TaskId, TaskState? State, TestGenProgress? Progress, TestGenReport? Report, string? StatusText);

/// <summary>
/// The api's side of the test agent's A2A contract. Like the compliance consultant: found by its card, this system
/// authenticates as itself, and every operation is audited without content. It speaks to the agent with the SDK's
/// client directly — the request is a data part, and the run is followed, polled and cancelled by task id, none of
/// which the Agent Framework's A2A agent exposes (DECISIONS §23).
/// </summary>
public sealed class TestAgentClient(
    IHttpClientFactory http,
    ToolAudit audit,
    IOptions<TestAgentOptions> options,
    TimeProvider time,
    ILogger<TestAgentClient> logger)
{
    public const string HttpClientName = "a2a-testgen";

    private readonly SemaphoreSlim _discovery = new(1, 1);
    private IA2AClient? _client;
    private DateTimeOffset _cardFetchedAt;

    /// <summary>
    /// Sends the request and returns as soon as the agent has accepted it as a task. The run goes on over there; the
    /// follower picks it up by its id. Invalid input comes back as a rejected task, reported as such.
    /// </summary>
    public async Task<TaskObservation> StartAsync(TestGenRequest request, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var outcome = "unreachable";
        string? taskId = null;
        try
        {
            var remote = await ConnectAsync(ct);
            using var accepted = CancellationTokenSource.CreateLinkedTokenSource(ct);
            accepted.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await foreach (var update in remote.SendStreamingMessageAsync(new SendMessageRequest { Message = Ask(request) }, accepted.Token))
                {
                    var seen = Observe(update);
                    if (seen is { TaskId.Length: > 0 })
                    {
                        taskId = seen.TaskId;
                        outcome = seen.State == TaskState.Rejected ? "rejected" : "started";
                        // The first event with a task id is all a start needs; the stream may close now.
                        return seen;
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new AgentUnavailableException("The test agent did not accept the task in time.");
            }
            throw new AgentUnavailableException("The test agent returned no task.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AgentUnavailableException)
        {
            Forget();
            throw new AgentUnavailableException($"The test agent could not be reached ({ex.GetType().Name}).", ex);
        }
        finally
        {
            await RecordAsync("a2a.testgen.start", request.RunId, taskId, outcome, watch.Elapsed, ct);
        }
    }

    /// <summary>The task's updates as they happen: the current state first, then every change, until the stream ends.</summary>
    public async IAsyncEnumerable<TaskObservation> SubscribeAsync(string taskId, [EnumeratorCancellation] CancellationToken ct)
    {
        var remote = await ConnectAsync(ct);
        await foreach (var update in remote.SubscribeToTaskAsync(new SubscribeToTaskRequest { Id = taskId }, ct))
        {
            if (Observe(update) is { } seen)
            {
                yield return seen with { TaskId = taskId };
            }
        }
    }

    /// <summary>The task as it stands: the fallback when a stream cannot be had.</summary>
    public async Task<TaskObservation> GetAsync(string taskId, CancellationToken ct)
    {
        var remote = await ConnectAsync(ct);
        var task = await remote.GetTaskAsync(new GetTaskRequest { Id = taskId }, ct);
        return Observe(task) with { TaskId = taskId };
    }

    public async Task CancelAsync(string runId, string taskId, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var outcome = "canceled";
        try
        {
            var remote = await ConnectAsync(ct);
            await remote.CancelTaskAsync(new CancelTaskRequest { Id = taskId }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome = "failed";
            logger.LogWarning("test agent cancel failed ({ErrorType})", ex.GetType().Name);
            throw;
        }
        finally
        {
            await RecordAsync("a2a.testgen.cancel", runId, taskId, outcome, watch.Elapsed, ct);
        }
    }

    /// <summary>Audits a follow that ended, with how it ended.</summary>
    public Task RecordFollowAsync(string runId, string taskId, string outcome, TimeSpan took, CancellationToken ct) =>
        RecordAsync("a2a.testgen.follow", runId, taskId, outcome, took, ct);

    internal static TaskObservation? Observe(StreamResponse update) =>
        update.Task is { } task ? Observe(task)
        : update.StatusUpdate is { } status ? new TaskObservation(status.TaskId ?? "", status.Status?.State,
            Progress(status.Status?.Message), null, Text(status.Status?.Message))
        : update.ArtifactUpdate is { } artifact ? new TaskObservation(artifact.TaskId ?? "", null, null, Report(artifact.Artifact is { } a ? [a] : []), null)
        : null;

    internal static TaskObservation Observe(AgentTask task) =>
        new(task.Id ?? "", task.Status?.State, Progress(task.Status?.Message), Report(task.Artifacts ?? []), Text(task.Status?.Message));

    private static TestGenProgress? Progress(Message? message) =>
        DataOfKind(message?.Parts ?? [], TestGenKinds.Progress)?.Deserialize<TestGenProgress>(TestGenKinds.Json);

    private static TestGenReport? Report(IEnumerable<Artifact> artifacts) =>
        DataOfKind(artifacts.SelectMany(a => a.Parts ?? []), TestGenKinds.Report)?.Deserialize<TestGenReport>(TestGenKinds.Json);

    private static JsonElement? DataOfKind(IEnumerable<Part> parts, string kind) =>
        parts.Select(p => p.Data).FirstOrDefault(d => d is { ValueKind: JsonValueKind.Object } data
            && data.TryGetProperty("kind", out var k) && k.GetString() == kind);

    private static string? Text(Message? message) =>
        message?.Parts?.Select(p => p.Text).FirstOrDefault(t => t is { Length: > 0 });

    private static Message Ask(TestGenRequest request) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = MessageRole.User,
        Parts = [new Part { Data = JsonSerializer.SerializeToElement(request, TestGenKinds.Json) }],
    };

    /// <summary>The card, then the agent it describes, fetched once and reused; dropped when a call fails.</summary>
    private async Task<IA2AClient> ConnectAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.BaseUrl))
        {
            throw new AgentUnavailableException("No test agent is configured.");
        }
        if (string.IsNullOrWhiteSpace(opts.ClientSecret))
        {
            throw new AgentUnavailableException("No credentials for the test agent.");
        }
        if (_client is not null && time.GetUtcNow() - _cardFetchedAt < opts.CardCacheFor)
        {
            return _client;
        }
        await _discovery.WaitAsync(ct);
        try
        {
            if (_client is not null && time.GetUtcNow() - _cardFetchedAt < opts.CardCacheFor)
            {
                return _client;
            }
            var baseUrl = opts.BaseUrl.TrimEnd('/');
            var tokenResponse = await http.CreateClient(HttpClientName).PostAsJsonAsync($"{baseUrl}/a2a/token",
                new A2AEndpoints.TokenRequest(opts.ClientId, opts.ClientSecret), ct);
            tokenResponse.EnsureSuccessStatusCode();
            var token = (await tokenResponse.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(ct))!.AccessToken;

            var authenticated = http.CreateClient(HttpClientName);
            authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Timeouts are per operation here: a subscription may rightly stay open for as long as a run takes.
            authenticated.Timeout = Timeout.InfiniteTimeSpan;
            var address = new Uri(baseUrl);
            var origin = new Uri(address.GetLeftPart(UriPartial.Authority));
            var card = await new A2ACardResolver(origin, authenticated, $"{address.AbsolutePath.TrimEnd('/')}{AgentCardFactory.WellKnownPath}")
                .GetAgentCardAsync(ct);
            // The path from the card, the origin from configuration (the card advertises where it is published).
            var advertised = card.SupportedInterfaces?
                .FirstOrDefault(i => string.Equals(i.ProtocolBinding, "JSONRPC", StringComparison.OrdinalIgnoreCase))
                ?? card.SupportedInterfaces?.FirstOrDefault();
            var endpoint = advertised?.Url is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                ? new Uri(origin, parsed.AbsolutePath)
                : new Uri($"{baseUrl}/a2a");
            _client = new A2AClient(endpoint, authenticated);
            _cardFetchedAt = time.GetUtcNow();
            logger.LogInformation("test agent discovered name={Name} endpoint={Endpoint}", card.Name, endpoint);
            return _client;
        }
        finally
        {
            _discovery.Release();
        }
    }

    public void Forget() => _client = null;

    private async Task RecordAsync(string operation, string runId, string? taskId, string outcome, TimeSpan took, CancellationToken ct)
    {
        try
        {
            // Coverage runs belong to the repository, not a firm: filed under the shared scope, as a system action.
            await audit.RecordAsync(new AuditEntry(
                new Principal("maf-lab-assistant", TenantId.Shared, UserRole.READ_ONLY, []),
                null, null, operation, $"agent=testgen runId={runId} taskId={taskId ?? "-"}",
                outcome, (long)took.TotalMilliseconds, Compliance.AuditKinds.A2AConsultation), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("test agent audit write failed ({ErrorType})", ex.GetType().Name);
        }
    }
}
