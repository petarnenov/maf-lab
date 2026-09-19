namespace Maf.Lab.Domain.Retrieval;

/// <summary>Output of the search_documents tool. Snippets only, never a synthesized answer.</summary>
public sealed record SearchDocumentsResult(
    IReadOnlyList<DocumentSnippet> Results,
    int TotalMatches,
    bool Truncated,
    string? RefineHint);

public sealed record DocumentSnippet(
    string Snippet,
    string SourcePath,
    string SectionPath,
    double Score,
    DateTimeOffset UpdatedAt,
    string DocId);
