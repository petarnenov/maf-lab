using System.Text;

namespace Maf.Lab.Eval.Judging;

/// <summary>
/// Cuts an answer into the sentences Jev grades one by one (adopt-meai-evaluation, design D2). Code, not a model: the
/// same answer always gives the same sentences. A fenced code block is one sentence, a table row is one, a list item
/// is cut like prose without its marker, and prose is cut after <c>. ! ? …</c> followed by whitespace and an upper-case
/// letter (Latin or Cyrillic), a digit or an opening mark. Text inside back-ticks is never cut, so paths, line ranges
/// and qualified names stay whole; a decimal has no space after its point and is never cut either.
/// </summary>
public static class AnswerSentences
{
    public static IReadOnlyList<string> Split(string answer)
    {
        var sentences = new List<string>();
        var fence = new StringBuilder();
        var inFence = false;
        foreach (var raw in answer.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fence.AppendLine(raw);
                if (inFence)
                {
                    Add(sentences, fence.ToString());
                    fence.Clear();
                }
                inFence = !inFence;
                continue;
            }
            if (inFence)
            {
                fence.AppendLine(raw);
                continue;
            }
            if (line.Length == 0 || line.StartsWith('#') || IsTableRule(line))
            {
                continue;
            }
            if (line.StartsWith('|'))
            {
                Add(sentences, line);
                continue;
            }
            foreach (var sentence in CutProse(StripListMarker(line)))
            {
                Add(sentences, sentence);
            }
        }
        if (inFence)
        {
            // An unclosed fence still says something; grade what it holds rather than drop it.
            Add(sentences, fence.ToString());
        }
        return sentences;
    }

    private static IEnumerable<string> CutProse(string line)
    {
        var start = 0;
        var inCode = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '`')
            {
                inCode = !inCode;
                continue;
            }
            if (inCode || c is not ('.' or '!' or '?' or '…'))
            {
                continue;
            }
            // Closing marks after the end belong to the sentence that ends: "stale.”" or "it.**".
            var end = i + 1;
            while (end < line.Length && line[end] is '"' or '”' or '’' or ')' or '*' or '_')
            {
                end++;
            }
            var next = end;
            if (next >= line.Length || !char.IsWhiteSpace(line[next]))
            {
                continue;
            }
            while (next < line.Length && char.IsWhiteSpace(line[next]))
            {
                next++;
            }
            if (next < line.Length && StartsSentence(line[next]))
            {
                yield return line[start..end];
                start = next;
                i = next - 1;
            }
        }
        if (start < line.Length)
        {
            yield return line[start..];
        }
    }

    private static bool StartsSentence(char c) =>
        char.IsUpper(c) || char.IsDigit(c) || c is '`' or '*' or '"' or '“' or '„' or '(' or '[' or '$';

    private static string StripListMarker(string line)
    {
        if (line.Length > 1 && line[0] is '-' or '*' or '•' or '+' && line[1] == ' ')
        {
            return line[2..].TrimStart();
        }
        var digits = 0;
        while (digits < line.Length && char.IsDigit(line[digits]))
        {
            digits++;
        }
        return digits is > 0 and < 4 && digits + 1 < line.Length && line[digits] is '.' or ')' && line[digits + 1] == ' '
            ? line[(digits + 2)..].TrimStart()
            : line;
    }

    private static bool IsTableRule(string line) =>
        line.StartsWith('|') && line.All(c => c is '|' or '-' or ':' or ' ');

    private static void Add(List<string> sentences, string piece)
    {
        var text = piece.Trim();
        if (text.Any(char.IsLetterOrDigit))
        {
            sentences.Add(text);
        }
    }
}
