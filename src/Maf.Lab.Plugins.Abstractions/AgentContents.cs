using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Maf.Lab.Plugins.Abstractions;

/// <summary>Agent Framework content mapped by the core's official AG-UI server; never a protocol event.</summary>
public static class AgentContents
{
    public const string StateType = "application/vnd.maf-lab.state+json";
    public const string StepStartedType = "application/vnd.maf-lab.step-started";
    public const string StepFinishedType = "application/vnd.maf-lab.step-finished";

    public static AIContent State(JsonElement snapshot) => new DataContent(Encoding.UTF8.GetBytes(snapshot.GetRawText()), StateType);
    public static AIContent StepStarted(string name) => new DataContent(Encoding.UTF8.GetBytes(name), StepStartedType);
    public static AIContent StepFinished(string name) => new DataContent(Encoding.UTF8.GetBytes(name), StepFinishedType);
}

/// <summary>The thread an official agent run was requested on; input parsing remains the core's.</summary>
public interface IAgentRunInput
{
    string? ThreadId(AgentRunOptions? options);
}
