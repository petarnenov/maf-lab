using Markdig;
using Markdig.Syntax;

namespace Maf.Lab.Indexing.Chunking;

/// <summary>Splits Markdown by heading hierarchy; each chunk records its heading path ("A > B > C").</summary>
public sealed class MarkdownChunker : IChunker
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();

    public IReadOnlyList<RawChunk> Chunk(string content, string relativePath, int maxChars)
    {
        var document = Markdown.Parse(content, Pipeline);
        var headings = new List<(int Level, string Title)>();
        var chunks = new List<RawChunk>();
        var body = new System.Text.StringBuilder();

        void Flush()
        {
            var sectionPath = string.Join(" > ", headings.Select(h => h.Title));
            foreach (var piece in ChunkText.SplitToFit(body.ToString(), maxChars))
            {
                chunks.Add(new RawChunk(sectionPath, piece));
            }
            body.Clear();
        }

        foreach (var block in document)
        {
            if (block is HeadingBlock heading)
            {
                Flush();
                var title = heading.Inline is null ? "" : string.Concat(heading.Inline.Descendants<Markdig.Syntax.Inlines.LiteralInline>().Select(l => l.Content.ToString())).Trim();
                headings.RemoveAll(h => h.Level >= heading.Level);
                headings.Add((heading.Level, title));
                continue;
            }
            body.Append(content.AsSpan(block.Span.Start, block.Span.Length)).Append("\n\n");
        }
        Flush();
        return chunks;
    }
}
