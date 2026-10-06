namespace Maf.Lab.Api.Agent;

/// <summary>
/// The domains a question can belong to, each served by its own MCP server with its own documentation search. A turn
/// that calls tools of more than one crosses the domain boundary, and the trace says where (add-portfolio-domain).
/// Read from the domain catalogue (introduce-plugins decision 6): the core names no domain and treats none as first.
/// </summary>
public static class Domains
{
    /// <summary>What a tool of no domain in use is recorded under: it is not attributed to any domain.</summary>
    public const string None = "none";

    /// <summary>Every domain in use, in the order the trace lists them.</summary>
    public static IReadOnlyList<string> All => DomainCatalogue.Current.Ids;

    /// <summary>Each domain's documentation search: the tool a forcing intent calls in that domain.</summary>
    public static IReadOnlyDictionary<string, string> SearchTool => DomainCatalogue.Current.SearchTools;

    /// <summary>True for a documentation search of any domain: its result carries snippets and sources.</summary>
    public static bool IsSearch(string tool) => SearchTool.Values.Contains(tool);

    /// <summary>
    /// The domain a tool belongs to when no tool set is at hand (a stored envelope): the domain whose descriptor names it,
    /// or null when no domain in use does — an unknown tool is attributed to no domain (task 4.6).
    /// </summary>
    public static string? OfTool(string tool) => DomainCatalogue.Current.OfTool(tool);

    /// <summary>The graph tools (add-neo4j-graph), by the domain whose server offers them.</summary>
    public static IReadOnlyDictionary<string, string> GraphTool => DomainCatalogue.Current.GraphTools;

    /// <summary>Whether a domain's search results are screened as code (its guard context).</summary>
    public static bool IsCode(string? domain) =>
        domain is not null && DomainCatalogue.Current.Get(domain)?.GuardContext == Maf.Lab.Plugins.Abstractions.GuardContexts.Code;

    /// <summary>Whether a tool is the documentation search of a domain whose results are code.</summary>
    public static bool IsCodeSearch(string tool) => IsSearch(tool) && IsCode(OfTool(tool));

    /// <summary>
    /// The content battery a tool's result is screened with, as the trace names it: the code battery for a code domain's
    /// search, the documents battery for every other result (a graph tool's included) and for words no tool returned.
    /// </summary>
    public static string GuardContextOf(string? tool) =>
        tool is not null && IsCodeSearch(tool) ? Maf.Lab.Plugins.Abstractions.GuardContexts.Code : Maf.Lab.Plugins.Abstractions.GuardContexts.Documents;
}
