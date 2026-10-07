using Maf.Lab.A2A;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.A2A;

/// <summary>
/// The a2a plugin's in-process part (extract-a2a): the assistant's agent card and the handler partners talk to. The
/// handler reaches the assistant, billing's tools and the audit only through the core's ports; the protocol server,
/// partner authentication and the surface and protocol routes are this plugin's. Batch 1 of the change: the task and
/// push stores, their tables and the admin page follow in batch 2.
/// </summary>
public sealed class A2APlugin : IMafPlugin, IContributesServices, IContributesEndpoints
{
    public const string PluginName = "a2a";

    public string Name => PluginName;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Partner tokens: a second JwtBearer scheme beside the dev issuer's, which stays the default.
        services.AddA2APartnerAuthentication(configuration);
        // The card follows the installed set, per request (extract-billing): resolved for each well-known fetch, and read
        // per call by the extended-card handler.
        services.AddTransient(sp => AssistantAgentCard.Installed(sp.GetRequiredService<IInstalledPlugins>()));
        services.AddSingleton<Func<AgentCardDescriptor>>(sp => () => sp.GetRequiredService<AgentCardDescriptor>());
        // A singleton: the protocol endpoints are mapped once, and the partner is read from the current request.
        services.AddSingleton<global::A2A.IAgentHandler, AssistantAgentHandler>();
        services.AddSingleton<global::A2A.ChannelEventNotifier>();
        services.AddSingleton<global::A2A.A2AServer>();
        // The SDK's server does the protocol; five operations it leaves throwing are implemented around it.
        services.AddSingleton<global::A2A.IA2ARequestHandler, A2ARequestHandlerWithExtras>();
    }

    /// <summary>The agent card at the well-known path, the token endpoint and the protocol, at the root behind the gate.</summary>
    public void MapEndpoints(IMafEndpoints endpoints)
    {
        endpoints.Routes.MapA2ASurface();
        endpoints.Routes.MapA2AProtocol();
    }
}
