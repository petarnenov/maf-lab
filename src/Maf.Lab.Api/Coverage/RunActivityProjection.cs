using System.Text.Json;
using AGUI.Abstractions;
using Maf.Lab.Api.Agent.Streaming;
using Maf.Lab.Api.Storage;
using Maf.Lab.TestGen;

namespace Maf.Lab.Api.Coverage;

/// <summary>
/// A test-generation run as the browser sees it: AG-UI and nothing else (add-run-activity-view). The run's summary is
/// the protocol's state, each phase a step, each tool call the protocol's tool events, the model's text and reasoning
/// its messages, and an attempt's result a custom event. Built only from what the database holds, so every replica
/// tells the same run the same way. One projection per stream: it remembers what it has already said.
/// </summary>
public sealed class RunActivityProjection(string runId)
{
    public const string AttemptEvent = "maf-lab/testgen-attempt";
    public const string DroppedEvent = "maf-lab/testgen-activity-dropped";
    public const string StoppedEvent = "maf-lab/testgen-stopped";
    public const string ResumedEvent = "maf-lab/testgen-resumed";

    private readonly Dictionary<long, int> _sent = [];
    private string? _summary;
    private string? _step;
    private (long Seq, string Type)? _open;
    private bool _droppedSaid;

    public string ThreadId => $"testgen:{runId}";

    /// <summary>The highest entry sequence this stream has shown: what changed since is what comes next.</summary>
    public long Cursor { get; private set; }

    public BaseEvent Started() => new RunStartedEvent { ThreadId = ThreadId, RunId = runId };

    /// <summary>The run's summary as state, when it changed since the last one sent.</summary>
    public IEnumerable<BaseEvent> Summary(RunSummary summary)
    {
        var json = JsonSerializer.Serialize(summary, AGUIStream.Json);
        if (json == _summary)
        {
            yield break;
        }
        _summary = json;
        yield return new StateSnapshotEvent { Snapshot = JsonSerializer.Deserialize<JsonElement>(json) };
    }

    /// <summary>The events for entries new or grown since the cursor, in the agent's order.</summary>
    public IEnumerable<BaseEvent> Entries(IReadOnlyList<TestGenRunActivityRow> rows, bool dropped)
    {
        if (dropped && !_droppedSaid)
        {
            _droppedSaid = true;
            yield return new CustomEvent
            {
                Name = DroppedEvent,
                Value = JsonSerializer.SerializeToElement(new { reason = "The oldest activity of this run was dropped to keep it within its cap." }, AGUIStream.Json),
            };
        }
        foreach (var row in rows.OrderBy(r => r.Seq))
        {
            Cursor = Math.Max(Cursor, row.LastSeq);
            if (row.Type is ActivityType.Text or ActivityType.Reasoning)
            {
                foreach (var e in Message(row))
                {
                    yield return e;
                }
                continue;
            }
            foreach (var e in CloseMessage())
            {
                yield return e;
            }
            switch (row.Type)
            {
                case ActivityType.Phase:
                    if (_step is { } previous)
                    {
                        yield return new StepFinishedEvent { StepName = previous };
                    }
                    _step = row.Attempt == 0 ? $"baseline: {row.Phase}" : $"attempt {row.Attempt}: {row.Phase}";
                    yield return new StepStartedEvent { StepName = _step };
                    break;
                case ActivityType.Tool when Data<ToolActivity>(row) is { } tool:
                    var callId = $"tool-{row.Seq}";
                    yield return new ToolCallStartEvent { ToolCallId = callId, ToolCallName = tool.Name };
                    yield return new ToolCallArgsEvent
                    {
                        ToolCallId = callId,
                        Delta = JsonSerializer.Serialize(new { path = tool.Path }, AGUIStream.Json),
                    };
                    yield return new ToolCallEndEvent { ToolCallId = callId };
                    yield return new ToolCallResultEvent
                    {
                        MessageId = $"tool-result-{row.Seq}",
                        ToolCallId = callId,
                        Role = AGUIRoles.Tool,
                        Content = JsonSerializer.Serialize(new { outcome = tool.Outcome, summary = tool.Summary }, AGUIStream.Json),
                    };
                    break;
                case ActivityType.Attempt when Data<AttemptActivity>(row) is { } result:
                    yield return new CustomEvent
                    {
                        Name = AttemptEvent,
                        Value = JsonSerializer.SerializeToElement(new
                        {
                            attempt = row.Attempt,
                            before = result.Before,
                            after = result.After,
                            build = result.Build,
                            tests = result.Tests,
                            errors = result.Errors,
                            violations = result.Violations,
                        }, AGUIStream.Json),
                    };
                    break;
                case ActivityType.Resumed:
                    // The agent restarted: the step it was in is over, and the attempt it resumes at starts again.
                    if (_step is { } interrupted)
                    {
                        _step = null;
                        yield return new StepFinishedEvent { StepName = interrupted };
                    }
                    yield return new CustomEvent
                    {
                        Name = ResumedEvent,
                        Value = JsonSerializer.SerializeToElement(new { attempt = row.Attempt }, AGUIStream.Json),
                    };
                    break;
                case ActivityType.Stopped when Data<StoppedActivity>(row) is { } stop:
                    // The agent is done: its last step is over, and the timeline closes on why.
                    if (_step is { } open)
                    {
                        _step = null;
                        yield return new StepFinishedEvent { StepName = open };
                    }
                    yield return new CustomEvent
                    {
                        Name = StoppedEvent,
                        Value = JsonSerializer.SerializeToElement(new
                        {
                            reason = stop.Reason,
                            lastAttempt = stop.LastAttempt,
                            bestPct = stop.BestPct,
                            notStarted = stop.NotStarted,
                        }, AGUIStream.Json),
                    };
                    break;
            }
        }
    }

