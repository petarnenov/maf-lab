using System.Runtime.CompilerServices;
using System.Text.Json;
using Maf.Lab.Api.Agent.AGUI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Lab.Api.Coverage;

/// <summary>
/// A test-generation run as an AG-UI agent (agui-protocol-only), the only way a browser follows one: a run of this agent
/// on thread <c>testgen:&lt;run id&gt;</c> replays the run from the start, then follows it live until the agent's work is
/// over. It reads the shared database, so any replica serves any run, and a browser that lost the stream simply runs it
/// again. The browser never talks to the test agent itself.
/// </summary>
public sealed class TestGenRunAgent(TestGenRuns runs, RunActivityStore activity, IOptions<TestAgentOptions> options) : AIAgent
{
    public const string AgentName = "testgen-run";
    public const string ThreadPrefix = "testgen:";

    public override string? Name => AgentName;

    /// <summary>The run a thread is about, or null for a thread that is not a run's.</summary>
    public static string? RunIdOf(string? threadId) =>
        threadId is { Length: > 8 } t && t.StartsWith(ThreadPrefix, StringComparison.Ordinal) ? t[ThreadPrefix.Length..] : null;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new RunSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(JsonSerializer.SerializeToElement(new { }));

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new RunSession());

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null,
        AgentRunOptions? options = null, CancellationToken cancellationToken = default) =>
        await RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages,
        AgentSession? session = null, AgentRunOptions? runOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var runId = RunIdOf(TurnContents.Request(runOptions)?.ThreadId)
            ?? throw new InvalidOperationException("A run is followed on its own thread.");
        var run = await runs.GetAsync(runId, cancellationToken) ?? throw new InvalidOperationException("No such run.");
        var view = new RunActivityProjection(run.Id);
        var pollEvery = options.Value.EventPollEvery;

        var summary = RunSummary.Of(run);
        foreach (var u in view.Summary(summary))
        {
            yield return new AgentResponseUpdate(u);
        }
        while (true)
        {
            // Entries are read after the state they belong to: the api stores them first, so a final state is never
            // reported ahead of what led to it.
            foreach (var u in view.Entries(await activity.ChangedSinceAsync(run.Id, view.Cursor, cancellationToken), run.ActivityDropped))
            {
                yield return new AgentResponseUpdate(u);
            }
            if (RunActivityProjection.IsOver(summary.State))
            {
                foreach (var u in view.Ended())
                {
                    yield return new AgentResponseUpdate(u);
                }
                break;
            }
            await Task.Delay(pollEvery, cancellationToken);
            run = await runs.GetAsync(run.Id, cancellationToken) ?? run;
            summary = RunSummary.Of(run);
            foreach (var u in view.Summary(summary))
            {
                yield return new AgentResponseUpdate(u);
            }
        }

        if (RunActivityProjection.Failed(summary.State))
        {
            // Its state already says how and why; the server ends the run in the protocol's error.
            throw new RunEndedInErrorException();
        }
    }

    private sealed class RunSession : AgentSession;
}

/// <summary>A run that failed or was canceled; its state carries the reason.</summary>
public sealed class RunEndedInErrorException() : Exception("The run did not complete.");
