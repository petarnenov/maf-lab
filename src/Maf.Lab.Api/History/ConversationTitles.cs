using System.Text.RegularExpressions;

namespace Maf.Lab.Api.History;

public static partial class ConversationTitles
{
    public const int DefaultMaxChars = 80;
    public const int MaxChars = 120;

    /// <summary>First question, whitespace collapsed, cut at the last word boundary before 80 characters, with "…".</summary>
    public static string FromQuestion(string question)
    {
        var text = Whitespace().Replace(question.Trim(), " ");
        if (text.Length <= DefaultMaxChars)
        {
            return text;
        }
        var cut = text.LastIndexOf(' ', DefaultMaxChars - 1);
        return (cut > DefaultMaxChars / 2 ? text[..cut] : text[..(DefaultMaxChars - 1)]).TrimEnd() + "…";
    }

    public static string? Validate(string? title)
    {
        var trimmed = title?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxChars ? null : trimmed;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
