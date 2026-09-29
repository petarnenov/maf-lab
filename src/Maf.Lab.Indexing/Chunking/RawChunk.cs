namespace Maf.Lab.Indexing.Chunking;

/// <summary>Chunker output before identity and metadata are attached.</summary>
public sealed record RawChunk(string SectionPath, string Text, string? Symbol = null);

public interface IChunker
{
    IReadOnlyList<RawChunk> Chunk(string content, string relativePath, ChunkBudget budget);
}

/// <summary>
/// How large a chunk may be: <see cref="Limit"/> characters, or — with <see cref="Tokens"/> — that many embedding-model
/// tokens as <see cref="TokenEstimator"/> counts them. An int converts to a character budget, which is what the billing
/// and portfolio corpora have always been cut with.
/// </summary>
public readonly record struct ChunkBudget(int Limit, bool Tokens = false)
{
    public int Measure(string text) => Tokens ? TokenEstimator.Estimate(text) : text.Length;

    public bool Fits(string text) => Measure(text) <= Limit;

    public static ChunkBudget OfTokens(int tokens) => new(tokens, Tokens: true);

    public static implicit operator ChunkBudget(int maxChars) => new(maxChars);
}

internal static class ChunkText
{
    /// <summary>Splits oversize text on blank lines, then lines, then hard cuts, keeping pieces within the budget.</summary>
    public static IEnumerable<string> SplitToFit(string text, ChunkBudget budget)
    {
        text = text.Trim();
        if (budget.Fits(text))
        {
            if (text.Length > 0)
            {
                yield return text;
            }
            yield break;
        }

        var current = new System.Text.StringBuilder();
        var size = 0;
        foreach (var unit in Units(text, budget))
        {
            var unitSize = budget.Measure(unit);
            if (current.Length > 0 && size + unitSize + 1 > budget.Limit)
            {
                yield return current.ToString().Trim();
                current.Clear();
                size = 0;
            }
            current.Append(unit).Append('\n');
            size += unitSize + 1;
        }
        if (current.ToString().Trim().Length > 0)
        {
            yield return current.ToString().Trim();
        }
    }

    private static IEnumerable<string> Units(string text, ChunkBudget budget)
    {
        foreach (var paragraph in text.Split("\n\n"))
        {
            if (budget.Fits(paragraph))
            {
                yield return paragraph + "\n";
                continue;
            }
            foreach (var line in paragraph.Split('\n'))
            {
                if (!budget.Tokens)
                {
                    for (var i = 0; i < line.Length; i += budget.Limit)
                    {
                        yield return line.Substring(i, Math.Min(budget.Limit, line.Length - i));
                    }
                    continue;
                }
                foreach (var piece in HardCut(line, budget))
                {
                    yield return piece;
                }
            }
        }
    }

    /// <summary>A line longer than a token budget: the longest prefixes that still fit, one after another.</summary>
    private static IEnumerable<string> HardCut(string line, ChunkBudget budget)
    {
        var rest = line;
        while (rest.Length > 0)
        {
            if (budget.Fits(rest))
            {
                yield return rest;
                yield break;
            }
            // Every token covers at least one character and the estimator never counts a character as more than two,
            // so Limit characters is an upper bound worth starting from; shrink until it fits.
            var take = Math.Min(rest.Length, Math.Max(1, budget.Limit));
            while (take > 1 && !budget.Fits(rest[..take]))
            {
                take = take * 3 / 4;
            }
            if (char.IsHighSurrogate(rest[take - 1]) && take < rest.Length)
            {
                take = take > 1 ? take - 1 : take + 1;
            }
            yield return rest[..take];
            rest = rest[take..];
        }
    }
}
