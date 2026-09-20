using Maf.Lab.A2A;
using Maf.Lab.Api.A2A;
using Maf.Lab.Api.Admin;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Feedback;
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

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        builder.Services.AddMafIndexing(builder.Configuration);
        builder.Services.AddDevJwtAuthentication(builder.Configuration);
        builder.Services.AddA2APartnerAuthentication(builder.Configuration);
        builder.Services.AddAuthorizationBuilder();
        builder.Services.Configure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(AuthPolicies.Add);
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.Section));

        builder.Services.AddDbContextFactory<MafDbContext>(o => o
            .UseSqlite(builder.Configuration["Storage:ConnectionString"] ?? "Data Source=maf-lab.db")
            .AddInterceptors(new SqlitePragmaInterceptor()));

        builder.Services.AddSingleton<SystemPrompt>();
        builder.Services.AddSingleton<TokenCounter>();
        builder.Services.AddSingleton<ToolAudit>();
        builder.Services.AddHttpClient("mcp");
        builder.Services.AddSingleton<IToolSource, McpToolSource>();
        builder.Services.AddSingleton<ConversationService>();
        builder.Services.AddSingleton<IIntentClassifier, ModelIntentClassifier>();
        builder.Services.Configure<Agent.FeeAdjustmentOptions>(builder.Configuration.GetSection("FeeAdjustments"));
        builder.Services.AddScoped<Agent.FeeAdjustmentFlow>();
        builder.Services.AddScoped<Agent.ConfirmationService>();
        builder.Services.AddScoped<ChatTurnRunner>();
        builder.Services.AddSingleton<DatasetWriter>();
        builder.Services.Configure<AdminJobOptions>(builder.Configuration.GetSection("AdminJobs"));
        builder.Services.AddSingleton<AdminJobRunner>();
        builder.Services.Configure<Topology.TopologyOptions>(builder.Configuration.GetSection(Topology.TopologyOptions.Section));
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient("topology");
        builder.Services.AddSingleton<Topology.IServiceResolver, Topology.DnsServiceResolver>();
        builder.Services.AddSingleton<Topology.TopologyProbe>();
        builder.Services.AddSingleton(A2A.BillingAgentCard.Descriptor);
        builder.Services.Configure<A2A.ComplianceOptions>(builder.Configuration.GetSection(A2A.ComplianceOptions.Section));
        builder.Services.AddHttpClient("a2a-consult");
        builder.Services.AddSingleton<A2A.ComplianceConsultant>();
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
        builder.Services.Configure<Agent.Tracing.TracingOptions>(builder.Configuration.GetSection(Agent.Tracing.TracingOptions.Section));
        builder.Services.AddSingleton<Agent.Tracing.TraceRetentionService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Agent.Tracing.TraceRetentionService>());

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>();
            using var db = dbFactory.CreateDbContext();
            DatabaseInitializer.InitializeAsync(db).GetAwaiter().GetResult();
        }

        app.UseInstanceHeader();
        app.UseA2ASpecWire();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInstanceHealth();
        if (app.Configuration.GetValue("Auth:EnableDevIssuer", true))
        {
            app.MapDevIssuer();
        }
        app.MapChat();
        app.MapFeedback();
        app.MapAdminIndex();
        app.MapEvalReports();
        app.MapTraces();
        app.MapHistory();
        app.MapTopology();
        app.MapCompliance();
        app.MapA2ASurface();
        app.MapA2AProtocol();
        return app;
    }
}
