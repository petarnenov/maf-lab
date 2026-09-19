using Maf.Lab.Api.Admin;
using Maf.Lab.Api.Agent;
using Maf.Lab.Api.Endpoints;
using Maf.Lab.Api.Feedback;
using Maf.Lab.Api.Storage;
using Maf.Lab.Indexing;
using Maf.Lab.Retrieval.Auth;
using Microsoft.EntityFrameworkCore;

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

        builder.Services.AddDbContextFactory<MafDbContext>(o =>
            o.UseSqlite(builder.Configuration["Storage:ConnectionString"] ?? "Data Source=maf-lab.db"));

        builder.Services.AddSingleton<SystemPrompt>();
        builder.Services.AddSingleton<TokenCounter>();
        builder.Services.AddSingleton<ToolAudit>();
        builder.Services.AddHttpClient("mcp");
        builder.Services.AddSingleton<IToolSource, McpToolSource>();
        builder.Services.AddSingleton<ConversationService>();
        builder.Services.AddScoped<ChatTurnRunner>();
        builder.Services.AddSingleton<DatasetWriter>();
        builder.Services.AddSingleton<AdminJobRunner>();

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MafDbContext>>();
            using var db = dbFactory.CreateDbContext();
            db.Database.EnsureCreated();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        if (app.Configuration.GetValue("Auth:EnableDevIssuer", true))
        {
            app.MapDevIssuer();
        }
        app.MapChat();
        app.MapFeedback();
        app.MapAdminIndex();
        app.MapEvalReports();
        return app;
    }
}
