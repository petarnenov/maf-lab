using System.Text;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Indexing.Corpus;

namespace Maf.Lab.Indexing.Chunking;

/// <summary>A chunk with its stable identity, ready for enrichment and encoding.</summary>
public sealed record PreparedChunk(SourceDocument Document, string ChunkId, string SectionPath, string? Symbol, string Text)
{
    /// <summary>Text fed to BM25 (context sentences only enrich the dense embedding; see DECISIONS.md).</summary>
    public string SparseText => $"{SectionPath}\n{Text}";

    public string DenseText(string? context) => context is null ? SparseText : $"{context}\n{SectionPath}\n{Text}";
}

public static partial class ChunkBuilder
{
    private static readonly MarkdownChunker Markdown = new();
    private static readonly ProcedureChunker Procedure = new();
    private static readonly CodeChunker Code = new();

    public static IChunker ChunkerFor(string sourceType) => sourceType switch
    {
        SourceType.Docs => Markdown,
        SourceType.Procedures => Procedure,
        SourceType.Code => Code,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType)),
    };

    /// <summary>
    /// chunk_id = "{doc_id}#{section-slug}" with "-2", "-3" … for repeated sections, so ids are stable across runs
    /// and readable in eval datasets.
    /// </summary>
    public static IReadOnlyList<PreparedChunk> Build(SourceDocument doc, int maxChars)
    {
        var raw = ChunkerFor(doc.SourceType).Chunk(doc.Content, doc.RelativePath, maxChars);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<PreparedChunk>(raw.Count);
        foreach (var chunk in raw)
        {
            var slug = Slug(chunk.Symbol ?? LastSection(chunk.SectionPath));
            if (slug.Length == 0)
            {
                slug = "root";
            }
            var n = seen[slug] = seen.GetValueOrDefault(slug) + 1;
            var id = n == 1 ? $"{doc.DocId}#{slug}" : $"{doc.DocId}#{slug}-{n}";
            result.Add(new PreparedChunk(doc, id, chunk.SectionPath, chunk.Symbol, chunk.Text));
        }
        return result;
    }

    private static string LastSection(string sectionPath)
    {
        var parts = sectionPath.Split(" > ");
        return parts.Length switch
        {
            0 => "",
            1 => parts[0],
            _ => $"{parts[^2]}-{parts[^1]}",
        };
    }

    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        }
        return Dashes().Replace(sb.ToString(), "-").Trim('-') is var s && s.Length > 60 ? s[..60].TrimEnd('-') : Dashes().Replace(sb.ToString(), "-").Trim('-');
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex Dashes();
}
