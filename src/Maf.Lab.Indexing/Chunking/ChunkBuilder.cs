using System.Text;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Retrieval;
using Maf.Lab.Indexing.Corpus;

namespace Maf.Lab.Indexing.Chunking;

/// <summary>A chunk with its stable identity, ready for enrichment and encoding.</summary>
/// <param name="StartLine">1-based first line of the chunk in its file; null when the text could not be placed.</param>
public sealed record PreparedChunk(SourceDocument Document, string ChunkId, string SectionPath, string? Symbol, string Text,
    int? StartLine = null, int? EndLine = null)
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
    private static readonly CodeChunker StructuralCode = new(structural: true);

    public static IChunker ChunkerFor(string sourceType, bool fromRepository = false) => sourceType switch
    {
        SourceType.Docs => Markdown,
        SourceType.Procedures => Procedure,
        SourceType.Code => fromRepository ? StructuralCode : Code,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceType)),
    };

    /// <summary>
    /// chunk_id = "{doc_id}#{section-slug}" with "-2", "-3" … for repeated sections, so ids are stable across runs
    /// and readable in eval datasets.
    /// </summary>
    /// <param name="maxInputTokens">
    /// The most tokens the embedding model reads of one chunk's text, section path included — its context window less
    /// whatever the encoder puts in front (the document prefix, a context sentence). A chunk over it is split again
    /// however it was cut, so none reaches the model longer than the model can read. Null enforces no ceiling.
    /// </param>
    public static IReadOnlyList<PreparedChunk> Build(SourceDocument doc, ChunkBudget budget, int? maxInputTokens = null)
    {
        var raw = ChunkerFor(doc.SourceType, doc.FromRepository).Chunk(doc.Content, doc.RelativePath, budget);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<PreparedChunk>(raw.Count);
        var locator = new LineLocator(doc.Content);
        foreach (var chunk in raw)
        {
            var sectionPath = doc.FromRepository ? Qualified(doc, chunk.SectionPath) : chunk.SectionPath;
            foreach (var text in WithinCeiling(sectionPath, chunk.Text, maxInputTokens))
            {
                var slug = Slug(chunk.Symbol ?? LastSection(chunk.SectionPath));
                if (slug.Length == 0)
                {
                    slug = "root";
                }
                var n = seen[slug] = seen.GetValueOrDefault(slug) + 1;
                var id = n == 1 ? $"{doc.DocId}#{slug}" : $"{doc.DocId}#{slug}-{n}";
                var (start, end) = locator.Locate(text);
                result.Add(new PreparedChunk(doc, id, sectionPath, chunk.Symbol, text, start, end));
            }
        }
        return result;
    }

    /// <summary>The text itself when it fits under the ceiling with its section path; otherwise its token-sized pieces.</summary>
    private static IEnumerable<string> WithinCeiling(string sectionPath, string text, int? maxInputTokens)
    {
        if (maxInputTokens is not { } ceiling || TokenEstimator.Estimate($"{sectionPath}\n{text}") <= ceiling)
        {
            return [text];
        }
        var room = Math.Max(16, ceiling - TokenEstimator.Estimate(sectionPath + "\n"));
        return ChunkText.SplitToFit(text, ChunkBudget.OfTokens(room));
    }

    /// <summary>
    /// A repository file's section path starts with its path from the root: "src/…/TenantScopedSearch.cs > QueryAsync"
    /// for code (whose chunker names only the file), "DECISIONS.md > Heading" for Markdown (whose chunker names none).
    /// </summary>
    private static string Qualified(SourceDocument doc, string sectionPath)
    {
        var fileName = Path.GetFileName(doc.RelativePath);
        if (sectionPath == fileName)
        {
            return doc.RelativePath;
        }
        if (sectionPath.StartsWith(fileName + " > ", StringComparison.Ordinal))
        {
            return doc.RelativePath + sectionPath[fileName.Length..];
        }
        return sectionPath.Length == 0 ? doc.RelativePath : $"{doc.RelativePath} > {sectionPath}";
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

    /// <summary>
    /// Places chunk texts back in their file. Chunks come in file order, so each search starts where the last chunk
    /// began; a chunker that re-joined its text (Markdown blocks) is placed by its first line instead.
    /// </summary>
    private sealed class LineLocator(string content)
    {
        private readonly string _content = content.Replace("\r\n", "\n");
        private int _cursor;

        public (int? Start, int? End) Locate(string text)
        {
            var at = Find(text) ?? Find(text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "");
            if (at is not { } offset)
            {
                return (null, null);
            }
            _cursor = offset;
            var start = 1 + Count(_content.AsSpan(0, offset));
            return (start, start + Count(text.AsSpan()));
        }

        private int? Find(string needle)
        {
            if (needle.Length == 0)
            {
                return null;
            }
            var at = _content.IndexOf(needle, _cursor, StringComparison.Ordinal);
            if (at < 0)
            {
                at = _content.IndexOf(needle, StringComparison.Ordinal);
            }
            return at < 0 ? null : at;
        }

        private static int Count(ReadOnlySpan<char> span) => span.Count('\n');
    }
}
