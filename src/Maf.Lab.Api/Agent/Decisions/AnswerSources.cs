using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Chat;

namespace Maf.Lab.Api.Agent.Decisions;

/// <summary>
/// The answer as the answer check reads it (fit-answer-checks-to-code-questions, D6): the hyphens a model writes inside
/// paths and line ranges become <c>-</c>, and no-break spaces become spaces. Code, not Jev — a regex does it exactly.
/// Used for the answer in the Jev state and for the cited-source match; never for the question, which the user wrote,
/// and never for the answer shown or stored.
/// </summary>
public static partial class AnswerText
{
    public static string Normalise(string answer)
    {
        if (string.IsNullOrEmpty(answer))
        {
            return answer;
        }
        var text = answer.Replace('‐', '-').Replace('‑', '-').Replace('‒', '-')
            .Replace(' ', ' ').Replace(' ', ' ');
        // An en dash only between two digits is a range ("153–195"); between words it is punctuation and stays.
        return EnDashBetweenDigits().Replace(text, "-");
    }

    [GeneratedRegex(@"(?<=\d)–(?=\d)")]
    private static partial Regex EnDashBetweenDigits();
}

/// <summary>
/// One thing the model read, as the answer check sends it: a search item or a whole result, keyed so a repeat is sent
/// once, with the names an answer cites it by, and the domain whose tool produced it.
/// </summary>
/// <param name="Key"><c>code:path:start-end</c>, <c>doc:docId›section</c>, or <c>text:</c> and a hash of the text.</param>
/// <param name="Text">What is sent: <c>path:start-end › symbol: code</c>, <c>docId › section: text</c>, or <c>tool: result</c>.</param>
/// <param name="CitationNames">For code, the paths and file names an answer would cite it by; empty otherwise.</param>
/// <param name="Domain">The tool's domain; <see cref="Domains.None"/> for a tool no domain in use names.</param>
public sealed partial record ReadItem(string Key, string Text, IReadOnlyList<string> CitationNames, string Domain)
{
    /// <summary>What the answer check reads in place of an item the content guard withheld: the place, never the text.</summary>
    public const string WithheldMark = "(withheld by the content guard)";

    /// <summary>A whole result, a fixed text or a previous envelope: keyed by its text.</summary>
    public static ReadItem Whole(string tool, string text, string domain) =>
        new($"text:{Hash(text)}", $"{tool}: {text}", [], domain);

