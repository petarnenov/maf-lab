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

    /// <summary>Every domain, in the order the trace lists them.</summary>
    public static readonly IReadOnlyList<string> All = [Billing, Portfolio, Codebase];

    /// <summary>Each domain's documentation search: the tool a forcing intent calls in that domain.</summary>
    public static readonly IReadOnlyDictionary<string, string> SearchTool = new Dictionary<string, string>
    {
        [Billing] = SearchDocumentsTool.Name,
        [Portfolio] = PortfolioTools.Search,
        [Codebase] = Maf.Lab.Domain.Code.CodeTools.Search,
    };

    /// <summary>True for a documentation search of any domain: its result carries snippets and sources.</summary>
    public static bool IsSearch(string tool) => SearchTool.Values.Contains(tool);
}
