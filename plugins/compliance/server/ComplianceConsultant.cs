using Maf.Lab.Plugins.Abstractions;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Maf.Lab.A2A;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MessageRole = A2A.Role;

namespace Maf.Lab.Plugins.Compliance;

/// <summary>
/// Consults the compliance reviewer — a different system, with its own identity, its own pace and its own right to
/// ask a question instead of answering.
///
/// It is found by its card, never by a hard-coded route, so moving it is configuration. This system authenticates
/// as itself: a user's token is not forwarded, and the reviewer never learns who asked. Every way the
/// consultation can end is a <see cref="ConsultationResult"/>, so a caller cannot forget the unhappy ones.
/// </summary>
public sealed class ComplianceConsultant(
    IHttpClientFactory http,
    IWriteAudit audit,
    IOptions<ComplianceOptions> options,
    TimeProvider time,
    ILogger<ComplianceConsultant> logger) : IReviewerConsultation
{
    /// <summary>The audited action's name; the kind it is filed under is <see cref="AuditKind"/>.</summary>
    public const string Operation = "a2a.consult";

    /// <summary>The named HTTP client the consultation goes out on.</summary>
    public const string HttpClientName = "a2a-consult";

    /// <summary>The kind of every consultation record: a request this system sent to another agent over A2A.</summary>
    public const string AuditKind = "a2a.consultation";

    /// <summary>The audited name of cancelling a review over there because the run that asked for it was stopped.</summary>
    public const string CancelOperation = "a2a.consult.cancel";

    /// <summary>How long a cancel may take: the run it belongs to has already stopped and is waiting to end.</summary>
    internal static readonly TimeSpan CancelWithin = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim discovery = new(1, 1);
    private IA2AClient? client;
    private DateTimeOffset cardFetchedAt;

    /// <summary>Starts a review and waits for it, within the deadline.</summary>
    public Task<ConsultationResult> ReviewAsync(ReviewRequest adjustment, CancellationToken ct) =>
        ConsultAsync(adjustment, taskId: null, ct);

    /// <summary>Answers a reviewer's question, continuing the review it belongs to.</summary>
    public Task<ConsultationResult> AnswerAsync(ReviewRequest adjustment, string taskId, string justification, CancellationToken ct) =>
        ConsultAsync(adjustment with { Reason = justification }, taskId, ct);

    private async Task<ConsultationResult> ConsultAsync(ReviewRequest adjustment, string? taskId, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var result = await RunAsync(adjustment, taskId, ct);
        await RecordAsync(adjustment, result, watch.Elapsed, ct);
        return result;
    }

    private async Task<ConsultationResult> RunAsync(ReviewRequest adjustment, string? taskId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.BaseUrl))
        {
            return new ConsultationResult.Unreachable("No compliance agent is configured.");
        }
        if (string.IsNullOrWhiteSpace(options.Value.ClientSecret))
        {
            // Anonymously is not an option: a sub-agent is talked to as this system or not at all.
            return new ConsultationResult.Unreachable("No credentials for the compliance agent.");
        }

        IA2AClient remote;
        try
        {
            remote = await ConnectAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Forget();
            return new ConsultationResult.Unreachable($"Discovery failed ({ex.GetType().Name}).");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.Value.Deadline);

        // Streamed, not awaited whole: a review that outlives the deadline must still leave us its task id,
        // which only the first event carries. Without it the answer could never be collected later.
        var seen = new Review(taskId);
        try
        {
            await foreach (var update in remote.SendStreamingMessageAsync(new SendMessageRequest { Message = Ask(adjustment, taskId) }, deadline.Token))
            {
                seen.Observe(update);
            }
            return Read(seen, adjustment);
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            // The run that asked was stopped. Closing the stream does not cancel an A2A task, so the review is told
            // to stop the way A2A says: tasks/cancel. A deadline is not a stop — that case below keeps the task.
            if (seen.TaskId is { Length: > 0 } stoppedTask)
            {
                await CancelAsync(remote, adjustment, stoppedTask);
            }
            if (ex is OperationCanceledException)
            {
                throw;
            }
            throw new OperationCanceledException("The run that asked for the review was stopped.", ex, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The review is still running over there; its id is how the answer is collected later.
            return new ConsultationResult.TimedOut(seen.TaskId);
        }
        catch (A2AException ex)
        {
            return new ConsultationResult.Failed(seen.TaskId, $"{ex.ErrorCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Forget();
            return new ConsultationResult.Unreachable(ex.GetType().Name);
        }
    }

    /// <summary>
    /// Cancels the review over there, within <see cref="CancelWithin"/> of its own — the run's token is already
    /// cancelled. A cancel that fails is recorded and goes no further: the run is ending either way.
    /// </summary>
    private async Task CancelAsync(IA2AClient remote, ReviewRequest adjustment, string taskId)
    {
        var watch = Stopwatch.StartNew();
        var outcome = "cancelled";
        using var within = new CancellationTokenSource(CancelWithin, time);
        try
        {
            await remote.CancelTaskAsync(new CancelTaskRequest { Id = taskId }, within.Token);
        }
        catch (Exception ex)
        {
            outcome = "cancel_failed";
            logger.LogWarning("compliance review cancel failed ({ErrorType}) task={TaskId}", ex.GetType().Name, taskId);
        }
        await RecordAsync(CancelOperation, adjustment, taskId, outcome, watch.Elapsed, CancellationToken.None);
    }

    /// <summary>
    /// The card, then the agent it describes — fetched once and reused, because paying for discovery on every
    /// review would be a tax on every review. It is dropped when a call fails at the transport level, so a
    /// reviewer that moved is found again rather than remembered wrongly.
    /// </summary>
    private async Task<IA2AClient> ConnectAsync(CancellationToken ct)
    {
        if (client is not null && time.GetUtcNow() - cardFetchedAt < options.Value.CardCacheFor)
        {
            return client;
        }

        await discovery.WaitAsync(ct);
        try
        {
            if (client is not null && time.GetUtcNow() - cardFetchedAt < options.Value.CardCacheFor)
            {
                return client;
            }

            var baseUrl = options.Value.BaseUrl.TrimEnd('/');
            var token = await TokenAsync(baseUrl, ct);
            var authenticated = http.CreateClient(HttpClientName);
            authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // The resolver appends the well-known path to the *origin*, so an agent served under a prefix — as it
            // is when two agents share one entry point — must be asked for its card by full path, or the other
            // agent's card comes back.
            var address = new Uri(baseUrl);
            var cardPath = $"{address.AbsolutePath.TrimEnd('/')}{AgentCardFactory.WellKnownPath}";
            var resolver = new A2ACardResolver(new Uri(address.GetLeftPart(UriPartial.Authority)), authenticated, cardPath);
            var card = await resolver.GetAgentCardAsync(ct);

            // The card says *where* it answers; the configured address says *how this system gets there*. A card
            // is public, so it advertises the public entry point — which, from inside the network the assistant
            // runs in, is not a route at all. So the path comes from the card and the origin from configuration,
            // as it does for every client behind a reverse proxy. Assembling the path here instead would be
            // hard-coding a route, which is the thing the card exists to avoid.
            var advertised = card.SupportedInterfaces?
                .FirstOrDefault(i => string.Equals(i.ProtocolBinding, "JSONRPC", StringComparison.OrdinalIgnoreCase))
                ?? card.SupportedInterfaces?.FirstOrDefault();
            var endpoint = advertised?.Url is { Length: > 0 } advertisedUrl && Uri.TryCreate(advertisedUrl, UriKind.Absolute, out var parsed)
                ? new Uri(new Uri(address.GetLeftPart(UriPartial.Authority)), parsed.AbsolutePath)
                : new Uri($"{baseUrl}/a2a");

            client = new A2AClient(endpoint, authenticated);
            cardFetchedAt = time.GetUtcNow();
            logger.LogInformation("compliance agent discovered name={Name} endpoint={Endpoint}", card.Name, endpoint);
            return client;
        }
        finally
        {
            discovery.Release();
        }
    }

    private void Forget() => client = null;

    private async Task<string> TokenAsync(string baseUrl, CancellationToken ct)
    {
        var anonymous = http.CreateClient(HttpClientName);
        var response = await anonymous.PostAsJsonAsync($"{baseUrl}/a2a/token",
            new A2AEndpoints.TokenRequest(options.Value.ClientId, options.Value.ClientSecret), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(ct))!.AccessToken;
    }

    private static Message Ask(ReviewRequest adjustment, string? taskId) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = MessageRole.User,
        TaskId = taskId,
        Parts =
        [
            new Part
            {
                Data = JsonSerializer.SerializeToElement(new
                {
                    adjustmentId = adjustment.AdjustmentId,
                    firmId = adjustment.FirmId,
                    accountId = adjustment.AccountId,
                    amount = adjustment.Amount,
                    reason = adjustment.Reason,
                }),
            },
        ],
    };

    /// <summary>
    /// What the stream has told us so far. Kept as it arrives, because a deadline can fall at any point and
    /// what we know by then is all we will have.
    /// </summary>
    private sealed class Review(string? taskId)
    {
        public string TaskId { get; private set; } = taskId ?? "";

        public TaskState? State { get; private set; }

        public Message? Question { get; private set; }

        public List<Artifact> Artifacts { get; } = [];

        public Message? DirectReply { get; private set; }

        public void Observe(StreamResponse update)
        {
            if (update.Task is { } task)
            {
                TaskId = task.Id is { Length: > 0 } id ? id : TaskId;
                State = task.Status?.State ?? State;
                Question = task.Status?.Message ?? Question;
                if (task.Artifacts is { Count: > 0 } artifacts)
                {
                    Artifacts.AddRange(artifacts);
                }
            }
            if (update.StatusUpdate is { } status)
            {
                TaskId = status.TaskId is { Length: > 0 } statusId ? statusId : TaskId;
                State = status.Status?.State ?? State;
                Question = status.Status?.Message ?? Question;
            }
            if (update.ArtifactUpdate is { } artifact)
            {
                TaskId = artifact.TaskId is { Length: > 0 } artifactId ? artifactId : TaskId;
                if (artifact.Artifact is { } a)
                {
                    Artifacts.Add(a);
                }
            }
            if (update.Message is { } message)
            {
                DirectReply = message;
            }
        }
    }

    private static ConsultationResult Read(Review review, ReviewRequest adjustment)
    {
        if (review.State is null)
        {
            // A message rather than a task: the reviewer declined to review this at all.
            var text = string.Join(' ', review.DirectReply?.Parts?.Select(p => p.Text).Where(t => t is not null) ?? []);
            return new ConsultationResult.Failed(review.TaskId, text is { Length: > 0 } ? text : "The reviewer returned no task.");
        }

        var id = review.TaskId;
        switch (review.State)
        {
            case TaskState.InputRequired or TaskState.AuthRequired:
                var question = string.Join(' ', review.Question?.Parts?.Select(p => p.Text).Where(t => t is not null) ?? []);
                return new ConsultationResult.QuestionAsked(id, question);

            case TaskState.Completed:
                var verdict = review.Artifacts
                    .SelectMany(a => a.Parts ?? [])
                    .Select(p => p.Data)
                    .FirstOrDefault(d => d is not null && d.Value.TryGetProperty("decision", out _));
                if (verdict is null)
                {
                    return new ConsultationResult.Failed(id, "The review completed without a verdict.");
                }
                return Judge(verdict.Value, id, adjustment);

            default:
                return new ConsultationResult.Failed(id, $"The review ended {review.State}.");
        }
    }

    /// <summary>
    /// A verdict is another system's word about our question, so it is checked before it is believed: it must
    /// say what it decided, and it must be about the adjustment and the account we asked about. The
    /// identifiers we carry on are the ones we sent — never the ones that came back.
    /// </summary>
    internal static ConsultationResult Judge(JsonElement verdict, string taskId, ReviewRequest adjustment)
    {
        if (!verdict.TryGetProperty("decision", out var decisionValue) || decisionValue.GetString() is not { Length: > 0 } decision)
        {
            return new ConsultationResult.Failed(taskId, "The review returned no decision.");
        }
        if (!Echoes(verdict, "adjustmentId", adjustment.AdjustmentId))
        {
            return new ConsultationResult.Failed(taskId, "The review answered about a different adjustment.");
        }
        if (!Echoes(verdict, "accountId", adjustment.AccountId))
        {
            return new ConsultationResult.Failed(taskId, "The review answered about a different account.");
        }

        return new ConsultationResult.Verdict(
            taskId,
            adjustment.AdjustmentId,
            Approved: string.Equals(decision, "approved", StringComparison.OrdinalIgnoreCase),
            Reason: verdict.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "" : "");
    }

    private static bool Echoes(JsonElement verdict, string property, string expected) =>
        verdict.TryGetProperty(property, out var value)
        && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    /// <summary>
    /// The same record every other action leaves: who, what, which task, the outcome, how long — no content. It is
    /// filed under the firm whose adjustment was reviewed, so that firm's compliance export contains it.
    /// </summary>
    private Task RecordAsync(ReviewRequest adjustment, ConsultationResult result, TimeSpan took, CancellationToken ct) =>
        RecordAsync(Operation, adjustment, result.TaskIdOrNull, result.Outcome, took, ct);

    private async Task RecordAsync(
        string operation, ReviewRequest adjustment, string? taskId, string outcome, TimeSpan took, CancellationToken ct)
    {
        try
        {
            // Filed under the request's principal (its tenant is the adjustment's firm), as a step of the write it is for.
            await audit.RecordAsync(AuditKind, operation,
                $"agent=compliance adjustmentId={adjustment.AdjustmentId} taskId={taskId ?? "-"}",
                outcome, (long)took.TotalMilliseconds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("consultation audit write failed ({Error})", ex.GetType().Name);
        }
    }
}
