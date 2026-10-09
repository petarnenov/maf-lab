using System.Text.Json;
using System.Text.Json.Nodes;
using Maf.Lab.TestGen;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Plugins.Coverage;

/// <summary>
/// A test-generation run as the browser sees it, in Agent Framework content that the official AG-UI server maps to the
/// protocol's own events (agui-protocol-only): each phase a step, each tool call a call and its result, the model's text
/// and reasoning its messages, and everything else — the summary, each attempt's result, the stop, a takeover after a
/// restart, a dropped record — the run's shared state. Built only from what the database holds, so every replica tells the
/// same run the same way. One projection per stream: it remembers what it has already said.
/// </summary>
public sealed class RunActivityProjection(string runId)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<long, int> _sent = [];
    private readonly List<JsonNode> _attempts = [];
    private readonly List<int> _resumes = [];
    private JsonNode? _stop;
    private bool _dropped;
    private RunSummary? _summary;
    private string? _state;
    private string? _step;
    private (long Seq, string Type)? _open;

    public string ThreadId => $"testgen:{runId}";

    /// <summary>The highest entry sequence this stream has shown: what changed since is what comes next.</summary>
    public long Cursor { get; private set; }

    /// <summary>The run's state, when the summary changed since the last one sent.</summary>
    public IEnumerable<ChatResponseUpdate> Summary(RunSummary summary)
    {
        _summary = summary;
        return State();
    }

    /// <summary>What the entries new or grown since the cursor say, in the agent's order.</summary>
    public IEnumerable<ChatResponseUpdate> Entries(IReadOnlyList<TestGenRunActivityRow> rows, bool dropped)
    {
        if (dropped && !_dropped)
        {
            _dropped = true;
            foreach (var c in State())
            {
                yield return c;
            }
        }
        foreach (var row in rows.OrderBy(r => r.Seq))
        {
            Cursor = Math.Max(Cursor, row.LastSeq);
            if (row.Type is ActivityType.Text or ActivityType.Reasoning)
            {
                if (Message(row) is { } message)
                {
                    yield return message;
                }
                continue;
            }
            _open = null;
            switch (row.Type)
            {
                case ActivityType.Phase:
                    if (_step is { } previous)
                    {
                        yield return Update(AgentContents.StepFinished(previous));
                    }
                    _step = row.Attempt == 0 ? $"baseline: {row.Phase}" : $"attempt {row.Attempt}: {row.Phase}";
                    yield return Update(AgentContents.StepStarted(_step));
                    break;
                case ActivityType.Tool when Data<ToolActivity>(row) is { } tool:
                    var callId = $"tool-{row.Seq}";
                    yield return Update(new FunctionCallContent(callId, tool.Name, new Dictionary<string, object?> { ["path"] = tool.Path }));
                    yield return Update(new FunctionResultContent(callId,
                        JsonSerializer.Serialize(new { outcome = tool.Outcome, summary = tool.Summary }, Json)));
                    break;
                case ActivityType.Attempt when Data<AttemptActivity>(row) is { } result:
                    _attempts.Add(AttemptValue(row.Attempt, result));
                    foreach (var c in State())
                    {
                        yield return c;
                    }
                    break;
                case ActivityType.Resumed:
                    // The agent restarted: the step it was in is over, and the attempt it resumes at starts again.
                    if (CloseStep() is { } interrupted)
                    {
                        yield return interrupted;
                    }
                    _resumes.Add(row.Attempt);
                    foreach (var c in State())
                    {
                        yield return c;
                    }
                    break;
                case ActivityType.Stopped when Data<StoppedActivity>(row) is { } stop:
                    // The agent is done: its last step is over, and the timeline closes on why.
                    if (CloseStep() is { } open)
                    {
                        yield return open;
                    }
                    _stop = JsonSerializer.SerializeToNode(new
                    {
                        reason = stop.Reason,
                        lastAttempt = stop.LastAttempt,
                        bestPct = stop.BestPct,
                        notStarted = stop.NotStarted,
                    }, Json);
                    foreach (var c in State())
                    {
                        yield return c;
                    }
                    break;
            }
        }
    }

    /// <summary>The run's work is over: its last step closes. How it ended is in its state.</summary>
    public IEnumerable<ChatResponseUpdate> Ended()
    {
        if (CloseStep() is { } step)
        {
            yield return step;
        }
    }

    /// <summary>Whether the agent's work on a run in this state is over, so the stream ends.</summary>
    public static bool IsOver(string state) => state == TestGenRunState.Candidate || TestGenRunState.Final.Contains(state);

    /// <summary>Whether a run that ended this way ended in error: then the run ends in the protocol's error.</summary>
    public static bool Failed(string state) => state is TestGenRunState.Failed or TestGenRunState.Canceled;

    /// <summary>
    /// The run's state: its summary, the attempts finished so far, the stop, each takeover and whether the record was cut.
    /// Sent whole, and only when it changed.
    /// </summary>
    private IEnumerable<ChatResponseUpdate> State()
    {
        if (_summary is null)
        {
            yield break;
        }
        var state = JsonSerializer.SerializeToNode(_summary, Json)!.AsObject();
        state["attempts"] = new JsonArray([.. _attempts.Select(a => a.DeepClone())]);
        state["stop"] = _stop?.DeepClone();
        state["resumes"] = new JsonArray([.. _resumes.Select(r => (JsonNode)JsonValue.Create(r))]);
        state["dropped"] = _dropped;
        var json = state.ToJsonString(Json);
        if (json == _state)
        {
            yield break;
        }
        _state = json;
        yield return Update(AgentContents.State(JsonSerializer.SerializeToElement(state, Json)));
    }

    private ChatResponseUpdate? CloseStep()
    {
        if (_step is not { } step)
        {
            return null;
        }
        _step = null;
        return Update(AgentContents.StepFinished(step));
    }

    /// <summary>
    /// A text or reasoning entry: all of it the first time, then only what it grew by while its message is still the
    /// open one. The message is keyed by the entry, so the server opens and closes it around other content.
    /// </summary>
    private ChatResponseUpdate? Message(TestGenRunActivityRow row)
    {
        var text = row.Text ?? "";
        if (_sent.TryGetValue(row.Seq, out var sent))
        {
            if (text.Length <= sent || _open?.Seq != row.Seq)
            {
                return null;
            }
            _sent[row.Seq] = text.Length;
            return Content(row, text[sent..]);
        }
        _open = (row.Seq, row.Type);
        _sent[row.Seq] = text.Length;
        return text.Length > 0 ? Content(row, text) : null;
    }

    private static ChatResponseUpdate Content(TestGenRunActivityRow row, string delta) =>
        new(ChatRole.Assistant, [row.Type == ActivityType.Reasoning ? new TextReasoningContent(delta) : new TextContent(delta)])
        {
            MessageId = MessageId(row),
        };

    private static ChatResponseUpdate Update(AIContent content) => new(ChatRole.Assistant, [content]);

    /// <summary>The message an entry's content belongs to.</summary>
    public static string MessageId(TestGenRunActivityRow row) => $"{row.Type}-{row.Seq}";

    /// <summary>
    /// An attempt's result as the browser reads it. What the attempt ran (<c>run</c>) and its whole-suite confirmation
    /// (<c>confirmation</c>) are present only when the entry recorded them, so an entry from before reads as it did.
    /// </summary>
    internal static JsonNode AttemptValue(int attempt, AttemptActivity result)
    {
        var value = JsonSerializer.SerializeToNode(new
        {
            attempt,
            before = result.Before,
            after = result.After,
            build = result.Build,
            tests = result.Tests,
            errors = result.Errors,
            violations = result.Violations,
        }, Json)!.AsObject();
        if (result.Run is { } run)
        {
            value["run"] = JsonSerializer.SerializeToNode(run, TestGenKinds.Json);
        }
        if (result.Confirmation is { } confirmation)
        {
            value["confirmation"] = JsonSerializer.SerializeToNode(confirmation, TestGenKinds.Json);
        }
        return value;
    }

    private static T? Data<T>(TestGenRunActivityRow row) where T : class =>
        row.DataJson is { } json ? JsonSerializer.Deserialize<T>(json, TestGenKinds.Json) : null;
}
