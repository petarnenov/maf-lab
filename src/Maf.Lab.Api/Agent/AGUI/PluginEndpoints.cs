using Maf.Lab.Plugins.Abstractions;
using Microsoft.Agents.AI;

namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>
/// The core's side of <see cref="IMafEndpoints"/>: a plugin's route group, and the one way a plugin serves an AG-UI
/// agent. It lives here, beside the AG-UI mappings, so a plugin never references an AG-UI type and every agent goes
/// through the Agent Framework's own server with the official mappings (agui-protocol-only).
/// </summary>
internal sealed class PluginEndpoints(IEndpointRouteBuilder routes) : IMafEndpoints
{
    public IEndpointRouteBuilder Routes { get; } = routes;

    public IEndpointConventionBuilder MapPluginAgent(string pattern, AIAgent agent) =>
        Routes.MapAgent(pattern, agent, AGUIMappings.Agents());
}
