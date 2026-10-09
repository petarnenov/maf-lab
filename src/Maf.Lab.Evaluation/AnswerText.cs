using System.Text.RegularExpressions;
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
