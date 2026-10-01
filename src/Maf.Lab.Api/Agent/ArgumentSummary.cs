using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>Identifier-only view of tool arguments for audit logs and UI cards. Free-text fields are never included.</summary>
public static class ArgumentSummary
{
    private static readonly HashSet<string> FreeText = new(StringComparer.OrdinalIgnoreCase) { "query", "question", "text", "message", "comment", "note", "reason", "justification" };

    /// <summary>
    /// The same view, taken from a call's arguments as JSON. The protocol renders a tool call's arguments
    /// before the call is invoked, so this is what the client sees — the identifiers, never the free text.
    /// </summary>
    public static string FromJson(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return "";
        }
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return "";
            }
            var arguments = document.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
            return From(arguments);
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
    }

    public static string From(IDictionary<string, object?>? arguments) =>
        string.Join(" ", Redacted(arguments).Select(a => $"{a.Key}={a.Value}"));

    /// <summary>
    /// The arguments a client may see (agui-protocol-only): the identifiers, formatted as in <see cref="From"/>, and never
    /// the free text. A tool call travels with these in place of what the model sent.
    /// </summary>
    public static Dictionary<string, object?> Redacted(IDictionary<string, object?>? arguments) =>
        arguments is null
            ? []
            : arguments
                .Where(a => !FreeText.Contains(a.Key) && a.Value is not null)
                .OrderBy(a => a.Key, StringComparer.Ordinal)
                .ToDictionary(a => a.Key, a => (object?)Format(a.Value), StringComparer.Ordinal);

    private static string Format(object? value)
    {
        var s = value switch
        {
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } e => string.Join(",", e.EnumerateArray().Select(x => x.ToString())),
            System.Collections.IEnumerable list and not string => string.Join(",", list.Cast<object?>()),
            _ => value?.ToString() ?? "",
        };
        s = new string(s.Where(c => char.IsLetterOrDigit(c) || c is ',' or '-' or '_' or '.' or ':').ToArray());
        return s.Length > 40 ? s[..40] : s;
    }
}
