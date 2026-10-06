using System.ComponentModel;
using Maf.Lab.Domain.Graph;
using Maf.Lab.Hosting;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Graph;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.Retrieval.Tools;

// names a domain until the extract-evals-plugin follow-up moves it (introduce-plugins 8.1)
// The billing host, which stays here until the eval stops hosting it in-process.
/// <summary>
/// The billing graph: how accounts, households, fee schedules and documents connect, within the caller's firm and the
/// shared corpus. Read-only; the tenant comes from the principal, the query from a fixed template.
/// </summary>
[McpServerToolType]
public sealed class BillingGraphTools(IGraphReader graph, IPrincipalAccessor principals, ILogger<BillingGraphTools> logger)
{
    public const string Description =
        "Shows how billing entities connect, from the billing graph: for an account, its firm, household, the household's other " +
        "accounts and the firm's latest billing runs; for a household, its accounts; for a fee schedule code, the household " +
        "profiles and notes that refer to it; and for each, the documents that mention it, with the document id to read them by.\n" +
        "Use when: the user asks which accounts share a household, which households or documents use a fee schedule " +
        "(e.g. 'which households are on NW-INST-2026-083'), or what documents concern an account or a household.\n" +
        "Do not use for: how billing works, procedures or definitions — use search_documents. For the current state of a run, " +
        "use get_billing_run_status or search_billing_runs.\n" +
        "Pass one id: an account id (A-1042), a household id (HH-RIDGELINE) or a fee schedule code (NW-INST-2026-083).";

    /// <summary>The same answer for an unknown id and another firm's id, so neither reveals the other.</summary>
    internal const string NotFound =
        "No account, household or fee schedule with that id was found. Check the id, or use search_documents to find it.";

    [McpServerTool(Name = GraphTools.TraceBilling, Title = "Trace billing relationships", ReadOnly = true, Idempotent = true, Destructive = false,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(BillingRelationships))]
    [Description(Description)]
    public async Task<CallToolResult> TraceAsync(
        [Description("An account id (e.g. 'A-1042'), a household id (e.g. 'HH-RIDGELINE') or a fee schedule code (e.g. 'NW-INST-2026-083').")]
        string entityId,
        [Description("How far to follow links: 1 for direct links only, 2 (default) to include what those link to. At most 2.")]
        int? depth = null,
        CancellationToken cancellationToken = default,
        RequestContext<CallToolRequestParams>? context = null)
    {
        // Asked for diagnostics: the read path records each read for the turn trace's graph event, structure only.
        using var reads = GraphReadLog.BeginIf(SearchDocumentsTool.TraceRequested(context));
        return GraphReadLog.Attach(await TraceCoreAsync(entityId, depth, cancellationToken), reads);
    }

    private async Task<CallToolResult> TraceCoreAsync(string? entityId, int? depth, CancellationToken cancellationToken)
    {
        var id = entityId?.Trim() ?? "";
        if (id.Length is 0 or > 64)
        {
            return ToolErrors.Error("entityId is required: pass an account id, a household id or a fee schedule code.");
        }
        var hops = depth ?? BillingNeighbourhood.MaxDepth;
        if (hops is < 1 or > BillingNeighbourhood.MaxDepth)
        {
            return ToolErrors.Error($"depth must be between 1 and {BillingNeighbourhood.MaxDepth}.");
        }

        using var span = LabTelemetry.Source.StartActivity("mcp.tool");
        span?.SetTag("tool.name", GraphTools.TraceBilling);
        try
        {
            var principal = principals.Current;
            var neighbourhood = await graph.ReadAsync(principal, new BillingNeighbourhood(id, hops), cancellationToken);
            if (neighbourhood.Start is not { } start)
            {
                span?.SetTag("graph.found", false);
                return ToolErrors.Error(NotFound);
            }
            var runs = start.Kind == BillingEntityKinds.Account
                ? await graph.ReadAsync(principal, new FirmRuns(start.Id), cancellationToken)
                : [];
            span?.SetTag("graph.found", true);
            span?.SetTag("graph.related", neighbourhood.Related.Count + neighbourhood.Documents.Count);
            return SearchDocumentsTool.Structured(new BillingRelationships(
                start, hops, neighbourhood.Related, neighbourhood.Documents, runs, neighbourhood.Truncated));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("{Tool} failed: {ErrorType}", GraphTools.TraceBilling, ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Graph lookup"));
        }
    }
}
