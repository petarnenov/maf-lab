using System.ComponentModel;
using System.Text.Json.Serialization;
using Maf.Lab.Domain.Code;
using Maf.Lab.Hosting;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.CodeSearch.Tools;

[JsonConverter(typeof(JsonStringEnumConverter<CodeKind>))]
public enum CodeKind
{
    code,
    docs,
}

/// <summary>
/// The codebase's RAG tools. The corpus is the repository, indexed as shared, so every caller sees the same code; the
/// tenant still comes from the principal and never from an argument.
/// </summary>
[McpServerToolType]
public sealed class CodeSearchTools(CodeSearchService service, IPrincipalAccessor principals, ILogger<CodeSearchTools> logger)
{
    public const string SearchDescription =
        "Searches the maf-lab repository — C# and TypeScript source, tests, scripts, OpenSpec specs, DECISIONS.md and docs — " +
        "lexically (exact identifiers and their camelCase parts) and semantically, and returns matching snippets with file path, " +
        "line range and symbol. It never returns a synthesized answer.\n" +
        "Use when: you need to find where something is implemented, how a type or method works, which spec or decision covers a " +
        "behaviour, or the code behind an error message.\n" +
        "Do not use for: questions about fee billing or portfolio data — use search_documents or search_portfolio_documents. " +
        "Who calls a method and which tests cover a file come from the code graph: trace_code_symbol and change_impact.\n" +
        "Pass a natural-language phrase or an identifier, e.g. 'where is the tenant filter applied' or 'TenantScopedSearch'.";

    public const string AskDescription =
        "Answers a question about the maf-lab codebase from the snippets search_codebase would retrieve, citing each claim as " +
        "path:start-end. The answer rests only on the indexed code and docs; when they do not cover the question it says so " +
        "(grounded=false when nothing relevant was found).\n" +
        "Use when: you want an explanation that spans several files, e.g. 'how does a search go from the MCP tool to Qdrant'.\n" +
        "Do not use for: finding a place to read or edit — search_codebase returns the snippets themselves.";

    [McpServerTool(Name = CodeTools.Search, Title = "Search the codebase", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(CodeSearchResult))]
    [Description(SearchDescription)]
    public async Task<CallToolResult> SearchAsync(
        [Description("What to find: a natural-language phrase or an identifier, e.g. 'how chunks are sized for the embedding model'.")]
        string query,
        [Description("Optional: 'code' for source only, 'docs' for Markdown (specs, decisions, READMEs) only.")]
        CodeKind? kind = null,
        [Description("Optional path prefix from the repository root, e.g. 'src/Maf.Lab.Indexing/' or 'web/src/'.")]
        string? pathPrefix = null,
        [Description("Maximum snippets to return (1-10, default 5). Values above 10 are capped.")]
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolErrors.Error("query is required: pass a phrase or an identifier to look for.");
        }
        using var span = LabTelemetry.Source.StartActivity("mcp.tool");
        span?.SetTag("tool.name", CodeTools.Search);
        try
        {
            var result = await service.SearchAsync(principals.Current, query.Trim(), kind?.ToString(), pathPrefix, maxResults, cancellationToken);
            span?.SetTag("retrieval.returned", result.Results.Count);
            return SearchDocumentsTool.Structured(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{Tool} failed: {ErrorType}", CodeTools.Search, ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Codebase search"));
        }
    }

    [McpServerTool(Name = CodeTools.Ask, Title = "Ask about the codebase", ReadOnly = true, Idempotent = false, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(CodebaseAnswer))]
    [Description(AskDescription)]
    public async Task<CallToolResult> AskAsync(
        [Description("The question, in any language, e.g. 'How is the tenant filter applied to hybrid search?'")]
        string question,
        [Description("Optional path prefix from the repository root to answer from, e.g. 'src/Maf.Lab.Api/'.")]
        string? pathPrefix = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return ToolErrors.Error("question is required.");
        }
        using var span = LabTelemetry.Source.StartActivity("mcp.tool");
        span?.SetTag("tool.name", CodeTools.Ask);
        try
        {
            var answer = await service.AskAsync(principals.Current, question.Trim(), pathPrefix, cancellationToken);
            span?.SetTag("retrieval.returned", answer.Sources.Count);
            return SearchDocumentsTool.Structured(answer);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{Tool} failed: {ErrorType}", CodeTools.Ask, ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Codebase question answering"));
        }
    }
}
