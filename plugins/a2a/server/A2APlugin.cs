using Maf.Lab.A2A;
using Maf.Lab.Plugins.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maf.Lab.Plugins.A2A;

/// <summary>
/// The a2a plugin's in-process part (extract-a2a): the assistant's agent card and the handler partners talk to, the
/// protocol server, partner authentication, the surface, protocol and admin routes, and the task and push stores over
/// this plugin's own tables. The handler reaches the assistant, billing's tools and the audit only through the core's
/// ports.
/// </summary>
public sealed class A2APlugin : IMafPlugin, IContributesServices, IContributesEndpoints, IContributesModel, IContributesOpenWork
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
        // Tasks and webhooks in the store the replicas share, so any replica answers for a task; every save tells the
        // dispatcher, which delivers one POST per state change.
        services.AddHttpClient("a2a-push");
        services.AddSingleton<PushNotificationDispatcher>();
        services.AddSingleton<global::A2A.ITaskStore, SqliteTaskStore>();
        services.AddSingleton<IPushConfigStore, SqlitePushConfigStore>();
    }

    public void ConfigureModel(ModelBuilder model) => A2ATables.Configure(model);

    public IOpenWork CreateOpenWork(IServiceProvider services) =>
        new A2AOpenWork(services.GetRequiredService<IDbContextFactory<DbContext>>(), services.GetRequiredService<global::A2A.ITaskStore>(),
            services.GetRequiredService<TimeProvider>());

    /// <summary>
    /// The agent card at the well-known path, the token endpoint and the protocol, at the root behind the gate; and the
    /// firm's view of the activity.
    /// </summary>
    public void MapEndpoints(IMafEndpoints endpoints)
    {
        endpoints.Routes.MapA2ASurface();
        endpoints.Routes.MapA2AProtocol();
        A2AAdminEndpoints.Map(endpoints.Routes);
    }
}

/// <summary>
/// The tasks partners started that have not ended, so plugin-off refuses while one runs or stops them first. A stop goes
/// through the task store, the one place every transition passes: each task is saved canceled (its guarded write keeps
/// an end already reached), the run watching that store stops at its next step, and the partner's webhook hears it.
/// </summary>
internal sealed class A2AOpenWork(IDbContextFactory<DbContext> db, global::A2A.ITaskStore tasks, TimeProvider time) : IOpenWork
{
    public async Task<IReadOnlyList<OpenWorkItem>> ListOpenAsync(CancellationToken ct)
    {
        var open = await OpenAsync(ct);
        return [.. open.Select(t => new OpenWorkItem("a2a.task", t.Id, t.State))];
    }

    public async Task CancelAllAsync(CancellationToken ct)
    {
        foreach (var row in await OpenAsync(ct))
        {
            if (await tasks.GetTaskAsync(row.Id, ct) is not { } task)
            {
                continue;
            }
            task.Status = new global::A2A.TaskStatus { State = global::A2A.TaskState.Canceled, Timestamp = time.GetUtcNow() };
            await tasks.SaveTaskAsync(row.Id, task, ct);
        }
    }

    private async Task<List<A2ATaskRow>> OpenAsync(CancellationToken ct)
    {
        await using var ctx = await db.CreateDbContextAsync(ct);
        return await ctx.Set<A2ATaskRow>().AsNoTracking().Where(t => !SqliteTaskStore.Terminal.Contains(t.State)).ToListAsync(ct);
    }
}