    /// <summary>The one terminal event, after closing whatever is open.</summary>
    public IEnumerable<BaseEvent> Ended(RunSummary summary)
    {
        foreach (var e in CloseMessage())
        {
            yield return e;
        }
        if (_step is { } step)
        {
            _step = null;
            yield return new StepFinishedEvent { StepName = step };
        }
        if (summary.State is TestGenRunState.Failed or TestGenRunState.Canceled)
        {
            var code = summary.Reason ?? summary.State;
            yield return new RunErrorEvent { Message = $"The run {summary.State} ({code}).", Code = code };
            yield break;
        }
        yield return new RunFinishedEvent
        {
            ThreadId = ThreadId,
            RunId = runId,
            Outcome = new RunFinishedSuccessOutcome(),
            Result = JsonSerializer.SerializeToElement(summary, AGUIStream.Json),
        };
    }

    /// <summary>Whether the agent's work on a run in this state is over, so the stream ends.</summary>
    public static bool IsOver(string state) => state == TestGenRunState.Candidate || TestGenRunState.Final.Contains(state);

    private IEnumerable<BaseEvent> Message(TestGenRunActivityRow row)
    {
        var text = row.Text ?? "";
        var id = MessageId(row.Seq, row.Type);
        if (_sent.TryGetValue(row.Seq, out var sent))
        {
            // The entry grew: only what is new goes out, and only while its message is still open.
            if (text.Length > sent && _open?.Seq == row.Seq)
            {
                _sent[row.Seq] = text.Length;
                yield return Content(row.Type, id, text[sent..]);
            }
            yield break;
        }
        foreach (var e in CloseMessage())
        {
            yield return e;
        }
        _open = (row.Seq, row.Type);
        _sent[row.Seq] = text.Length;
        if (row.Type == ActivityType.Reasoning)
        {
            yield return new ReasoningStartEvent { MessageId = id };
            yield return new ReasoningMessageStartEvent { MessageId = id, Role = AGUIRoles.Assistant };
        }
        else
        {
            yield return new TextMessageStartEvent { MessageId = id, Role = AGUIRoles.Assistant };
        }
        if (text.Length > 0)
        {
            yield return Content(row.Type, id, text);
        }
    }

    private IEnumerable<BaseEvent> CloseMessage()
    {
        if (_open is not { } open)
        {
            yield break;
        }
        _open = null;
        var id = MessageId(open.Seq, open.Type);
        if (open.Type == ActivityType.Reasoning)
        {
            yield return new ReasoningMessageEndEvent { MessageId = id };
            yield return new ReasoningEndEvent { MessageId = id };
        }
        else
        {
            yield return new TextMessageEndEvent { MessageId = id };
        }
    }

    private static BaseEvent Content(string type, string id, string delta) => type == ActivityType.Reasoning
        ? new ReasoningMessageContentEvent { MessageId = id, Delta = delta }
        : new TextMessageContentEvent { MessageId = id, Delta = delta };

    private static string MessageId(long seq, string type) => $"{type}-{seq}";

    private static T? Data<T>(TestGenRunActivityRow row) where T : class =>
        row.DataJson is { } json ? JsonSerializer.Deserialize<T>(json, TestGenKinds.Json) : null;
}
