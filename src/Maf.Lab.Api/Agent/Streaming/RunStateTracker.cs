using System.Text.Json;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Domain.Tenancy;

namespace Maf.Lab.Api.Agent.Streaming;

/// <summary>
/// Keeps a run's snapshot in the shared store as the run streams, so a client that closed its tab can be told
/// where the turn stands — by whichever replica it comes back to.
///
/// It is a snapshot and not a recording: the answer so far, the tool calls and how each ended, whether the run is
/// waiting for a person, and how it finished. Frame-by-frame replay is a different thing and is not this.
///
/// Fed what the official server wrote, by the one reader of a run's events (AGUI.RunTap), so there is a single writer per run and no
/// contention. Text arrives in many small pieces, so a save is made when something happens that a person would
/// notice, and at most every <see cref="QuietMs"/> while only text is arriving.
/// </summary>
/// <param name="turnId">The turn the run records; null for a run that records none.</param>
public sealed class RunStateTracker(IRunStateStore store, Principal principal, string conversationId, string runId, string? turnId, TimeProvider time)
{
    /// <summary>How often a run that is only producing text writes its snapshot.</summary>
    public const int QuietMs = 500;

    private readonly Dictionary<string, RunToolCall> _toolCalls = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly System.Text.StringBuilder _answer = new();
    private readonly DateTimeOffset _startedAt = time.GetUtcNow();
    private long _lastSaveMs;
    private string _outcome = RunOutcomes.Running;
    private string? _awaitingId;
    private readonly string? _turnId = turnId;
    private string? _error;

    /// <summary>Reads one event of the run, as written. Returns true when the snapshot is worth writing now.</summary>
    public bool Observe(JsonElement e)
    {
        switch (Str(e, "type"))
        {
            case "TEXT_MESSAGE_CONTENT":
                _answer.Append(Str(e, "delta"));
                return Quiet();

            case "TOOL_CALL_START":
                var name = Str(e, "toolCallName") ?? "";
                Put(Str(e, "toolCallId"), c => c with { ToolName = name });
                return true;

            case "TOOL_CALL_ARGS":
                // The arguments travel as identifier-only JSON; the snapshot keeps them as the audit writes them.
                var summary = ArgumentSummary.FromJson(Str(e, "delta"));
                Put(Str(e, "toolCallId"), c => c with { ArgumentSummary = summary });
                return true;

            case "TOOL_CALL_RESULT":
                var content = Str(e, "content") ?? "";
                Put(Str(e, "toolCallId"), c => c with
                {
                    ResultSummary = Field(content, "summary") ?? content,
                    Finished = true,
                    IsError = Field(content, "isError") == "true",
                });
                return true;

            case "RUN_ERROR":
                _outcome = RunOutcomes.Failed;
                // The same short, user-facing text the client was given; never anything internal.
                _error = Str(e, "message");
                return true;

            case "RUN_FINISHED":
                if (e.TryGetProperty("outcome", out var outcome) && Str(outcome, "type") == "interrupt")
                {
                    _outcome = RunOutcomes.AwaitingPerson;
                    _awaitingId = outcome.TryGetProperty("interrupts", out var interrupts) && interrupts.GetArrayLength() > 0
                        ? Str(interrupts[0], "id")
                        : null;
                }
                else
                {
                    _outcome = RunOutcomes.Answered;
                }
                return true;

            default:
                return false;
        }
    }

    /// <summary>The run was stopped before it ended: the caller asked, or walked away.</summary>
    public void Cancelled()
    {
        if (_outcome == RunOutcomes.Running)
        {
            _outcome = RunOutcomes.Cancelled;
        }
    }

    public RunState Snapshot() => new(
        runId, conversationId, principal.UserId, principal.TenantId.Value,
        _answer.ToString(),
        [.. _order.Select(id => _toolCalls[id])],
        _outcome, _awaitingId, _turnId, _error,
        _startedAt, time.GetUtcNow());

    public Task SaveAsync(CancellationToken ct)
    {
        _lastSaveMs = time.GetUtcNow().ToUnixTimeMilliseconds();
        return store.SaveAsync(Snapshot(), ct);
    }

    private bool Quiet()
    {
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        return now - _lastSaveMs >= QuietMs;
    }

    private void Put(string? callId, Func<RunToolCall, RunToolCall> update)
    {
        if (callId is null)
        {
            return;
        }
        if (!_toolCalls.TryGetValue(callId, out var call))
        {
            call = new RunToolCall(callId, "", "", null, false, false);
            _order.Add(callId);
        }
        _toolCalls[callId] = update(call);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Field(string content, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty(name, out var value)
                ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
