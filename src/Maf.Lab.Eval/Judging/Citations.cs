using System.Text.RegularExpressions;
using Maf.Lab.Api.Agent.Jev;

namespace Maf.Lab.Eval.Judging;

/// <summary>One place a sentence cites, where it stands in the sentence, and whether a source the turn read holds it.</summary>
public sealed record CitedPlace(string Text, int Index, bool Found);

/// <summary>
/// Checks the places an answer cites against what the turn read, in code (adopt-meai-evaluation, DECISIONS.md §78).
/// Whether "Section 3 → Step 2" or <c>src/X.cs:121-140</c> is a place a source holds is a lookup of numbers, which
/// jev-usage §5 gives to code: asked of Jev, correct citations scored 0.46–0.49 and invented ones 0.58–0.60. The
/// formats are the ones the answers are told to use and the corpus is written in — the system prompt's
/// <c>path:start-end</c> for code, and the procedures' <c>Section N: … &gt; Step M</c> — not a list of past failures.
/// </summary>
public static partial class Citations
{
    /// <summary>The marker a found place is replaced with in the text Jev reads, so it judges only what the sentence says.</summary>
    public const string Verified = "[cited place verified]";

    /// <summary>
    /// Every place the sentence cites: a code range (a path with <c>:start-end</c>, or bare ranges following a path, which
    /// cite that path), or a procedure section and step. A code range is found when a codebase source of the same path
    /// covers it; a section and step when a document source's section path names both.
    /// </summary>
    public static IReadOnlyList<CitedPlace> Find(string sentence, IReadOnlyList<ReadItem> sources)
    {
        var code = sources.Select(CodeRange).OfType<(string Path, int Start, int End)>().ToList();
        var sections = sources.Select(SectionPath).OfType<string>().ToList();
        var places = new List<CitedPlace>();

        string? path = null;
        var pathEnd = -1;
        foreach (Match m in CodeOrRange().Matches(sentence))
        {
            if (m.Groups["path"].Success)
            {
                path = m.Groups["path"].Value;
                pathEnd = m.Index + m.Groups["path"].Length;
                if (!m.Groups["start"].Success)
                {
                    continue;
                }
            }
            if (path is null || m.Index < pathEnd && !m.Groups["path"].Success)
            {
                continue;
            }
            var start = int.Parse(m.Groups["start"].Value);
            var end = m.Groups["end"].Success ? int.Parse(m.Groups["end"].Value) : start;
            var cited = path;
            var found = code.Any(c => SamePath(c.Path, cited) && c.Start <= start && end <= c.End);
            places.Add(new CitedPlace(m.Groups["path"].Success ? m.Value : m.Groups["range"].Value, m.Groups["path"].Success ? m.Index : m.Groups["range"].Index, found));
        }

        foreach (Match m in SectionStep().Matches(sentence))
        {
            var section = m.Groups["section"].Value;
            var step = m.Groups["step"].Value;
            var found = sections.Any(s => Regex.IsMatch(s, $@"\bSection {section}\b") && Regex.IsMatch(s, $@"\bStep {step}\b"));
            places.Add(new CitedPlace(m.Value, m.Index, found));
        }
        return [.. places.OrderBy(p => p.Index)];
    }

    /// <summary>The sentence as Jev reads it: every found place replaced with <see cref="Verified"/>.</summary>
    public static string Mask(string sentence, IReadOnlyList<CitedPlace> places)
    {
        var text = sentence;
        foreach (var place in places.Where(p => p.Found).OrderByDescending(p => p.Index))
        {
            text = text[..place.Index] + Verified + text[(place.Index + place.Text.Length)..];
        }
        return text;
    }

    private static (string Path, int Start, int End)? CodeRange(ReadItem item)
    {
        // A code item's key is code:path:start-end (ReadItem.FromSearchItem).
        var m = CodeKey().Match(item.Key);
        return m.Success ? (m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    }

    private static string? SectionPath(ReadItem item)
    {
        // A document item's key is doc:docId›sectionPath.
        var i = item.Key.IndexOf('›');
        return item.Key.StartsWith("doc:", StringComparison.Ordinal) && i > 0 ? item.Key[(i + 1)..] : null;
    }

    /// <summary>A cited path matches a source's when one ends with the other: answers often drop a leading folder.</summary>
    private static bool SamePath(string source, string cited) =>
        source.EndsWith(cited, StringComparison.Ordinal) || cited.EndsWith(source, StringComparison.Ordinal);

    [GeneratedRegex(@"^code:(.+):(\d+)-(\d+)$")]
    private static partial Regex CodeKey();

    // A path with an extension, optionally followed by :start[-end] or " lines start-end"; or a bare range after one.
    [GeneratedRegex(@"(?<path>(?:[\w.-]+/)+[\w.-]+\.[A-Za-z]{1,5})(?:(?::|,?\s+lines?\s+)(?<start>\d+)(?:\s*[-–]\s*(?<end>\d+))?)?|(?<range>(?<start>\d+)\s*[-–]\s*(?<end>\d+))")]
    private static partial Regex CodeOrRange();

    // "Section 3 → Step 2", "Section 3: Assign a fee schedule > Step 1".
    [GeneratedRegex(@"Section\s+(?<section>\d+)(?::[^>→)\n]*)?\s*(?:>|→)\s*Step\s+(?<step>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SectionStep();
}
