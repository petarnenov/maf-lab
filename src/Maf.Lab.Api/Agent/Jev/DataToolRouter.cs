using Maf.Lab.Domain.Portfolio;
using System.Globalization;
using System.Text.RegularExpressions;
using Maf.Lab.Retrieval.Jev;
using Maf.Lab.Retrieval.Tools;

namespace Maf.Lab.Api.Agent.Jev;

/// <summary>
/// Turns Jev's routing answer for a data question into a read call the turn can issue without asking the model — or
/// into the reason it cannot. Jev names the tool; code only takes the arguments from the question, through fixed
/// patterns: a run id, a status Jev chose, a month and year. Anything the patterns cannot pin down is left to the model,
/// and the write tool is never a candidate: its probability can only stop a route.
/// </summary>
public static partial class DataToolRouter
{
    public const string StatusQuestionId = "run_status";

    /// <summary>The read tools a data question may be routed to. <see cref="WriteTool"/> is asked about only as a veto.</summary>
    public static readonly IReadOnlyList<string> ReadTools =
        [BillingTools.GetStatusName, BillingTools.SearchRunsName, PortfolioTools.GetPortfolio, PortfolioTools.AumHistory, PortfolioTools.ListAccounts];

    /// <summary>The domain each read tool belongs to: a question is routed only among the tools of its domains in scope.</summary>
    internal static readonly IReadOnlyDictionary<string, string> ToolDomain = new Dictionary<string, string>
    {
        [BillingTools.GetStatusName] = Domains.Billing,
        [BillingTools.SearchRunsName] = Domains.Billing,
        [PortfolioTools.GetPortfolio] = Domains.Portfolio,
        [PortfolioTools.AumHistory] = Domains.Portfolio,
        [PortfolioTools.ListAccounts] = Domains.Portfolio,
    };

    public const string WriteTool = Maf.Lab.Domain.Billing.FeeAdjustmentTool.Name;

    public static string ToolQuestionId(string tool) => $"tool_{tool}";

    /// <summary>What each tool does, in the words Jev reads beside the question (never the question itself).</summary>
    internal static readonly IReadOnlyDictionary<string, string> ToolDescriptions = new Dictionary<string, string>
    {
        [BillingTools.GetStatusName] = "Returns the current status of one billing run by its run id: status, billing period, account count and failure reason.",
        [BillingTools.SearchRunsName] = "Lists the firm's billing runs, optionally filtered by status (pending, running, completed, failed) and billing period.",
        [WriteTool] = "Proposes a change to one account's fee (a credit or an increase), for a person to confirm.",
        [PortfolioTools.GetPortfolio] = "Returns one account's current portfolio by its account id: holdings, allocation against its model, drift and total value.",
        [PortfolioTools.AumHistory] = "Returns one account's quarter-end AUM valuations by its account id, oldest first, with each quarter's change.",
        [PortfolioTools.ListAccounts] = "Lists the accounts the signed-in user can access: id, name, household, model portfolio and currency. Takes no account id.",
    };

    internal const string StatusInstructions = "Which billing run status does `user_question` ask about?";

    internal static readonly IReadOnlyDictionary<string, string> StatusCriteria = new Dictionary<string, string>
    {
        ["pending"] = "Runs that are pending or waiting",
        ["running"] = "Runs that are running now",
        ["completed"] = "Runs that completed or finished",
        ["failed"] = "Runs that failed",
        ["none"] = "No particular status is asked about",
    };

    /// <summary>The routing questions added to the intent request: a Noul per tool, the tool described beside it, and the status Choice.</summary>
    internal static IEnumerable<KeyValuePair<string, object>> Questions() =>
        ToolDescriptions.Select(t => KeyValuePair.Create(ToolQuestionId(t.Key),
                (object)new JevNoulQuestion(new JevToolInstructions($"{t.Key}: {t.Value}", "To answer `user_question`, is it necessary to call `tool`?"))))
            .Append(KeyValuePair.Create(StatusQuestionId, (object)new JevChoiceQuestion(StatusInstructions, StatusCriteria)));

    /// <summary>Reads Jev's answers to the routing questions; null when any tool answer is missing.</summary>
    internal static RoutingAnswer? Read(IReadOnlyDictionary<string, JevAnswer> answers)
    {
        var tools = new Dictionary<string, double>();
        foreach (var tool in ToolDescriptions.Keys)
        {
            if (answers.GetValueOrDefault(ToolQuestionId(tool))?.Noul is not { } p || double.IsNaN(p))
            {
                return null;
            }
            tools[tool] = p;
        }
        var status = answers.GetValueOrDefault(StatusQuestionId);
        return new RoutingAnswer(tools, status?.Choice, status?.Confidence);
    }

