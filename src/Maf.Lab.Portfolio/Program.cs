using Maf.Lab.Hosting;
using Maf.Lab.Portfolio.Store;
using Maf.Lab.Portfolio.Tools;
using Maf.Lab.Retrieval;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.Portfolio;

// names a domain until the extract-evals-plugin follow-up moves it (introduce-plugins 8.1)
// The portfolio host, which stays here until the eval stops hosting it in-process.
/// <summary>
/// The portfolio domain's MCP server (protocol 2026-07-28, Streamable HTTP, stateless): its own documentation, searched
/// through its own collection, and read tools over household portfolios. Read-only, so it keeps no shared state.
/// </summary>
public partial class Program
{
    public const string ServerName = "maf-lab-portfolio";

    /// <summary>This domain's own collection and BM25 vocabulary: never billing's, whatever else configures the core.</summary>
    public const string Collection = Domain.Portfolio.PortfolioCollections.Chunks;
    public const string MetaCollection = Domain.Portfolio.PortfolioCollections.Meta;

    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile("portfolio.json", optional: true).AddEnvironmentVariables();
        // Pinned after every other source: a shared Qdrant__Collection meant for billing must not point this server at it.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Qdrant:Collection"] = builder.Configuration["Portfolio:Collection"] ?? Collection,
            ["Qdrant:MetaCollection"] = builder.Configuration["Portfolio:MetaCollection"] ?? MetaCollection,
        });
        configure?.Invoke(builder);

        builder.AddLabTelemetry("maf-lab-mcp-portfolio");
        builder.AddSharedState();
        builder.RequireSharedState<Maf.Lab.Domain.SharedState.IBreakGlassPermissionStore>();

        // The retrieval core, pointed by portfolio.json at this domain's collection and vocabulary.
        builder.Services.AddMafRetrievalCore(builder.Configuration);
        // The installed providers: the decision engine the relevance judge asks.
        Maf.Lab.Plugins.Abstractions.ProviderHost.AddInstalledProviders(builder.Services, builder.Configuration);
        builder.Services.AddLabAuthentication(builder.Configuration, "portfolio");
        builder.Services.AddLabMcpAuthentication(builder.Configuration);
        builder.Services.AddSingleton<PortfolioStore>();
        builder.Services.AddHostedService<BootstrapService>();
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new() { Name = ServerName, Version = "1.0.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithOperatorContentAccess()
            .WithTools<PortfolioSearchTool>()
            .WithTools<HouseholdTools>();

        builder.Services.AddInstanceHealth();
        var app = builder.Build();
        app.UseLabMcpForwarding();
        app.UseInstanceHeader();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInstanceHealth();
        app.MapMcp("/mcp").RequireAuthorization();
        return app;
    }
}
