namespace Maf.Lab.Domain.Code;

/// <summary>The codebase's own Qdrant collection and BM25 vocabulary: never billing's or portfolio's.</summary>
public static class CodeCollections
{
    public const string Chunks = "maf_code_chunks";
    public const string Meta = "maf_code_meta";
}

/// <summary>Wire names of the tools the codebase MCP server exposes.</summary>
public static class CodeTools
{
    public const string Search = "search_codebase";
    public const string Ask = "ask_codebase";
}

/// <summary>What a snippet is: source code, or documentation written in Markdown (specs, decisions, READMEs).</summary>
public static class CodeKinds
{
    public const string Code = "code";
    public const string Docs = "docs";
}

/// <summary>One place in the repository that matches a query.</summary>
/// <param name="Path">Path from the repository root, e.g. "src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs".</param>
/// <param name="StartLine">1-based first line of the snippet in that file; null when the indexer could not place it.</param>
/// <param name="Symbol">The type or member the snippet belongs to ("TenantScopedSearch.QueryAsync"); null for docs and file-level code.</param>
/// <param name="Section">Where in the file: the symbol path for code, the heading path for Markdown.</param>
/// <param name="Kind">"code" or "docs".</param>
/// <param name="Language">From the file extension: csharp, typescript, python, sql, shell, markdown …</param>
public sealed record CodeSnippet(
    string Path,
    int? StartLine,
    int? EndLine,
    string? Symbol,
    string Section,
    string Kind,
    string Language,
    double Score,
    string Snippet);

/// <summary>Result of search_codebase. Snippets only, never a synthesized answer.</summary>
public sealed record CodeSearchResult(IReadOnlyList<CodeSnippet> Results, int TotalMatches, bool Truncated, string? RefineHint);

/// <summary>A place the answer of ask_codebase rests on.</summary>
public sealed record CodeSource(string Path, int? StartLine, int? EndLine, string? Symbol);

/// <summary>
/// Result of ask_codebase: an answer written only from the snippets retrieved for the question, and those snippets'
/// places. <paramref name="Grounded"/> is false when nothing relevant was found, and the answer then says so.
/// </summary>
public sealed record CodebaseAnswer(string Answer, bool Grounded, IReadOnlyList<CodeSource> Sources);
