using Maf.Lab.Plugins.Abstractions;
using Microsoft.Agents.AI;
using AGUI.Abstractions;

namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>
/// The core's side of <see cref="IMafEndpoints"/>: a plugin's route group, and the one way a plugin serves an AG-UI
/// agent. It lives here, beside the AG-UI mappings, so a plugin never references an AG-UI type and every agent goes
/// through the Agent Framework's own server with the official mappings (agui-protocol-only).
/// </summary>
internal sealed class PluginEndpoints(IEndpointRouteBuilder routes) : IMafEndpoints
{
    public IEndpointRouteBuilder Routes { get; } = routes;

    public IEndpointConventionBuilder MapPluginAgent(string pattern, AIAgent agent,
        Func<string?, CancellationToken, Task<IResult?>>? checkThread = null)
    {
        var mapped = Routes.MapAgent(pattern, agent, AGUIMappings.Agents());
        if (checkThread is not null)
        {
            mapped.AddEndpointFilter(async (context, next) =>
            {
                var input = context.Arguments.OfType<RunAgentInput>().FirstOrDefault();
                var rejected = await checkThread(input?.ThreadId, context.HttpContext.RequestAborted);
                return rejected is not null ? rejected : await next(context);
            });
        }
        return mapped;
    }
}
