using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Server;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>
/// The agents' side of the protocol, in Agent Framework terms (agui-protocol-only): the run's input read off the
/// official server's request, and what this system adds to a run expressed as content the server's mappings turn into
/// the protocol's own events (<see cref="AGUIMappings"/>). Nothing here writes an event.
/// </summary>
public static class TurnContents
{
    /// <summary>A data card for the client: becomes <c>ACTIVITY_SNAPSHOT</c>.</summary>
    public const string ActivityType = "application/vnd.maf-lab.activity+json";

    /// <summary>The run's shared state: becomes <c>STATE_SNAPSHOT</c>.</summary>
    public const string StateType = AgentContents.StateType;

    /// <summary>A step of the run starting or finishing: becomes <c>STEP_STARTED</c>/<c>STEP_FINISHED</c>.</summary>
    public const string StepStartedType = AgentContents.StepStartedType;
    public const string StepFinishedType = AgentContents.StepFinishedType;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What the request asked for; null when it did not come through the AG-UI server.</summary>
    public static ChatRunRequest? Request(AgentRunOptions? options)
    {
        if (options is not ChatClientAgentRunOptions { ChatOptions: { } chat } || !chat.TryGetRunAgentInput(out var input) || input is null)
        {
            return null;
        }
        var message = input.Messages?.OfType<AGUIUserMessage>().LastOrDefault() is { } last ? last.Content.ToString() : null;
        var resume = input.Resume?.FirstOrDefault();
        return new ChatRunRequest(
            input.ThreadId ?? "",
            input.RunId ?? "",
            string.IsNullOrWhiteSpace(input.ParentRunId) ? null : input.ParentRunId,
            resume is null ? message?.Trim() : null,
            input.State,
            resume is null ? null : new PersonAnswer(resume.InterruptId, resume.Payload));
    }

    /// <summary>A data card, keyed by the tool call it came from.</summary>
    public static AIContent Activity(string messageId, string activityType, JsonElement content) =>
        Data(ActivityType, new { messageId, activityType, content });

    /// <summary>The run's shared state: the account in focus, as an id or null, and nothing else.</summary>
    public static AIContent Focus(string? accountId) =>
        Data(StateType, new { focus = accountId is null ? null : new { accountId } });

    /// <summary>Any shared state the agent keeps, as a whole snapshot.</summary>
    public static AIContent State(JsonElement snapshot) => AgentContents.State(snapshot);

    public static AIContent StepStarted(string name) => AgentContents.StepStarted(name);

    public static AIContent StepFinished(string name) => AgentContents.StepFinished(name);

    /// <summary>The run stops to ask a person: the server finishes it with the protocol's interrupt.</summary>
    public static AIContent Ask(PersonQuestion question) => new InterruptRequestContent(question.Id)
    {
        Reason = question.Reason,
        Message = question.Message,
        ToolCallId = question.ToolCallId,
        ExpiresAt = question.ExpiresAt,
        ResponseSchema = question.ResponseSchema,
        Metadata = question.Metadata,
    };

    private static DataContent Data(string mediaType, object value) =>
        new(JsonSerializer.SerializeToUtf8Bytes(value, Json), mediaType);
}
