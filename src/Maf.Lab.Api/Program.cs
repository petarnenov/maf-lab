using Maf.Lab.Api.Admin;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Feedback;
using Maf.Lab.Api.Storage;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

using Maf.Lab.Retrieval.Hosting;

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
        builder.Services.AddScoped<ChatTurnRunner>();
        builder.Services.AddSingleton<DatasetWriter>();
        builder.Services.Configure<AdminJobOptions>(builder.Configuration.GetSection("AdminJobs"));
        builder.Services.AddSingleton<AdminJobRunner>();
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
        return app;
    }
}
