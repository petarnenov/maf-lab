using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Maf.Lab.Api.Agent.AGUI;
using Maf.Lab.Api.Agent.Tracing;
using Maf.Lab.Domain.SharedState;
using Maf.Lab.Retrieval.Auth;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// The billing assistant as an Agent Framework agent (agui-protocol-only): what the official AG-UI server runs for
/// <c>/api/chat</c>. A run is a turn of <see cref="ChatTurnRunner"/>, an answer to a question the previous run asked
/// (<see cref="ConfirmationService"/>), or a rejoin of a run the caller lost (<see cref="RunRejoin"/>). The caller, its
/// tenant and its token come from the request, never from the run's input.
/// </summary>
public sealed class ChatAgent(IHttpContextAccessor http) : AIAgent
{
    public const string AgentName = "maf-lab-assistant";

    public override string? Name => AgentName;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new ChatSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(JsonSerializer.SerializeToElement(new { }));

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new ChatSession());

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null,
        AgentRunOptions? options = null, CancellationToken cancellationToken = default) =>
        await RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages,
        AgentSession? session = null, AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = TurnContents.Request(options)
            ?? throw new InvalidOperationException("The chat agent runs only behind the AG-UI server.");
        var context = http.HttpContext ?? throw new InvalidOperationException("The chat agent runs only inside a request.");
        var services = context.RequestServices;
        var principal = services.GetRequiredService<IPrincipalAccessor>().Current;
        var token = context.Request.Headers.Authorization.ToString() is { Length: > 7 } header ? header["Bearer ".Length..].Trim() : "";

        var output = Channel.CreateUnbounded<ChatResponseUpdate>(new UnboundedChannelOptions { SingleReader = true });
        var observation = services.GetRequiredService<TurnObservers>().Begin(request.RunId);
        string? error = null;
        var run = Task.Run(async () =>
        {
            try
            {
                if (request.Answer is { } answer)
                {
                    await services.GetRequiredService<ConfirmationService>()
                        .ResumeAsync(principal, token, request.ThreadId, answer, output.Writer, cancellationToken);
                }
                else if (request.Message is null && request.ParentRunId is { } lost)
                {
                    await services.GetRequiredService<RunRejoin>().ReplayAsync(principal, request.ThreadId, lost, output.Writer, cancellationToken);
                }
                else
                {
                    var result = await services.GetRequiredService<ChatTurnRunner>().RunAsync(principal, token, request.ThreadId,
                        request.Message ?? "", request.RunId, output.Writer, cancellationToken, request.State, observation);
                    error = result.Error;
                }
            }
            finally
            {
                output.Writer.TryComplete();
            }
        }, cancellationToken);

        try
        {
            await foreach (var update in output.Reader.ReadAllAsync(cancellationToken))
            {
                yield return new AgentResponseUpdate(update);
            }
            await run;
        }
        finally
        {
            await observation.CompleteAsync();
        }

        if (error is not null)
        {
            // The turn has been kept with its error; the server ends the run in the protocol's error, with short text
            // and nothing internal.
            throw new TurnFailedException();
        }
    }

    private sealed class ChatSession : AgentSession;
}

/// <summary>A turn that could not complete; its error is already recorded with the turn.</summary>
public sealed class TurnFailedException() : Exception("The assistant could not complete this answer.");
