using System.Text.Json;
using AGUI.Abstractions;
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
/// Written from the one loop every event of a run passes through, so there is a single writer per run and no
/// contention. Text arrives in many small pieces, so a save is made when something happens that a person would
/// notice, and at most every <see cref="QuietMs"/> while only text is arriving.
/// </summary>
public sealed class RunStateTracker(IRunStateStore store, Principal principal, string conversationId, string runId, TimeProvider time)
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
    private string? _turnId;
    private string? _error;

    /// <summary>Reads one event of the run. Returns true when the snapshot is worth writing now.</summary>
    public bool Observe(BaseEvent e)
    {
        switch (e)
        {
            case TextMessageContentEvent text:
                _answer.Append(text.Delta);
                return Quiet();

            case ToolCallStartEvent start:
                Put(start.ToolCallId, c => c with { ToolName = start.ToolCallName });
                return true;

            case ToolCallArgsEvent args:
                Put(args.ToolCallId, c => c with { ArgumentSummary = args.Delta ?? "" });
                return true;

            case ToolCallResultEvent result:
                Put(result.ToolCallId, c => c with
                {
                    ResultSummary = Summary(result),
                    Finished = true,
                    IsError = IsError(result),
                });
                return true;

            case RunErrorEvent error:
                _outcome = RunOutcomes.Failed;
                // The same short, user-facing text the client was given; never anything internal.
                _error = error.Message;
                return true;

            case RunFinishedEvent finished:
                Finish(finished);
                return true;

            default:
                return false;
        }
    }

    public RunState Snapshot() => new(
        runId, conversationId, principal.UserId, principal.FirmId.Value,
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

    private void Finish(RunFinishedEvent finished)
    {
        var outcome = finished.Outcome as object;
        _outcome = outcome switch
        {
            RunFinishedCancelledOutcome => RunOutcomes.Cancelled,
            RunFinishedInterruptOutcome interrupt => Awaiting(interrupt),
            _ => RunOutcomes.Answered,
        };
        if (finished.Result is JsonElement { ValueKind: JsonValueKind.Object } result
            && result.TryGetProperty("turnId", out var turn))
        {
            _turnId = turn.GetString();
        }
    }

    private string Awaiting(RunFinishedInterruptOutcome interrupt)
    {
        _awaitingId = interrupt.Interrupts?.FirstOrDefault()?.Id;
        return RunOutcomes.AwaitingPerson;
    }

    private void Put(string callId, Func<RunToolCall, RunToolCall> update)
    {
        if (!_toolCalls.TryGetValue(callId, out var call))
        {
            call = new RunToolCall(callId, "", "", null, false, false);
            _order.Add(callId);
        }
        _toolCalls[callId] = update(call);
    }

    /// <summary>
    /// The structured summary the client was given. By the time a run's events reach here they have been through
    /// redaction, so this is identifiers and counts and never a document's text.
    /// </summary>
    private static string Summary(ToolCallResultEvent result)
    {
        var content = Text(result);
        return Field(content, "summary") ?? content;
    }

    private static bool IsError(ToolCallResultEvent result) => Field(Text(result), "isError") == "true";

    private static string Text(ToolCallResultEvent result) => result.Content.ToString() ?? "";

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
