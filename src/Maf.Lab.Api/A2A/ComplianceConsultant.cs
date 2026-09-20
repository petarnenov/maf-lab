using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using A2A;
using Maf.Lab.A2A;
using Maf.Lab.Api.Agent;
using Maf.Lab.Domain.Tenancy;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using MessageRole = A2A.Role;
using UserRole = Maf.Lab.Domain.Tenancy.Role;

namespace Maf.Lab.Api.A2A;

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
    ToolAudit audit,
    IOptions<ComplianceOptions> options,
    TimeProvider time,
    ILogger<ComplianceConsultant> logger)
{
    /// <summary>The audited action's name; the kind it is filed under is the sub-agent kind.</summary>
    public const string Operation = "a2a.consult";

    private readonly SemaphoreSlim discovery = new(1, 1);
    private IA2AClient? client;
    private DateTimeOffset cardFetchedAt;

    /// <summary>Starts a review and waits for it, within the deadline.</summary>
    public Task<ConsultationResult> ReviewAsync(FeeAdjustment adjustment, CancellationToken ct) =>
        ConsultAsync(adjustment, taskId: null, ct);

    /// <summary>Answers a reviewer's question, continuing the review it belongs to.</summary>
    public Task<ConsultationResult> AnswerAsync(FeeAdjustment adjustment, string taskId, string justification, CancellationToken ct) =>
        ConsultAsync(adjustment with { Reason = justification }, taskId, ct);

    private async Task<ConsultationResult> ConsultAsync(FeeAdjustment adjustment, string? taskId, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var result = await RunAsync(adjustment, taskId, ct);
        await RecordAsync(adjustment, result, watch.Elapsed, ct);
        return result;
    }

    private async Task<ConsultationResult> RunAsync(FeeAdjustment adjustment, string? taskId, CancellationToken ct)
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
            var authenticated = http.CreateClient("a2a-consult");
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
        var anonymous = http.CreateClient("a2a-consult");
        var response = await anonymous.PostAsJsonAsync($"{baseUrl}/a2a/token",
            new A2AEndpoints.TokenRequest(options.Value.ClientId, options.Value.ClientSecret), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<A2AEndpoints.TokenResponse>(ct))!.AccessToken;
    }

    private static Message Ask(FeeAdjustment adjustment, string? taskId) => new()
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

    private static ConsultationResult Read(Review review, FeeAdjustment adjustment)
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
    internal static ConsultationResult Judge(JsonElement verdict, string taskId, FeeAdjustment adjustment)
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
    private async Task RecordAsync(FeeAdjustment adjustment, ConsultationResult result, TimeSpan took, CancellationToken ct)
    {
        try
        {
            var firm = TenantId.TryParse(adjustment.FirmId, out var parsed) ? parsed : TenantId.Shared;
            await audit.RecordAsync(new AuditEntry(
                new Principal("maf-lab-assistant", firm, UserRole.READ_ONLY, []),
                null, null, Operation,
                $"agent=compliance adjustmentId={adjustment.AdjustmentId} taskId={result.TaskIdOrNull ?? "-"}",
                result.Outcome, (long)took.TotalMilliseconds, Compliance.AuditKinds.A2AConsultation), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("consultation audit write failed ({Error})", ex.GetType().Name);
        }
    }
}
