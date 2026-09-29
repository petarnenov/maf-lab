namespace Maf.Lab.Indexing.Chunking;

/// <summary>
/// An upper bound on how many tokens the embedding model reads for a text, without a round trip to it. Calibrated
/// against embeddinggemma's own count (Ollama's prompt_eval_count) on 160 random windows of this repository — C#,
/// TypeScript, Markdown, SQL, shell, CSS, YAML — where it over-counted every one: by 1.13× at the least, 1.39× typically
/// (add-codebase-search). Over-counting only makes chunks smaller than they had to be; the indexer's refusal to let
/// Ollama cut input is what catches a text it under-counts.
/// </summary>
public static class TokenEstimator
{
    public static int Estimate(string text)
    {
        var tokens = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsAsciiLetter(c))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiLetter(text[i])) i++;
                tokens += (i - start + 3) / 4;
                continue;
            }
            if (c > 127 && char.IsLetter(c))
            {
                // Cyrillic runs measured at ~3.2 characters a token; two keeps a margin for less common scripts.
                var start = i;
                while (i < text.Length && text[i] > 127 && char.IsLetter(text[i])) i++;
                tokens += (i - start + 1) / 2;
                continue;
            }
            if (c is ' ' or '\t')
            {
                // A single space is part of the next token; a run of indentation is one token of its own.
                var start = i;
                while (i < text.Length && text[i] is ' ' or '\t') i++;
                tokens += i - start == 1 && c == ' ' ? 0 : 1;
                continue;
            }
            // Digits, punctuation and newlines one each; any other non-ASCII character (symbols, emoji halves) two,
            // since a character outside the vocabulary falls back to its UTF-8 bytes.
            tokens += c > 127 ? 2 : 1;
            i++;
        }
        return tokens;
    }
}
