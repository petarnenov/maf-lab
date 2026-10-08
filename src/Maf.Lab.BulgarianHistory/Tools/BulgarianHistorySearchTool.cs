using System.ComponentModel;
using Maf.Lab.Domain.BulgarianHistory;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Hosting;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.BulgarianHistory.Tools;

/// <summary>
/// The Bulgarian history domain's RAG tool. The same search as <c>search_documents</c> — hybrid, relevance-gated, reranked,
/// through the one tenant-scoped query method — over this server's own collection, which is configuration
/// (Qdrant:Collection), never a parameter.
/// </summary>
[McpServerToolType]
public sealed class BulgarianHistorySearchTool(DocumentSearchService search, IPrincipalAccessor principals, ILogger<BulgarianHistorySearchTool> logger)
{
    public const string Name = BulgarianHistoryTools.Search;

    public const string ToolDescription =
        "Searches the shared documentation on the history of Bulgaria — states, rulers, wars, uprisings, the liberation and " +
        "unification, culture and religion — and returns matching snippets with their source and section. It never returns a " +
        "synthesized answer.\n" +
        "Use when: the user asks who, when, what happened or why about Bulgaria's past.\n" +
        "Do not use for: an account's AUM or market value over quarters — use get_aum_history; billing runs that ran before — " +
        "use search_billing_runs; the lab's code and its changes — use search_codebase; billing or portfolio procedures — " +
        "use search_documents or search_portfolio_documents.\n" +
        "Pass a natural-language phrase as query.";

    [McpServerTool(Name = Name, Title = "Search Bulgarian history", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(SearchDocumentsResult))]
    [Description(ToolDescription)]
    public async Task<CallToolResult> SearchAsync(
        [Description("Natural-language phrase describing what to look up, e.g. 'who baptized the Bulgarians'. Not an identifier.")]
        string query,
        [Description("Optional filter on source types: docs.")]
        DocSourceType[]? sourceTypes = null,
        [Description("Maximum snippets to return (1-10, default 5). Values above 10 are capped.")]
        int? maxResults = null,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolErrors.Error("query is required: pass a natural-language phrase describing what to look up.");
        }
        using var span = LabTelemetry.Source.StartActivity("mcp.tool");
        span?.SetTag("tool.name", Name);
        try
        {
            var types = sourceTypes?.Select(t => t.ToString()).Distinct().ToList();
            var diagnostics = SearchDocumentsTool.TraceRequested(context) ? new SearchDiagnostics() : null;
            var outcome = await search.SearchAsync(principals.Current, query.Trim(), types, maxResults, settings: null, cancellationToken, diagnostics);
            var result = SearchDocumentsTool.Structured(outcome.Result);
            if (diagnostics is not null)
            {
                result.Meta = new System.Text.Json.Nodes.JsonObject
                {
                    [SearchDocumentsTool.TraceFlag] = diagnostics.ToJson(InstanceIdentity.Name),
                    [SearchDocumentsTool.InstanceKey] = InstanceIdentity.Name,
                };
            }
            // As search_documents does: a judged search says what Jev decided, so its Jev request is never invisible.
            if (outcome.Relevance is { } relevance)
            {
                result.Meta ??= [];
                result.Meta[SearchDocumentsTool.RelevanceKey] = relevance;
            }
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{Tool} failed: {ErrorType}", Name, ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Bulgarian history document search"));
        }
    }
}
