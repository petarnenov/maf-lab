using Maf.Lab.BulgarianHistory.Tools;
using Maf.Lab.Hosting;
using Maf.Lab.Retrieval;
using Maf.Lab.Retrieval.Auth;

namespace Maf.Lab.BulgarianHistory;

/// <summary>
/// The Bulgarian history domain's MCP server (protocol 2026-07-28, Streamable HTTP, stateless): a shared-only corpus
/// searched through its own collection by one read-only tool. It keeps no shared state.
/// </summary>
public partial class Program
{
    public const string ServerName = "maf-lab-bulgarian-history";

    /// <summary>This domain's own collection and BM25 vocabulary: never billing's, whatever else configures the core.</summary>
    public const string Collection = Domain.BulgarianHistory.BulgarianHistoryCollections.Chunks;
    public const string MetaCollection = Domain.BulgarianHistory.BulgarianHistoryCollections.Meta;

    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile("bulgarian-history.json", optional: true).AddEnvironmentVariables();
        // Pinned after every other source: a shared Qdrant__Collection meant for billing must not point this server at it.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Qdrant:Collection"] = builder.Configuration["BulgarianHistory:Collection"] ?? Collection,
            ["Qdrant:MetaCollection"] = builder.Configuration["BulgarianHistory:MetaCollection"] ?? MetaCollection,
            // The corpus is written in Bulgarian. The core's translator rewrites any non-Latin query into the corpus
            // language, which for the other servers is English; here a Cyrillic question is already in the corpus's
            // language, and a translation would turn it into words the Bulgarian BM25 vocabulary has never seen. The
            // embedding model is multilingual, so a Latin-script (English) question still finds its passages untranslated.
            ["Retrieval:CorpusLanguage"] = builder.Configuration["BulgarianHistory:CorpusLanguage"] ?? "bg",
            ["Retrieval:NormalizeQueryLanguage"] = builder.Configuration["BulgarianHistory:NormalizeQueryLanguage"] ?? "false",
        });
        configure?.Invoke(builder);

        builder.AddLabTelemetry("maf-lab-mcp-bulgarian-history");

        // The retrieval core, pointed by bulgarian-history.json at this domain's collection and vocabulary.
        builder.Services.AddMafRetrievalCore(builder.Configuration);
        builder.Services.AddDevJwtAuthentication(builder.Configuration);
        builder.Services.AddHostedService<BootstrapService>();
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new() { Name = ServerName, Version = "1.0.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<BulgarianHistorySearchTool>();

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
