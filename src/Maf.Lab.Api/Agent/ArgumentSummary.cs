using Microsoft.Extensions.AI;

namespace Maf.Lab.Api.Agent;

/// <summary>Identifier-only view of tool arguments for audit logs and UI cards. Free-text fields are never included.</summary>
public static class ArgumentSummary
{
    private static readonly HashSet<string> FreeText = new(StringComparer.OrdinalIgnoreCase) { "query", "question", "text", "message", "comment", "note" };

    public static string From(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "";
        }
        return string.Join(" ", arguments
            .Where(a => !FreeText.Contains(a.Key) && a.Value is not null)
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => $"{a.Key}={Format(a.Value)}"));
    }

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
