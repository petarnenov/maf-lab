using Maf.Lab.Api.Agent.AGUI;
using Maf.Lab.A2A;
using Maf.Lab.Api.Admin;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Agent.Decisions;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Plugins;
using Maf.Lab.Api.Storage;
using Maf.Lab.Retrieval;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Maf.Lab.Hosting;

namespace Maf.Lab.Api;

/// <summary>Agent host: chat over AG-UI, history, feedback and contributed plugin routes.</summary>
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
        builder.RequireSharedState<Maf.Lab.Domain.SharedState.IBreakGlassPermissionStore>();

        // The api's drain on a graceful stop: Docker's stop grace (40 s) exceeds it, so a stopping replica finishes its
        // runs or records them cancelled before it can be killed (introduce-plugins decision 2).
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        // The installed plugins: read at run time, composed here (introduce-plugins decision 5).
        builder.AddMafPlugins(plugins);
        builder.Services.AddHttpClient("plugins");
        builder.Services.Configure<Plugins.PluginHealthOptions>(builder.Configuration.GetSection(Plugins.PluginHealthOptions.Section));
        builder.Services.TryAddSingleton<Maf.Lab.Domain.Services.IServiceResolver, Maf.Lab.Hosting.Services.DnsServiceResolver>();
        builder.Services.AddSingleton<Plugins.PluginHealth>();
        builder.Services.AddHttpClient(Plugins.PluginHealth.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(Plugins.PluginHealth.CreateHandler);
        builder.Services.Configure<Plugins.PluginTokenExchangeOptions>(builder.Configuration.GetSection(Plugins.PluginTokenExchangeOptions.Section));
        builder.Services.AddHttpClient(Plugins.PluginTokenExchange.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.TryAddSingleton<Maf.Lab.Plugins.Abstractions.IPluginTokens, Plugins.PluginTokenExchange>();
        builder.Services.AddSingleton<Plugins.PluginAccessChanges>();
        builder.Services.AddSingleton<Plugins.IPluginAccessChanges>(sp => sp.GetRequiredService<Plugins.PluginAccessChanges>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Plugins.PluginAccessChanges>());
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IPluginEntitlements, Plugins.PluginEntitlements>();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IPluginAccess, Plugins.PluginAccess>();
        // The domains in use, as data (introduce-plugins decision 6): every installed plugin's, rebuilt when the installed
        // set changes.
        builder.Services.AddSingleton(sp => new DomainCatalogue(
            sp.GetServices<Maf.Lab.Plugins.Abstractions.IDomainBehaviour>(), sp.GetService<Plugins.PluginCatalogue>(),
            sp.GetService<Microsoft.Extensions.Options.IOptions<Plugins.PluginOptions>>()));

        // Retrieval and model providers for chat and shared feedback state; indexing is a plugin's to register.
        builder.Services.AddMafRetrievalCore(builder.Configuration);
        builder.Services.AddLabAuthentication(builder.Configuration);
        builder.Services.AddAuthorizationBuilder();
        builder.Services.Configure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(AuthPolicies.Add);
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.Section));

        // Unpooled: a pooled handle can come back with a live statement or transaction (efcore#38574, #38854), and the
        // next open fails with "database is locked". Microsoft's stated workaround; revisit when the fix ships in 10.x.
        builder.Services.AddDbContextFactory<MafDbContext>(o => o
            .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
                builder.Configuration["Storage:ConnectionString"] ?? "Data Source=maf-lab.db") { Pooling = false }.ToString())
            .AddInterceptors(new SqlitePragmaInterceptor())
            // One model per set of plugin tables, so two hosts with different plugins in one process never share one.
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, Storage.PluginModelCacheKeyFactory>());

        builder.Services.AddSingleton<SystemPrompt>();
        builder.Services.AddSingleton<TokenCounter>();
        builder.Services.AddSingleton<ToolAudit>();
        builder.Services.AddSingleton<Compliance.OperatorSessionAudit>();
        builder.Services.AddSingleton<Compliance.ContentAccessGrants>();
        builder.Services.AddHostedService<Compliance.ContentAccessGrantReconciler>();
        builder.Services.AddScoped<Maf.Lab.Domain.Tenancy.IOperatorContentAccess, Compliance.CoreOperatorContentAccess>();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.ISystemAudit, Agent.CoreSystemAudit>();
        builder.Services.AddHttpClient("mcp");
        builder.Services.AddSingleton<IToolSource, McpToolSource>();
        builder.Services.AddSingleton<ConversationService>();
        builder.Services.AddSingleton<DataLifecycle.CoreDataLifecycle>();
        builder.Services.AddSingleton(sp => new Plugins.NamedDataLifecycle(null,
            sp.GetRequiredService<DataLifecycle.CoreDataLifecycle>().CreateDataLifecycle(sp)));
        // The installed providers (the decision engine), then the core's callers over the port they implement.
        Maf.Lab.Plugins.Abstractions.ProviderHost.AddInstalledProviders(builder.Services, builder.Configuration);
        builder.Services.AddDecisionCallers(builder.Configuration);
        // Writes a person confirms (generalize-write-confirmation): the core's half of the seam and the ports a plugin's
        // flow reaches it through; the flows themselves are the installed plugins' (PluginHost).
        builder.Services.AddScoped<Agent.Writes.WriteTurnContext>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.IWriteAudit, Agent.Writes.CoreWriteAudit>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.IConsultationScreening, Agent.Writes.CoreConsultationScreening>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.IWriteTraceStep, Agent.Writes.CoreWriteTraceStep>();
        builder.Services.AddScoped<Agent.Writes.WriteFlows>();
        builder.Services.AddScoped<Agent.Writes.WriteConfirmations>();
        builder.Services.AddScoped<Agent.ConfirmationService>();
        // The turn observers of the installed plugins, as one (decision 7); none of the core's own.
        builder.Services.AddSingleton<Agent.Tracing.TurnObservers>();
        // The one store, as a plugin reaches its own tables (EF's factory over DbContext).
        builder.Services.AddSingleton<IDbContextFactory<DbContext>, Storage.PluginDbContextFactory>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.ITurnAccess, Storage.TurnAccess>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.IConversationStore, Storage.ConversationStore>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.ITurnRecords, Storage.TurnRecords>();
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.IFeedbackReviewStore, Feedback.CoreFeedbackReviewStore>();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IGuardSettings, Agent.CoreGuardSettings>();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IIntentSettings, Agent.CoreIntentSettings>();
        builder.Services.AddScoped<ChatTurnRunner>();
        builder.Services.AddScoped<Agent.RunRejoin>();
        // Every agent reaches a browser through the Agent Framework's own AG-UI server (agui-protocol-only).
        builder.Services.AddAGUIHosting();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IAgentRunInput, CoreAgentRunInput>();
        builder.Services.AddSingleton<Agent.ChatAgent>();
        builder.Services.TryAddSingleton<Maf.Lab.Plugins.Abstractions.IAppendEvalDataset, Maf.Lab.Plugins.Abstractions.NoEvalDataset>();
        builder.Services.Configure<AdminJobOptions>(builder.Configuration.GetSection("AdminJobs"));
        builder.Services.AddSingleton<AdminJobRunner>();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IInstallationJobs, CoreInstallationJobs>();
        // The job store as a plugin reaches it, scoped to the request's principal (extract-index-admin-plugin).
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.IAdminJobs, CoreAdminJobs>();
        // A turn's link to its trace: none until a plugin that keeps the traces registers its own (Null Object).
        builder.Services.TryAddSingleton<Maf.Lab.Plugins.Abstractions.ITraceLink, Maf.Lab.Plugins.Abstractions.NoTraceLink>();
        // The assistant's agent card and handler are the a2a plugin's (extract-a2a); it reaches the core through these.
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IAssistantAnswer, Agent.AssistantAnswer>();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IDomainToolCall, Agent.DomainToolCall>();
        builder.Services.AddSingleton<Maf.Lab.Plugins.Abstractions.IActivityAudit, Agent.ActivityAudit>();
        // The reviewer is the compliance plugin's (extract-compliance-plugin); without it, a flow that asks is told none
        // can be reached (a Null Object), and the tool that requires one is not offered at all.
        builder.Services.TryAddSingleton<Maf.Lab.Plugins.Abstractions.IReviewerConsultation, Agent.Writes.NoReviewer>();
        // The audit record as a screen reads it; the screen is the compliance plugin's.
        builder.Services.AddScoped<Maf.Lab.Plugins.Abstractions.IAuditTrail, Compliance.CoreAuditTrail>();
        builder.Services.Configure<Storage.MessageRetentionOptions>(
            builder.Configuration.GetSection(Storage.MessageRetentionOptions.Section));
        builder.Services.AddSingleton<Storage.MessageRetentionService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Storage.MessageRetentionService>());


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
        // Record entry before dispatch, independently of dashboard plugins and even if a route later refuses access.
        // A storage failure prevents dispatch: an operator must never enter without the tenant-visible record.
        var operatorAudit = app.Services.GetRequiredService<Compliance.OperatorSessionAudit>();
        var identity = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Maf.Lab.Domain.Configuration.AuthOptions>>().Value;
        var companyIdentity = !string.IsNullOrWhiteSpace(identity.Authority);
        var operatorIssuer = companyIdentity ? new Uri(identity.Authority!).AbsoluteUri.TrimEnd('/') : identity.Issuer;
        app.Use(async (context, next) =>
        {
            if (Maf.Lab.Domain.Tenancy.PrincipalClaims.TryCreate(context.User, out var principal) && principal.IsPlatformAdmin)
            {
                if (!Compliance.OperatorSessionAudit.TryReadSession(context.User, principal, companyIdentity, out var sessionId))
                {
                    await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.ChallengeAsync(context,
                        Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
                    return;
                }
                await operatorAudit.RecordAsync(principal, operatorIssuer, sessionId, context.RequestAborted);
            }
            await next(context);
        });
        // Entitlements are read only after the token is validated. One scope feeds every request reader.
        var domains = app.Services.GetRequiredService<DomainCatalogue>();
        var access = app.Services.GetRequiredService<Maf.Lab.Plugins.Abstractions.IPluginAccess>();
        app.Use(async (context, next) =>
        {
            if (Maf.Lab.Domain.Tenancy.PrincipalClaims.TryCreate(context.User, out var principal))
            {
                var snapshot = await access.For(principal, context.RequestAborted);
                using var permissionScope = Maf.Lab.Plugins.Abstractions.PluginAccessContext.Use(principal, snapshot);
                using var domainScope = DomainCatalogue.Use(domains.For(snapshot));
                await next(context);
            }
            else
            {
                using var domainScope = DomainCatalogue.Use(DomainCatalogue.Empty);
                await next(context);
            }
        });
        app.UseAuthorization();
        app.UseMiddleware<Agent.AGUI.RunTap>();
        app.MapInstanceHealth();
        var core = app.MapGroup("").AddEndpointFilter<Compliance.OperatorContentFilter>();
        core.MapChat();
        core.MapChatAgent();
        core.MapFeedback();
        core.MapHistory();
        core.MapPlugins();
        core.MapPluginAdministration();
        core.MapContentAccess();
        app.MapMafPlugins();
        return app;
    }
}
