using Maf.Lab.Plugins.Abstractions;
using Microsoft.Agents.AI;

namespace Maf.Lab.Api.Agent.AGUI;

public sealed class CoreAgentRunInput : IAgentRunInput
{
    public string? ThreadId(AgentRunOptions? options) => TurnContents.Request(options)?.ThreadId;
}
