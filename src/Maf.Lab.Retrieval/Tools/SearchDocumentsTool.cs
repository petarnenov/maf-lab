using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Retrieval.Auth;
using Maf.Lab.Retrieval.Search;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Maf.Lab.Retrieval.Tools;

[JsonConverter(typeof(JsonStringEnumConverter<DocSourceType>))]
public enum DocSourceType
{
    docs,
    procedures,
    code,
}

[McpServerToolType]
public sealed class SearchDocumentsTool(DocumentSearchService search, IPrincipalAccessor principals, ILogger<SearchDocumentsTool> logger)
{
    public const string Name = "search_documents";

    public const string ToolDescription =
        "Searches the platform documentation, billing procedures and code snippets visible to the caller and returns matching " +
        "snippets with their source and section. It never returns a synthesized answer.\n" +
        "Use when: the user asks how or why something works, what the procedure is (e.g. what to do when a fee schedule is missing), " +
        "or asks to explain a term, policy, configuration or code behaviour.\n" +
        "Do not use for: current data such as the status of a billing run, which runs failed, account details or fee amounts. " +
        "For a specific run's status use get_billing_run_status; to find or list billing runs use search_billing_runs.\n" +
        "Pass a natural-language phrase as query, not a bare identifier.";

    [McpServerTool(Name = Name, Title = "Search documentation", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(SearchDocumentsResult))]
    [Description(ToolDescription)]
    public async Task<CallToolResult> SearchAsync(
        [Description("Natural-language phrase describing what to look up, e.g. 'procedure when a fee schedule is missing'. Not an identifier.")]
        string query,
        [Description("Optional filter on source types: docs, procedures, code.")]
        DocSourceType[]? sourceTypes = null,
        [Description("Maximum snippets to return (1-10, default 5). Values above 10 are capped.")]
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolErrors.Error("query is required: pass a natural-language phrase describing what to look up.");
        }
        try
        {
            var types = sourceTypes?.Select(t => t.ToString()).Distinct().ToList();
            var outcome = await search.SearchAsync(principals.Current, query.Trim(), types, maxResults, settings: null, cancellationToken);
            return Structured(outcome.Result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError("search_documents failed: {ErrorType}", ex.GetType().Name);
            return ToolErrors.Error(ToolErrors.ForException(ex, "Document search"));
        }
    }

    internal static CallToolResult Structured<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, McpJson.Options);
        return new CallToolResult
        {
            StructuredContent = element,
            Content = [new TextContentBlock { Text = element.GetRawText() }],
        };
    }
}

internal static class McpJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
