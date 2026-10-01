using System.Text.Json;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// One run of the chat agent as the request asked for it, in this system's own terms (agui-protocol-only).
/// <c>Agent/AGUI</c> reads it off the protocol's input.
/// </summary>
/// <param name="ThreadId">The conversation, already resolved for the caller by the endpoint.</param>
/// <param name="RunId">The run, which is also the id of the turn it records.</param>
/// <param name="ParentRunId">On a rejoin, the run the caller lost and wants to see again.</param>
/// <param name="Message">The user's newest message; null on a resume or a rejoin.</param>
/// <param name="State">The client's shared state, of which only <c>focus</c> is read.</param>
/// <param name="Answer">A person's answer to what an earlier run asked them.</param>
public sealed record ChatRunRequest(
    string ThreadId,
    string RunId,
    string? ParentRunId,
    string? Message,
    JsonElement? State,
    PersonAnswer? Answer);
