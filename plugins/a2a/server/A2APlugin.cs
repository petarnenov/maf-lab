using Maf.Lab.A2A;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Maf.Lab.Plugins.A2A;

/// <summary>
/// The a2a plugin's in-process part (extract-a2a): the assistant's agent card and the handler partners talk to. The
/// handler reaches the assistant, billing's tools and the audit only through the core's ports; the protocol server,
/// partner authentication, protocol routes, task and push stores, tables, admin screen and open work belong to this plugin.
/// </summary>
public sealed class A2APlugin : IMafPlugin, IContributesServices, IContributesEndpoints, IContributesModel, IContributesOpenWork
{
    public const string PluginName = "a2a";

    public string Name => PluginName;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Partner tokens: a second JwtBearer scheme beside the dev issuer's, which stays the default.
        services.AddA2APartnerAuthentication(configuration);
        services.RequireA2AStoreKeyspace();
        services.AddHttpClient("a2a-push");
        services.AddSingleton<IPluginRouteAccess, PartnerPluginAccess>();
        services.AddSingleton<PushNotificationDispatcher>();
        services.AddSingleton<SqliteTaskStore>();
        services.AddSingleton<global::A2A.ITaskStore>(sp => sp.GetRequiredService<SqliteTaskStore>());
        services.AddSingleton<IA2ATaskOwner>(sp => sp.GetRequiredService<SqliteTaskStore>());
        services.AddSingleton<IPushConfigStore, SqlitePushConfigStore>();
        // The card follows the installed set, per request (extract-billing): resolved for each well-known fetch, and read
        // per call by the extended-card handler.
        services.AddTransient(sp => PartnerPluginAccess.Current is { } readers
            ? AssistantAgentCard.ForReaders(sp.GetRequiredService<IInstalledPlugins>(), readers)
            : AssistantAgentCard.Installed(sp.GetRequiredService<IInstalledPlugins>()));
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
        A2AAdminEndpoints.Map(endpoints.Routes);
    }

    public void ConfigureModel(ModelBuilder model)
    {
        var tasks = model.Entity<A2ATaskRow>();
        tasks.ToTable("A2ATasks");
        tasks.HasKey(x => x.Id);
        tasks.HasIndex(x => new { x.ContextId, x.UpdatedAt });
        tasks.HasIndex(x => new { x.PartnerId, x.UpdatedAt });
        tasks.HasIndex(nameof(A2ATaskRow.TenantId), nameof(A2ATaskRow.UpdatedAt));
        var configs = model.Entity<A2APushConfigRow>();
        configs.ToTable("A2APushConfigs");
        configs.HasKey(x => x.Id);
        configs.HasIndex(x => x.TaskId);
        var deliveries = model.Entity<A2APushDeliveryRow>();
        deliveries.ToTable("A2APushDeliveries");
        deliveries.HasIndex(x => new { x.TaskId, x.At });
    }

    public IOpenWork CreateOpenWork(IServiceProvider services) =>
        new A2AOpenWork(services.GetRequiredService<IDbContextFactory<DbContext>>(),
            services.GetRequiredService<global::A2A.A2AServer>());
}
