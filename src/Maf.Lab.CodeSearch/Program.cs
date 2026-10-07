using Maf.Lab.CodeSearch.Tools;
using Maf.Lab.Hosting;
using Maf.Lab.Retrieval;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Maf.Lab.CodeSearch;

/// <summary>
/// The codebase's MCP server (protocol 2026-07-28, Streamable HTTP, stateless): the repository itself — source, tests,
/// specs and decisions — indexed by structure into its own collection, searched lexically (BM25 over identifiers split
/// into words) and semantically (embeddinggemma) through the one tenant-scoped query method. Read-only.
/// </summary>
public partial class Program
{
    public const string ServerName = "maf-lab-code";

    public static void Main(string[] args) => BuildApp(args).Run();

    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile("codesearch.json", optional: true).AddEnvironmentVariables();
        // Pinned after every other source: a shared Qdrant__Collection meant for billing must not point this server at it.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Qdrant:Collection"] = builder.Configuration["CodeSearch:Collection"] ?? Domain.Code.CodeCollections.Chunks,
            ["Qdrant:MetaCollection"] = builder.Configuration["CodeSearch:MetaCollection"] ?? Domain.Code.CodeCollections.Meta,
        });
        configure?.Invoke(builder);

        builder.AddLabTelemetry("maf-lab-mcp-code");

        builder.Services.AddMafRetrievalCore(builder.Configuration);
        builder.Services.AddDevJwtAuthentication(builder.Configuration);
        // The code graph (callers, callees, change impact). Like the billing server, it starts without the graph store.
        builder.Services.AddGraphStore(builder.Configuration);
        // An out-of-range depth pin stops the server instead of being clamped (add-graph-depth-eval).
        builder.Services.AddOptions<CodeSearchOptions>()
            .Bind(builder.Configuration.GetSection(CodeSearchOptions.Section))
            .Validate(o => CodeSearchOptions.IsValidPin(o.GraphDepthPin), CodeSearchOptions.PinRangeMessage)
            .ValidateOnStart();
        builder.Services.AddSingleton<ICodeRanker, DocumentSearchRanker>();
        builder.Services.AddSingleton<CodeSearchService>();
        builder.Services.AddHostedService<BootstrapService>();
        builder.Services
            .AddMcpServer(o => o.ServerInfo = new() { Name = ServerName, Version = "1.0.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<CodeSearchTools>()
            .WithTools<CodeGraphTools>();
        // A pinned server must say what it does: the trace reaches through the pin, and the model no longer picks a depth.
        builder.Services.AddOptions<McpServerOptions>().PostConfigure<IOptions<CodeSearchOptions>>((o, code) =>
        {
            if (code.Value.GraphDepthPin is { } depth)
            {
                CodeGraphTools.PublishPin(o, depth);
            }
        });

        builder.Services.AddInstanceHealth();
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

public sealed class CodeSearchOptions
{
    public const string Section = "CodeSearch";

    public string Collection { get; set; } = Domain.Code.CodeCollections.Chunks;
    public string MetaCollection { get; set; } = Domain.Code.CodeCollections.Meta;
    /// <summary>Characters of each snippet search_codebase returns; a chunk is at most ~1k tokens, so most arrive whole.</summary>
    public int SnippetMaxChars { get; set; } = 1200;
    /// <summary>How many snippets ask_codebase gives the chat model to answer from.</summary>
    public int AnswerSnippets { get; set; } = 8;
    /// <summary>Candidates ranked before a path filter is applied, so a narrow path still finds its matches.</summary>
    public int PathFilterCandidates { get; set; } = 60;
    /// <summary>
    /// Measurement only (the graph-depth eval): fixes the depth of trace_code_symbol and change_impact, 1 to
    /// <see cref="CallTrace.MaxDepth"/>. Unset in every deployed configuration; then the tools behave as published.
    /// </summary>
    public int? GraphDepthPin { get; set; }

    public static readonly string PinRangeMessage = $"{Section}:{nameof(GraphDepthPin)} must be between 1 and {CallTrace.MaxDepth}, or unset.";

    public static bool IsValidPin(int? pin) => pin is null or (>= 1 and <= CallTrace.MaxDepth);
}
