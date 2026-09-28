using Maf.Lab.Hosting;
using Maf.Lab.Portfolio.Store;
using Maf.Lab.Portfolio.Tools;
using Maf.Lab.Retrieval;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.Portfolio;

/// <summary>
/// The portfolio domain's MCP server (protocol 2026-07-28, Streamable HTTP, stateless): its own documentation, searched
/// through its own collection, and read tools over household portfolios. Read-only, so it keeps no shared state.
/// </summary>
public partial class Program
{
    public const string ServerName = "maf-lab-portfolio";

    /// <summary>This domain's own collection and BM25 vocabulary: never billing's, whatever else configures the core.</summary>
    public const string Collection = "maf_portfolio_chunks";
    public const string MetaCollection = "maf_portfolio_meta";

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

        // The retrieval core, pointed by portfolio.json at this domain's collection and vocabulary.
        builder.Services.AddMafRetrievalCore(builder.Configuration);
        builder.Services.AddDevJwtAuthentication(builder.Configuration);
        builder.Services.AddSingleton<PortfolioStore>();
        builder.Services.AddHostedService<BootstrapService>();
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new() { Name = ServerName, Version = "1.0.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<PortfolioSearchTool>()
            .WithTools<HouseholdTools>();

        var app = builder.Build();
        app.Services.GetRequiredService<Retrieval.Jev.JevCredential>();
        app.UseInstanceHeader();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapInstanceHealth();
        app.MapMcp("/mcp").RequireAuthorization();
        return app;
    }
}
