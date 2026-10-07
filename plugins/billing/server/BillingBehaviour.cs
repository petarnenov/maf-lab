using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Plugins.Abstractions;
using Maf.Lab.Retrieval.Tools;

namespace Maf.Lab.Plugins.Billing;

/// <summary>
/// The billing domain's behaviour: the run-status question beside its read tools, the arguments of a routed billing
/// read taken from the question through fixed patterns (a run id, a status the engine chose, a month and year), the run
/// status a mixed question needs beside its documentation, and its tools' result summaries.
/// </summary>
public sealed partial class BillingBehaviour : IDomainBehaviour
{
    public const string StatusQuestionId = "run_status";

    internal const string StatusInstructions = "Which billing run status does `user_question` ask about?";

    internal static readonly IReadOnlyDictionary<string, string> StatusCriteria = new Dictionary<string, string>
    {
        ["pending"] = "Runs that are pending or waiting",
        ["running"] = "Runs that are running now",
        ["completed"] = "Runs that completed or finished",
        ["failed"] = "Runs that failed",
        ["none"] = "No particular status is asked about",
    };

    public string Domain => BillingPlugin.DomainId;

    public IReadOnlyDictionary<string, DecisionQuestion> DataQuestions { get; } = new Dictionary<string, DecisionQuestion>
    {
        [StatusQuestionId] = new ChoiceQuestion(StatusInstructions, StatusCriteria),
    };

    public (IReadOnlyDictionary<string, object?>? Arguments, string? Reason) BindRead(string tool, string question,
        IReadOnlyDictionary<string, DecisionAnswer> answers, string? focus, double minConfidence)
    {
        var runs = RunIds(question);
        if (tool == BillingTools.GetStatusName)
        {
            return runs.Count == 1
                ? (new Dictionary<string, object?> { ["runId"] = runs[0] }, null)
                : (null, $"{tool} needs one run id, the question has {runs.Count}");
        }
        if (runs.Count > 0)
        {
            return (null, $"{tool} chosen but the question names a run");
        }
        var arguments = new Dictionary<string, object?>();
        if (answers.GetValueOrDefault(StatusQuestionId) is { Choice: { } status } a && status != "none" && StatusCriteria.ContainsKey(status)
            && a.Confidence is { } confidence && confidence >= minConfidence)
        {
            arguments["status"] = status;
        }
        var (period, unparsed) = Period(question);
        if (unparsed)
        {
            return (null, "the question has a time expression the router does not parse");
        }
        if (period is { } month)
        {
            arguments["periodFrom"] = month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            arguments["periodTo"] = month.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return (arguments, null);
    }

    /// <summary>
    /// A mixed question names one run: its status goes out beside the documentation search. The run id comes from the
    /// question through the fixed pattern, never from the model; two run ids, or none, and the model decides.
    /// </summary>
    public IReadOnlyList<DomainRoute> Alongside(string intent, string question, double confidence)
    {
        if (intent != "Mixed")
        {
            return [];
        }
        var runs = RunIds(question);
        return runs.Count == 1 ? [new DomainRoute(BillingTools.GetStatusName, new Dictionary<string, object?> { ["runId"] = runs[0] }, confidence)] : [];
    }

    public string? Summarize(string tool, JsonElement s) => tool switch
    {
        BillingTools.GetStatusName => $"run {Str(s, "runId")}: {Str(s, "status")}",
        BillingTools.SearchRunsName when s.TryGetProperty("runs", out var runs) => $"{runs.GetArrayLength()} run(s)",
        Maf.Lab.Domain.Billing.FeeAdjustmentTool.Name => Str(s, "status") switch
        {
            "applied" => "applied",
            "already_applied" => "already applied",
            "declined" => "declined by the advisor",
            _ => "nothing applied",
        },
        _ => null,
    };

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Distinct run ids: a number of three or more digits after "run" (or рън/ран), or after '#'.</summary>
    public static IReadOnlyList<string> RunIds(string question) =>
        RunId().Matches(question).Select(m => m.Groups["id"].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The first day of "&lt;month&gt; &lt;year&gt;" when that is the question's only time expression; unparsed when the
    /// question has any other (a year alone, a month without a year, "last month", a quarter, a date).
    /// </summary>
    internal static (DateOnly? Month, bool Unparsed) Period(string question)
    {
        var matches = MonthYear().Matches(question);
        DateOnly? month = null;
        var rest = question;
        if (matches.Count == 1 && Months.TryGetValue(matches[0].Groups["month"].Value.ToLowerInvariant(), out var m))
        {
            month = new DateOnly(int.Parse(matches[0].Groups["year"].Value, CultureInfo.InvariantCulture), m, 1);
            rest = question.Remove(matches[0].Index, matches[0].Length);
        }
        return (month, matches.Count > 1 || TimeWords().IsMatch(rest));
    }

    private static readonly Dictionary<string, int> Months = BuildMonths();

    private static Dictionary<string, int> BuildMonths()
    {
        string[][] names =
        [
            ["january", "jan", "януари", "yanuari"], ["february", "feb", "февруари", "fevruari"], ["march", "mar", "март", "mart"],
            ["april", "apr", "април"], ["may", "май", "mai"], ["june", "jun", "юни", "yuni"], ["july", "jul", "юли", "yuli"],
            ["august", "aug", "август", "avgust"], ["september", "sep", "sept", "септември", "septemvri"],
            ["october", "oct", "октомври", "oktomvri"], ["november", "nov", "ноември", "noemvri"], ["december", "dec", "декември", "dekemvri"],
        ];
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            foreach (var name in names[i])
            {
                map[name] = i + 1;
            }
        }
        return map;
    }

    [GeneratedRegex(@"(?:(?<![\p{L}\d])(?:run|рън|ран)\s*(?:#|№|no\.?)?\s*|#)(?<id>\d{3,})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex RunId();

    [GeneratedRegex(@"(?<![\p{L}])(?<month>january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sept|sep|oct|nov|dec|януари|февруари|март|април|май|юни|юли|август|септември|октомври|ноември|декември|yanuari|fevruari|mart|mai|yuni|yuli|avgust|septemvri|oktomvri|noemvri|dekemvri)\.?,?\s+(?<year>(?:19|20)\d{2})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex MonthYear();

    // Anything that names a time the router would have to interpret: years, month names, relative days and periods, dates.
    [GeneratedRegex(@"(?<![\p{L}\d])(?:(?:19|20)\d{2}|january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sept|sep|oct|nov|dec|today|yesterday|tomorrow|tonight|weeks?|months?|quarters?|years?|since|between|before|after|ago|q[1-4]|януари|февруари|март|април|май|юни|юли|август|септември|октомври|ноември|декември|днес|вчера|утре|седмиц\p{L}*|месец\p{L}*|тримесеч\p{L}*|годин\p{L}*|yanuari|fevruari|mart|mai|yuni|yuli|avgust|septemvri|oktomvri|noemvri|dekemvri|dnes|vchera|sedmic\p{L}*|mesec\p{L}*|trimesech\p{L}*|godin\p{L}*|\d{1,2}[./-]\d{1,2})(?![\p{L}\d])", RegexOptions.IgnoreCase)]
    private static partial Regex TimeWords();
}
