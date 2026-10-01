using System.Text.Json.Serialization.Metadata;
using AGUI.Abstractions;
using AGUI.Server;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Maf.Lab.Api.Agent.AGUI;

/// <summary>
/// How every agent reaches a browser: the Agent Framework's own AG-UI server, and nothing of ours on the wire
/// (agui-protocol-only, DECISIONS §74). Three settings make what it writes acceptable to the official client.
/// </summary>
public static class AGUIHosting
{
    /// <summary>The official server, set up so the official client can read what it writes.</summary>
    public static IServiceCollection AddAGUIHosting(this IServiceCollection services)
    {
        services.AddAGUIServer();
        services.AddHttpContextAccessor();
        // The hosting package never registers the interrupt contents, so any interrupt would end the run in an error.
        services.Configure<HttpJsonOptions>(o => AGUIJsonUtilities.RegisterInterruptContentTypes(o.SerializerOptions));
        // Its own resolver first: optional fields are omitted rather than written as null, which the client rejects.
        // And no event carries `rawEvent`, the whole model update it came from, arguments and results included.
        services.PostConfigure<HttpJsonOptions>(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0,
            AGUIJsonUtilities.DefaultTypeInfoResolver.WithAddedModifier(WithoutRawEvent)));
        return services;
    }

    /// <summary>
    /// An agent as an AG-UI endpoint for signed-in callers, with its own mappings. The returned builder takes endpoint
    /// filters, which see the request's <see cref="RunAgentInput"/> before the run starts.
    /// </summary>
    public static RouteHandlerBuilder MapAgent(this IEndpointRouteBuilder app, string pattern, AIAgent agent,
        AGUIStreamOptions mappings)
    {
        var endpoint = (RouteHandlerBuilder)app.MapAGUIServer(pattern, agent);
        // Endpoint metadata wins over the options in DI, so each agent's mappings stay with its own endpoint.
        return endpoint.RequireAuthorization().WithMetadata(mappings);
    }

    private static void WithoutRawEvent(JsonTypeInfo type)
    {
        if (!typeof(BaseEvent).IsAssignableFrom(type.Type))
        {
            return;
        }
        foreach (var property in type.Properties)
        {
            if (property.Name == "rawEvent")
            {
                property.ShouldSerialize = static (_, _) => false;
            }
        }
    }
}
