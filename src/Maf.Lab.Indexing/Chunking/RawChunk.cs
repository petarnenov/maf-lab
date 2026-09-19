namespace Maf.Lab.Indexing.Chunking;

/// <summary>Chunker output before identity and metadata are attached.</summary>
public sealed record RawChunk(string SectionPath, string Text, string? Symbol = null);

public interface IChunker
{
    IReadOnlyList<RawChunk> Chunk(string content, string relativePath, int maxChars);
}

internal static class ChunkText
{
    /// <summary>Splits oversize text on blank lines, then lines, then hard cuts, keeping pieces under maxChars.</summary>
    public static IEnumerable<string> SplitToFit(string text, int maxChars)
    {
        text = text.Trim();
        if (text.Length <= maxChars)
        {
            if (text.Length > 0)
            {
                yield return text;
            }
            yield break;
        }

        var current = new System.Text.StringBuilder();
        foreach (var unit in Units(text, maxChars))
        {
            if (current.Length > 0 && current.Length + unit.Length + 1 > maxChars)
            {
                yield return current.ToString().Trim();
                current.Clear();
            }
            current.Append(unit).Append('\n');
        }
        if (current.ToString().Trim().Length > 0)
        {
            yield return current.ToString().Trim();
        }
    }

    private static IEnumerable<string> Units(string text, int maxChars)
    {
        foreach (var paragraph in text.Split("\n\n"))
        {
            if (paragraph.Length <= maxChars)
            {
                yield return paragraph + "\n";
                continue;
            }
            foreach (var line in paragraph.Split('\n'))
            {
                for (var i = 0; i < line.Length; i += maxChars)
                {
                    yield return line.Substring(i, Math.Min(maxChars, line.Length - i));
                }
            }
        }
    }
}
