using System.ComponentModel;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Hosting;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Search;
using Maf.Lab.Retrieval.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.Portfolio.Tools;

/// <summary>
/// The portfolio domain's RAG tool. The same search as <c>search_documents</c> — hybrid, relevance-gated, reranked,
/// through the one tenant-scoped query method — over this server's own collection, which is configuration
/// (Qdrant:Collection), never a parameter.
/// </summary>
[McpServerToolType]
public sealed class PortfolioSearchTool(DocumentSearchService search, IPrincipalAccessor principals, ILogger<PortfolioSearchTool> logger)
{
    public const string Name = PortfolioTools.Search;

    public const string ToolDescription =
        "Searches the portfolio documentation visible to the caller — model portfolios, target weights, drift and tolerance bands, " +
        "rebalancing policy, quarter-end valuation, cash, held-away assets and performance reporting — and returns matching snippets " +
        "with their source and section. It never returns a synthesized answer.\n" +
        "Use when: the user asks how or why something works on the investment side, e.g. what triggers a rebalance, how quarter-end " +
        "AUM is struck, what a model portfolio holds, or how performance is reported.\n" +
        "Do not use for: billing procedures, fee schedules or billing runs — use search_documents. For one account's holdings use " +
        "get_household_portfolio; for its quarter-end AUM use get_aum_history.\n" +
        "Pass a natural-language phrase as query, not a bare identifier.";

    [McpServerTool(Name = Name, Title = "Search portfolio documentation", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(SearchDocumentsResult))]
    [Description(ToolDescription)]
    public async Task<CallToolResult> SearchAsync(
        [Description("Natural-language phrase describing what to look up, e.g. 'what drift triggers a rebalance'. Not an identifier.")]
        string query,
        [Description("Optional filter on source types: docs, procedures.")]
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
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{Tool} failed: {ErrorType}", Name, ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Portfolio document search"));
        }
    }
}
