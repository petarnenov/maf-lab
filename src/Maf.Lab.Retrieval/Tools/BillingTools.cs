using System.ComponentModel;
using Maf.Lab.Domain.Billing;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Billing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.Retrieval.Tools;

[McpServerToolType]
public sealed class BillingTools(BillingSeedStore store, IPrincipalAccessor principals, ILogger<BillingTools> logger)
{
    public const string GetStatusName = "get_billing_run_status";
    public const string SearchRunsName = "search_billing_runs";

    [McpServerTool(Name = GetStatusName, Title = "Get billing run status", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(BillingRunStatus))]
    [Description(
        "Returns the current status of one billing run by its run id: status, billing period, account count and failure reason if it failed.\n" +
        "Use when: the user names a specific run (e.g. 'status of run 4417', 'why did run 4417 fail') and wants its current state.\n" +
        "Do not use for: how billing works, procedures or definitions — use search_documents. To find runs without an id, use search_billing_runs.")]
    public CallToolResult GetStatus(
        [Description("The billing run id, e.g. '4417'.")] string runId)
    {
        try
        {
            var status = store.GetStatus(principals.Current, runId ?? "");
            return status is null
                ? ToolErrors.Error($"Billing run '{BillingSeedStore.NormalizeRunId(runId ?? "")}' was not found. Check the id or use search_billing_runs to list runs.")
                : SearchDocumentsTool.Structured(status);
        }
        catch (Exception ex)
        {
            logger.LogError("get_billing_run_status failed: {ErrorType}", ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Billing run lookup"));
        }
    }

    [McpServerTool(Name = SearchRunsName, Title = "Search billing runs", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(SearchBillingRunsResult))]
    [Description(
        "Lists billing runs of the caller's firm, optionally filtered by status and billing period, newest first.\n" +
        "Use when: the user wants to find runs, e.g. 'which runs failed last month', 'show pending runs'.\n" +
        "Do not use for: explaining procedures or terms — use search_documents. For one known run id, use get_billing_run_status.")]
    public CallToolResult Search(
        [Description("Optional status filter: pending, running, completed, failed.")] string? status = null,
        [Description("Optional earliest billing period date, ISO yyyy-MM-dd.")] DateOnly? periodFrom = null,
        [Description("Optional latest billing period date, ISO yyyy-MM-dd.")] DateOnly? periodTo = null,
        [Description("Maximum runs to return (1-20, default 10).")] int? maxResults = null)
    {
        try
        {
            var result = store.Search(principals.Current, status, periodFrom, periodTo, Math.Clamp(maxResults ?? 10, 1, 20));
            return SearchDocumentsTool.Structured(result);
        }
        catch (Exception ex)
        {
            logger.LogError("search_billing_runs failed: {ErrorType}", ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Billing run search"));
        }
    }
}