    /// <summary>
    /// What the model read from one tool result, after the guard: a search's items one by one — a withheld stub as its
    /// place marked withheld — and any other result (an empty search included: its hint is what the model was told) whole.
    /// </summary>
    public static IReadOnlyList<ReadItem> FromResult(string tool, string domain, string payload, JsonElement? structured, bool isError)
    {
        if (!isError && Domains.IsSearch(tool) && structured is { ValueKind: JsonValueKind.Object } s && s.TryGetProperty("results", out var results)
            && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
        {
            return [.. results.EnumerateArray().Select(r => FromSearchItem(r, domain))];
        }
        return [Whole(tool, payload, domain)];
    }

    /// <summary>One search item: a codebase snippet by its place, a document excerpt by its id and section.</summary>
    public static ReadItem FromSearchItem(JsonElement r, string domain)
    {
        var withheld = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("withheld", out var w) && w.ValueKind == JsonValueKind.True;
        var item = SourceRef.FromSearchItem(r);
        if (item.Kind == SourceRef.CodeKind)
        {
            var place = item.StartLine is { } start && item.EndLine is { } end ? $"{item.DocId}:{start}-{end}" : item.DocId;
            var names = CodeNames(item.DocId);
            return withheld
                ? new ReadItem($"withheld:code:{place}", $"{place}: {WithheldMark}", names, domain)
                : new ReadItem($"code:{place}", $"{item.SectionPath}: {item.Snippet}", names, domain);
        }
        return withheld
            ? new ReadItem($"withheld:doc:{item.DocId}", $"{item.DocId}: {WithheldMark}", [], domain)
            : new ReadItem($"doc:{item.DocId}›{item.SectionPath}", $"{item.DocId} › {item.SectionPath}: {item.Snippet}", [], domain);
    }

    /// <summary>
    /// A previous turn's data envelope, as its stored trace holds it: keyed by its text, its domain from the
    /// envelope's <c>tool="…"</c>, and — for a codebase search — every path in its JSON, so a follow-up that cites one is
    /// ordered first.
    /// </summary>
    public static ReadItem FromEnvelope(string envelope)
    {
        var tool = EnvelopeTool().Match(envelope) is { Success: true } m ? m.Groups[1].Value : "";
        var domain = Domains.OfTool(tool) ?? Domains.None;
        var names = new List<string>();
        if (Domains.IsCodeSearch(tool))
        {
            foreach (Match path in PathField().Matches(envelope))
            {
                names.AddRange(CodeNames(Regex.Unescape(path.Groups[1].Value)));
            }
        }
        return new ReadItem($"text:{Hash(envelope)}", envelope, [.. names.Distinct(StringComparer.Ordinal)], domain);
    }

    /// <summary>A path and its file name: what an answer cites a file by.</summary>
    private static List<string> CodeNames(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }
        var file = path[(path.LastIndexOf('/') + 1)..];
        return file.Length > 0 && file != path ? [path, file] : [path];
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    [GeneratedRegex("^<tool_data tool=\"([^\"]*)\">")]
    private static partial Regex EnvelopeTool();

    [GeneratedRegex("\"path\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex PathField();
}

/// <summary>
/// What the answer check sends, chosen by code (D4): this turn's sources and the previous turn's, each sent once, the
/// ones the answer cites first, every item whole — or <see cref="OverCap"/> when one that must go does not fit.
/// </summary>
/// <param name="Sources">This turn's items, cited first, in the order they are sent.</param>
/// <param name="Previous">The previous turn's items that are sent, cited first.</param>
/// <param name="Duplicates">Items dropped because an item with the same key came first.</param>
/// <param name="Chars">Characters of the items above.</param>
/// <param name="OverCap">This turn's items, or a cited previous one, do not fit: the check sends nothing.</param>
/// <param name="Codebase">Whether any item, current or previous, came from a codebase search: the context is code's.</param>
public sealed record SourceSelection(IReadOnlyList<ReadItem> Sources, IReadOnlyList<ReadItem> Previous, int Duplicates, int Chars,
    bool OverCap, bool Codebase);

public static class AnswerSources
{
    /// <summary>
    /// Drops repeated keys, orders cited-current, other-current, cited-previous, other-previous, and takes whole items:
    /// every current item and every cited previous one must fit under <paramref name="maxChars"/>, uncited previous
    /// items fill what is left. An item is cited when the normalised answer contains one of its names — a path
    /// ordinally, a bare file name ignoring case.
    /// </summary>
    public static SourceSelection Select(IReadOnlyList<ReadItem> current, IReadOnlyList<ReadItem> previous, string answer, int maxChars)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        List<ReadItem> Unique(IEnumerable<ReadItem> items)
        {
            var kept = new List<ReadItem>();
            foreach (var item in items)
            {
                if (seen.Add(item.Key))
                {
                    kept.Add(item);
                }
                else
                {
                    duplicates++;
                }
            }
            return kept;
        }
        var now = Unique(current);
        var before = Unique(previous);
        var normalised = AnswerText.Normalise(answer ?? "");
        bool Cited(ReadItem item) => item.CitationNames.Any(n => n.Length > 0
            && normalised.Contains(n, n.Contains('/') ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

        var sources = now.Where(Cited).Concat(now.Where(i => !Cited(i))).ToList();
        var citedBefore = before.Where(Cited).ToList();
        static bool Code(IEnumerable<ReadItem> items) => items.Any(i => Domains.IsCode(i.Domain));
        var left = Math.Max(0, maxChars);
        var chars = sources.Sum(i => i.Text.Length) + citedBefore.Sum(i => i.Text.Length);
        if (chars > left)
        {
            return new SourceSelection(sources, citedBefore, duplicates, chars, OverCap: true, Code(sources.Concat(citedBefore)));
        }
        var sent = new List<ReadItem>(citedBefore);
        foreach (var item in before.Where(i => !Cited(i)))
        {
            // An uncited previous item may be left out to fit; the ones after it are still tried.
            if (chars + item.Text.Length <= left)
            {
                sent.Add(item);
                chars += item.Text.Length;
            }
        }
        return new SourceSelection(sources, sent, duplicates, chars, OverCap: false, Code(sources.Concat(sent)));
    }
}