    /// <summary>The route for a question Jev classified as data, or why there is none.</summary>
    public static (ToolRoute? Route, string? Reason) Route(string question, RoutingAnswer answer, JevOptions o, DomainVerdict? domains = null,
        string? focusAccountId = null)
    {
        if (answer.Tools.GetValueOrDefault(WriteTool) >= 0.5)
        {
            return (null, $"a write is indicated ({answer.Tools[WriteTool]:F2})");
        }
        // Only the tools of the domains Jev put the question in; with no verdict, every read tool, as before domains.
        var candidates = domains is { InScope.Count: > 0 } d
            ? ReadTools.Where(t => d.InScope.Contains(ToolDomain[t])).ToList()
            : [.. ReadTools];
        if (candidates.Count == 0)
        {
            return (null, "no read tool belongs to a domain in scope");
        }
        var tool = candidates.OrderByDescending(t => answer.Tools.GetValueOrDefault(t)).First();
        var p = answer.Tools.GetValueOrDefault(tool);
        if (p < o.MinRouteProbability)
        {
            return (null, $"no read tool is clear ({tool} {p:F2})");
        }

        if (tool == PortfolioTools.ListAccounts)
        {
            // A question that names an account is about that account: the list is not the answer, so the model chooses.
            var named = AccountIds(question).Count;
            return named == 0
                ? (new ToolRoute(tool, new Dictionary<string, object?>(), p), null)
                : (null, $"{tool} takes no account id, the question names {named}");
        }

        if (ToolDomain[tool] == Domains.Portfolio)
        {
            var accounts = AccountIds(question);
            // A question that names no account is about the one in focus (add-focus-state). Which account is code's
            // call, not Jev's: an id is a value, not a closed set (docs/rules/jev-usage.md §2.1 E).
            if (accounts.Count == 0 && focusAccountId is not null)
            {
                return (new ToolRoute(tool, new Dictionary<string, object?> { ["accountId"] = focusAccountId }, p), null);
            }
            return accounts.Count == 1
                ? (new ToolRoute(tool, new Dictionary<string, object?> { ["accountId"] = accounts[0] }, p), null)
                : (null, $"{tool} needs one account id, the question has {accounts.Count}");
        }

        var runs = RunIds(question);
        if (tool == BillingTools.GetStatusName)
        {
            return runs.Count == 1
                ? (new ToolRoute(tool, new Dictionary<string, object?> { ["runId"] = runs[0] }, p), null)
                : (null, $"{tool} needs one run id, the question has {runs.Count}");
        }

        if (runs.Count > 0)
        {
            return (null, $"{tool} chosen but the question names a run");
        }
        var arguments = new Dictionary<string, object?>();
        if (answer.Status is { } status && status != "none" && StatusCriteria.ContainsKey(status)
            && answer.StatusConfidence is { } confidence && confidence >= o.MinConfidence)
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
        return (new ToolRoute(tool, arguments, p), null);
    }

    /// <summary>Distinct account ids, the platform's letter-dash-number form: A-1042, B-200, C-77 (upper-cased).</summary>
    internal static IReadOnlyList<string> AccountIds(string question) =>
        AccountId().Matches(question).Select(m => m.Groups["id"].Value.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();

    [GeneratedRegex(@"(?<![\p{L}\d-])(?<id>[A-Za-z]-\d{2,})(?![\d-])")]
    private static partial Regex AccountId();

    /// <summary>Distinct run ids: a number of three or more digits after "run" (or рън/ран), or after '#'.</summary>
    internal static IReadOnlyList<string> RunIds(string question) =>
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

/// <summary>Instructions with the tool described beside the question, which names it in backticks.</summary>
internal sealed record JevToolInstructions(string Tool, string Question);

/// <summary>Jev's answers to the routing questions: each tool's probability and the run status asked about.</summary>
public sealed record RoutingAnswer(IReadOnlyDictionary<string, double> Tools, string? Status, double? StatusConfidence);

/// <summary>A read call to issue on the model's behalf, with the arguments taken from the question and Jev's probability.</summary>
public sealed record ToolRoute(string Tool, IReadOnlyDictionary<string, object?> Arguments, double Probability);
