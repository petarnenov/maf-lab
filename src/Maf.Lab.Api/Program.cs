using Maf.Lab.Api.Agent.AGUI;
using Maf.Lab.A2A;
using Maf.Lab.Api.A2A;
using Maf.Lab.Api.Admin;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Jev;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Feedback;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Api.Storage;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

using Maf.Lab.Hosting;

namespace Maf.Lab.Api;

/// <summary>Agent host: chat over SSE, feedback, admin and eval reports.</summary>
public partial class Program
{
    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null,
        IEnumerable<System.Reflection.Assembly>? plugins = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        // What this service emits about itself. Nothing is exported unless an OTLP endpoint is configured.
        builder.AddLabTelemetry("maf-lab-api");

        // The one place every replica reads. A replica that cannot see it answers some requests correctly and
        // loses others, so it does not start at all.
        builder.AddSharedState();
        builder.RequireSharedState<Maf.Lab.Domain.SharedState.IRunStateStore>();

        // The api's drain on a graceful stop: Docker's stop grace (40 s) exceeds it, so a stopping replica finishes its
        // runs or records them cancelled before it can be killed (introduce-plugins decision 2).
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        // The installed plugins: read at run time, composed here (introduce-plugins decision 5).
        builder.AddMafPlugins(plugins);
        builder.Services.AddHttpClient("plugins");
        // The domains in use, as data (introduce-plugins decision 6): the built-in ones this deployment keeps and every
        // installed plugin's, rebuilt when the installed set changes.
        builder.Services.AddSingleton(sp => new DomainCatalogue(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>(),
            sp.GetServices<Maf.Lab.Plugins.Abstractions.IDomainBehaviour>(), sp.GetService<Plugins.PluginCatalogue>(),
            sp.GetService<Microsoft.Extensions.Options.IOptions<Plugins.PluginOptions>>()));

