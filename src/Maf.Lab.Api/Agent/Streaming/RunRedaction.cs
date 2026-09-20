using AGUI.Abstractions;

namespace Maf.Lab.Api.Agent.Streaming;

/// <summary>
/// What the adapter produces is not what a client may see.
///
/// It attaches the whole originating chat update to every event as `rawEvent` — which carries a tool call's
/// arguments and a tool result's full payload, including document text — and it renders a tool call's arguments
/// and result verbatim. This system's contract is that identifiers and summaries travel and free text does not,
/// so each event passes through here first. The protocol's identifiers and ordering, which are the fiddly part,
/// are left exactly as the adapter produced them.
///
/// The run's own lifecycle is dropped: the runner opens and closes the run itself, because a turn's first trace
/// events happen before the model is ever called and its last word may be that it is waiting for a person.
/// </summary>
public sealed class RunRedaction(Func<string, string?> argumentSummary, Func<string, string?> resultSummary)
{
    /// <summary>
    /// Set when the adapter reported the run as failed. It catches what the model threw and turns it into an
    /// event rather than letting it out, so without reading this a failed turn would end as a success.
    /// </summary>
    public bool Failed { get; private set; }

    /// <summary>The event to write, or null when it must not travel.</summary>
    public BaseEvent? Apply(BaseEvent e)
    {
        if (e is RunErrorEvent)
        {
            Failed = true;
            return null;
        }

        // The runner owns the run's beginning and end.
        if (e is RunStartedEvent or RunFinishedEvent)
        {
            return null;
        }

        e.RawEvent = null;

        switch (e)
        {
            case ToolCallArgsEvent args:
                // The protocol renders a call's arguments before the call is invoked, so the summary the audit
                // will write does not exist yet: it is taken from the arguments themselves, by the same rule.
                args.Delta = argumentSummary(args.ToolCallId) ?? ArgumentSummary.FromJson(args.Delta);
                break;

            case ToolCallResultEvent result:
                result.Content = resultSummary(result.ToolCallId) ?? "done";
                break;
        }

        return e;
    }

}
