using System.Text.RegularExpressions;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.Agent;

/// <summary>
/// Versioned system prompt loaded from Prompts/. Changing it requires an eval run (see README). A core template
/// (<c>core.v*</c>) holds what every domain shares — citations, refusals, injection defense, confirmation of writes — and
/// is assembled with the prompt fragments of the domains in use (introduce-plugins 5g, task 4.7): Template Method, the
/// fragments filling the template's named slots. A whole prompt (<c>system.v*</c>) is used as written.
/// </summary>
public sealed partial class SystemPrompt
{
    /// <summary>
    /// The prompt in use unless <c>Agent:SystemPrompt</c> names another. v6 (introduce-plugins) is v5 split into the core
    /// template and one fragment per domain; <c>Agent:SystemPrompt=system.v5</c> rolls back to the whole prompt.
    /// </summary>
    public const string DefaultVersion = "core.v6";

    private readonly string _template;
    private readonly bool _assembled;
    private readonly object _gate = new();
    private (IReadOnlyList<DomainDescriptor> Domains, string Text)? _cached;

    public SystemPrompt(IConfiguration configuration)
    {
        Version = configuration["Agent:SystemPrompt"] ?? DefaultVersion;
        var path = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{Version}.md");
        _template = File.ReadAllText(path);
        _assembled = Slot().IsMatch(_template);
    }

    public string Version { get; }

    /// <summary>The prompt for the domains in use, rebuilt when that set changes.</summary>
    public string Text
    {
        get
        {
            if (!_assembled)
            {
                return _template;
            }
            var domains = DomainCatalogue.Current.All;
            lock (_gate)
            {
                if (_cached is { } c && ReferenceEquals(c.Domains, domains))
                {
                    return c.Text;
                }
                var text = Assemble(_template, domains);
                _cached = (domains, text);
                return text;
            }
        }
    }

    /// <summary>The template with each slot filled from the domains' fragments, in the domains' order.</summary>
    public static string Assemble(string template, IReadOnlyList<DomainDescriptor> domains)
    {
        var fragments = domains.Select(d => Fragment.Parse(d.PromptFragment)).ToList();
        string Parts(string name, string separator) =>
            string.Join(separator, fragments.Select(f => f.Get(name)).Where(p => p.Length > 0));
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["summaries"] = List(fragments.Select(f => f.Get("summary"))),
            ["scope"] = List(fragments.Select(f => f.Get("scope"))),
            ["tools"] = Parts("tools", "\n\n"),
            ["examples"] = Parts("examples", "\n"),
            // A crossing between domains is said only when more than one is in use.
            ["crossing"] = fragments.Count(f => f.Get("summary").Length > 0) > 1 && Parts("crossing", " ") is { Length: > 0 } crossing ? " " + crossing : "",
            ["sections"] = Parts("section", "\n\n") is { Length: > 0 } sections ? "\n" + sections + "\n" : "",
            ["rules"] = Parts("rules", "\n"),
        };
        var text = Slot().Replace(template, m => slots.GetValueOrDefault(m.Groups[1].Value, ""));
        // A slot no domain fills leaves no blank line behind.
        return BlankLines().Replace(text, "\n\n");
    }

    /// <summary>"a", "a and b", "a, b and c".</summary>
    private static string List(IEnumerable<string> items)
    {
        var list = items.Where(i => i.Length > 0).ToList();
        return list.Count <= 1 ? string.Concat(list) : $"{string.Join(", ", list[..^1])} and {list[^1]}";
    }

    /// <summary>A domain's prompt fragment: Markdown split by <c>&lt;!-- name --&gt;</c> markers into named parts.</summary>
    private sealed class Fragment(IReadOnlyDictionary<string, string> parts)
    {
        public string Get(string name) => parts.GetValueOrDefault(name, "");

        public static Fragment Parse(string? text)
        {
            var parts = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new Fragment(parts);
            }
            var markers = Marker().Matches(text);
            for (var i = 0; i < markers.Count; i++)
            {
                var start = markers[i].Index + markers[i].Length;
                var end = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
                var name = markers[i].Groups[1].Value;
                var body = text[start..end].Trim();
                parts[name] = parts.TryGetValue(name, out var earlier) ? earlier + "\n" + body : body;
            }
            return new Fragment(parts);
        }
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Slot();

    [GeneratedRegex(@"<!--\s*(\w+)\s*-->")]
    private static partial Regex Marker();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