        builder.Services.AddMafIndexing(builder.Configuration);
        builder.Services.AddDevJwtAuthentication(builder.Configuration);
        builder.Services.AddA2APartnerAuthentication(builder.Configuration);
        builder.Services.AddAuthorizationBuilder();
        builder.Services.Configure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(AuthPolicies.Add);
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.Section));

        builder.Services.AddDbContextFactory<MafDbContext>(o => o
            .UseSqlite(builder.Configuration["Storage:ConnectionString"] ?? "Data Source=maf-lab.db")
            .AddInterceptors(new SqlitePragmaInterceptor())
            // One model per set of plugin tables, so two hosts with different plugins in one process never share one.
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, Storage.PluginModelCacheKeyFactory>());

        // Each built-in domain's chunks, keyed by domain, for the review queue to resolve a search's sources where they live.
        Maf.Lab.Api.BuiltIn.BuiltInDomains.AddStores(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<SystemPrompt>();
        builder.Services.AddSingleton<TokenCounter>();
        builder.Services.AddSingleton<ToolAudit>();
        builder.Services.AddHttpClient("mcp");
        builder.Services.AddSingleton<IToolSource, McpToolSource>();
        builder.Services.AddSingleton<ConversationService>();
        builder.Services.AddJevIntentClassifier(builder.Configuration);
        builder.Services.Configure<Agent.FeeAdjustmentOptions>(builder.Configuration.GetSection("FeeAdjustments"));
        builder.Services.AddScoped<Agent.FeeAdjustmentFlow>();
        builder.Services.AddScoped<Agent.ConfirmationService>();
        // The turn observers of the installed plugins, as one (decision 7); none of the core's own.
        builder.Services.AddSingleton<Agent.Tracing.TurnObservers>();
        // The one store, as a plugin reaches its own tables (EF's factory over DbContext).
        builder.Services.AddSingleton<IDbContextFactory<DbContext>, Storage.PluginDbContextFactory>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.ITurnAccess, Storage.TurnAccess>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.IConversationStore, Storage.ConversationStore>();
        builder.Services.AddScoped<ChatTurnRunner>();
        builder.Services.AddScoped<Agent.RunRejoin>();
        // Every agent reaches a browser through the Agent Framework's own AG-UI server (agui-protocol-only).
        builder.Services.AddAGUIHosting();
        builder.Services.AddSingleton<Agent.ChatAgent>();
        builder.Services.AddSingleton<Coverage.TestGenRunAgent>();
        builder.Services.AddSingleton<DatasetWriter>();
        builder.Services.Configure<AdminJobOptions>(builder.Configuration.GetSection("AdminJobs"));
        builder.Services.AddSingleton<AdminJobRunner>();
        builder.Services.Configure<Topology.TopologyOptions>(builder.Configuration.GetSection(Topology.TopologyOptions.Section));
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient("topology");
        builder.Services.AddSingleton<Topology.IServiceResolver, Topology.DnsServiceResolver>();
        // The graph store's driver, for the topology report's reachability probe only; the api reads no graph data.
        Maf.Lab.Retrieval.Graph.GraphServiceCollectionExtensions.AddGraphStore(builder.Services, builder.Configuration);
        builder.Services.AddSingleton<Topology.TopologyProbe>();
        builder.Services.Configure<Telemetry.TelemetryQueryOptions>(
            builder.Configuration.GetSection(Telemetry.TelemetryQueryOptions.Section));
        builder.Services.AddSingleton<Telemetry.TelemetryQueries>();
        // The card follows the installed set, per request (extract-billing): resolved for each well-known fetch, and read
        // per call by the extended-card handler.
        builder.Services.AddTransient(sp => A2A.BillingAgentCard.Installed(sp.GetRequiredService<Maf.Lab.Plugins.Abstractions.IInstalledPlugins>()));
        builder.Services.AddSingleton<Func<Maf.Lab.A2A.AgentCardDescriptor>>(sp => () => sp.GetRequiredService<Maf.Lab.A2A.AgentCardDescriptor>());
        builder.Services.Configure<A2A.ComplianceOptions>(builder.Configuration.GetSection(A2A.ComplianceOptions.Section));
        builder.Services.AddHttpClient("a2a-consult");
        builder.Services.AddSingleton<A2A.ComplianceConsultant>();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IReviewerConsultation>(sp => sp.GetRequiredService<A2A.ComplianceConsultant>());
        builder.Services.AddHttpClient("a2a-push");
        builder.Services.AddSingleton<A2A.PushNotificationDispatcher>();
        builder.Services.AddSingleton<global::A2A.ITaskStore, A2A.SqliteTaskStore>();
        // Singletons: the protocol endpoints are mapped once, and the partner is read from the current request
        // through IHttpContextAccessor rather than captured per instance.
        builder.Services.AddSingleton<A2A.AssistantBridge>();
        builder.Services.AddSingleton<global::A2A.IAgentHandler, A2A.BillingAgentHandler>();
        builder.Services.AddSingleton<global::A2A.ChannelEventNotifier>();
        builder.Services.AddSingleton<global::A2A.A2AServer>();
        // The SDK's server does the protocol; five operations it leaves throwing are implemented around it.
        builder.Services.AddSingleton<IPushConfigStore, A2A.SqlitePushConfigStore>();
        builder.Services.AddSingleton<global::A2A.IA2ARequestHandler, A2ARequestHandlerWithExtras>();
        builder.Services.Configure<Storage.MessageRetentionOptions>(
            builder.Configuration.GetSection(Storage.MessageRetentionOptions.Section));
        builder.Services.AddSingleton<Storage.MessageRetentionService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Storage.MessageRetentionService>());
        // The Coverage screen (add-coverage-dashboard-and-test-agent): snapshots, thresholds, and the runner that measures.
        builder.Services.Configure<Coverage.CoverageOptions>(builder.Configuration.GetSection(Coverage.CoverageOptions.Section));
        builder.Services.Configure<Coverage.CoverageRunnerOptions>(builder.Configuration.GetSection(Coverage.CoverageRunnerOptions.Section));
        builder.Services.AddSingleton<Coverage.IRepository, Coverage.GitRepository>();
        builder.Services.AddSingleton<Coverage.CoverageStore>();
        builder.Services.AddSingleton<Coverage.CoverageIngestor>();
        builder.Services.AddSingleton<Coverage.CoverageRefresher>();
        builder.Services.Configure<Coverage.TestAgentOptions>(builder.Configuration.GetSection(Coverage.TestAgentOptions.Section));
        builder.Services.AddSingleton<Coverage.ModelAvailability>();
        builder.Services.AddHttpClient(Coverage.TestAgentClient.HttpClientName);
        builder.Services.AddSingleton<Coverage.TestAgentClient>();
        builder.Services.AddHttpClient(Coverage.TestAgentProbe.HttpClientName);
        builder.Services.AddSingleton<Coverage.TestAgentProbe>();
        builder.Services.AddSingleton<Coverage.RunActivityStore>();
        builder.Services.AddSingleton<Coverage.TestGenRuns>();
        builder.Services.Configure<Coverage.GitHubOptions>(builder.Configuration.GetSection(Coverage.GitHubOptions.Section));
        builder.Services.AddHttpClient(Coverage.GitHubIssues.HttpClientName);
        builder.Services.AddSingleton<Coverage.GitHubIssues>();
        builder.Services.AddSingleton(sp => (Coverage.GitRepository)sp.GetRequiredService<Coverage.IRepository>());
        builder.Services.AddSingleton<Coverage.RepoWriter>();
        builder.Services.AddSingleton<Coverage.IRunVerifier, Coverage.RunVerifier>();
        builder.Services.AddSingleton<Coverage.CandidateDecisions>();
        builder.Services.AddSingleton<Coverage.RunFollower>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Coverage.RunFollower>());
        Coverage.CoverageRunnerRegistration.AddCoverageRunnerClient(builder.Services);

        var app = builder.Build();
        // Resolved now so a missing JEV_MAF_LAB is reported once at startup, not on the first turn.
        app.Services.GetRequiredService<JevCredential>();
        using (var scope = app.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>();
            using var db = dbFactory.CreateDbContext();
            DatabaseInitializer.InitializeAsync(db).GetAwaiter().GetResult();
        }

        // Every request, and the work it starts, reads this host's domains (ambient per flow, so two hosts in one test
        // process never see each other's).
        var domains = app.Services.GetRequiredService<DomainCatalogue>();
        app.Use(async (context, next) =>
        {
            using var _ = DomainCatalogue.Use(domains.Freeze());
            await next(context);
        });
        app.UseInstanceHeader();
        app.UseA2ASpecWire();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<Agent.AGUI.RunTap>();
        app.MapInstanceHealth();
        if (app.Configuration.GetValue("Auth:EnableDevIssuer", true))
        {
            app.MapDevIssuer();
        }
        app.MapChat();
        app.MapChatAgent();
        app.MapTestGenRunAgent();
        app.MapFeedback();
        app.MapAdminIndex();
        app.MapEvalReports();
        app.MapTelemetry();
        app.MapHistory();
        app.MapTopology();
        app.MapCompliance();
        app.MapIntentStats();
        app.MapJevStats();
        app.MapA2AAdmin();
        app.MapCoverage();
        app.MapA2ASurface();
        app.MapA2AProtocol();
        app.MapPlugins();
        app.MapMafPlugins();
        return app;
    }
}
