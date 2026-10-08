using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Retrieval.Tools;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// The domains a question can belong to, each served by its own MCP server with its own documentation search. A turn
/// that calls tools of more than one crosses the domain boundary, and the trace says where (add-portfolio-domain).
/// </summary>
public static class Domains
{
    public const string Billing = "billing";
    public const string Portfolio = "portfolio";
    /// <summary>The lab's own software, served by the codebase server's search (add-codebase-domain).</summary>
    public const string Codebase = "codebase";
    /// <summary>The history of Bulgaria, served by its server's search over a shared corpus (add-bulgarian-history-domain).</summary>
    public const string BulgarianHistory = "bulgarian-history";

    /// <summary>Every domain, in the order the trace lists them.</summary>
    public static readonly IReadOnlyList<string> All = [Billing, Portfolio, Codebase, BulgarianHistory];

    /// <summary>
    /// The domains with a search and no read tools: their search is forced whatever the intent but small talk, since a
    /// question in them has nothing else to be answered from (add-bulgarian-history-domain).
    /// </summary>
    public static readonly IReadOnlySet<string> SearchOnly = new HashSet<string>(StringComparer.Ordinal) { Codebase, BulgarianHistory };

    /// <summary>Each domain's documentation search: the tool a forcing intent calls in that domain.</summary>
    public static readonly IReadOnlyDictionary<string, string> SearchTool = new Dictionary<string, string>
    {
        [Billing] = SearchDocumentsTool.Name,
        [Portfolio] = PortfolioTools.Search,
        [Codebase] = Maf.Lab.Domain.Code.CodeTools.Search,
        [BulgarianHistory] = Maf.Lab.Domain.BulgarianHistory.BulgarianHistoryTools.Search,
    };

    /// <summary>True for a documentation search of any domain: its result carries snippets and sources.</summary>
    public static bool IsSearch(string tool) => SearchTool.Values.Contains(tool);

    /// <summary>
    /// The domain a tool belongs to when no tool set is at hand (a stored envelope): each domain's search, then the read
    /// tools the router knows, and billing — where every tool was before domains existed — for the rest.
    /// </summary>
    public static string OfTool(string tool) =>
        SearchTool.FirstOrDefault(kv => kv.Value == tool).Key
        ?? (Jev.DataToolRouter.ToolDomain.TryGetValue(tool, out var domain) ? domain : null)
        ?? GraphTool.GetValueOrDefault(tool)
        ?? Billing;

    /// <summary>The graph tools (add-neo4j-graph), by the domain whose server offers them.</summary>
    public static readonly IReadOnlyDictionary<string, string> GraphTool = new Dictionary<string, string>
    {
        [Maf.Lab.Domain.Graph.GraphTools.TraceBilling] = Billing,
        [Maf.Lab.Domain.Graph.GraphTools.TraceCodeSymbol] = Codebase,
        [Maf.Lab.Domain.Graph.GraphTools.ChangeImpact] = Codebase,
    };
}
