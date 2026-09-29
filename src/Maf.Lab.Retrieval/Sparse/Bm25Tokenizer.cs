using System.Globalization;
using System.Text;

namespace Maf.Lab.Retrieval.Sparse;

/// <summary>Names of the tokenizers a BM25 vocabulary can be built with; the model records which one it used.</summary>
public static class Bm25Tokenizers
{
    /// <summary>Words: <see cref="Bm25Tokenizer.Tokenize"/>. What every vocabulary without a name was built with.</summary>
    public const string Words = "words";
    /// <summary>Words plus the parts of each identifier: <see cref="Bm25Tokenizer.TokenizeCode"/>.</summary>
    public const string Code = "code";

    public static bool IsKnown(string? name) => name is Words or Code;
}

/// <summary>
/// Lowercases, splits on anything that is not a letter or digit, drops English stopwords.
/// Identifiers like "4417" or "fee_schedule" survive (underscore splits into parts, both kept).
/// </summary>
public static class Bm25Tokenizer
{
    public static IEnumerable<string> Tokenize(string text, string? tokenizer) =>
        tokenizer == Bm25Tokenizers.Code ? TokenizeCode(text) : Tokenize(text);

    /// <summary>
    /// For source code: every word <see cref="Tokenize"/> keeps, followed by the parts of a word written in camel or
    /// Pascal case — "TenantScopedSearch" also yields "tenant", "scoped" and "search", "HTTPClient" yields "http" and
    /// "client". So a question in plain words meets the identifier, and the whole identifier still ranks an exact
    /// name above its parts.
    /// </summary>
    public static IEnumerable<string> TokenizeCode(string text)
    {
        foreach (var word in RawWords(text))
        {
            var lower = word.ToLower(CultureInfo.InvariantCulture);
            if (Keep(lower))
            {
                yield return lower;
            }
            var parts = IdentifierParts(word);
            if (parts.Count < 2)
            {
                continue;
            }
            foreach (var part in parts)
            {
                var p = part.ToLower(CultureInfo.InvariantCulture);
                if (Keep(p))
                {
                    yield return p;
                }
            }
        }
    }

    /// <summary>Runs of letters and digits, case kept, NFKC-normalised as <see cref="Tokenize"/> does.</summary>
    private static IEnumerable<string> RawWords(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                continue;
            }
            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    /// <summary>Splits at lower→upper ("fooBar"), at the last capital of an acronym ("HTTPClient"), and at letter↔digit.</summary>
    internal static List<string> IdentifierParts(string word)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 1; i < word.Length; i++)
        {
            char prev = word[i - 1], cur = word[i];
            var boundary = (char.IsLower(prev) && char.IsUpper(cur))
                || (char.IsUpper(prev) && char.IsUpper(cur) && i + 1 < word.Length && char.IsLower(word[i + 1]))
                || (char.IsLetter(prev) && char.IsDigit(cur))
                || (char.IsDigit(prev) && char.IsLetter(cur));
            if (boundary)
            {
                parts.Add(word[start..i]);
                start = i;
            }
        }
        parts.Add(word[start..]);
        return parts;
    }

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "been", "but", "by", "can", "do", "does", "for", "from",
        "had", "has", "have", "he", "her", "his", "how", "i", "if", "in", "into", "is", "it", "its", "me",
        "my", "no", "not", "of", "on", "or", "our", "she", "so", "such", "that", "the", "their", "them",
        "then", "there", "these", "they", "this", "to", "was", "we", "were", "what", "when", "where", "which",
        "while", "who", "why", "will", "with", "would", "you", "your",
    };

    public static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
                continue;
            }
            if (sb.Length > 0)
            {
                var token = sb.ToString();
                sb.Clear();
                if (Keep(token))
                {
                    yield return token;
                }
            }
        }
        if (sb.Length > 0 && Keep(sb.ToString()))
        {
            yield return sb.ToString();
        }
    }

    private static bool Keep(string token) =>
        !Stopwords.Contains(token) && (token.Length > 1 || char.IsDigit(token[0])) && token.Length <= 64;
}
