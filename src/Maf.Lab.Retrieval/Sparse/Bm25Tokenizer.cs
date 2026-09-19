using System.Globalization;
using System.Text;

namespace Maf.Lab.Retrieval.Sparse;

/// <summary>
/// Lowercases, splits on anything that is not a letter or digit, drops English stopwords.
/// Identifiers like "4417" or "fee_schedule" survive (underscore splits into parts, both kept).
/// </summary>
public static class Bm25Tokenizer
{
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
