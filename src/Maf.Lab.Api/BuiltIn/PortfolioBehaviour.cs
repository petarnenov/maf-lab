using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Api.BuiltIn;

/// <summary>
/// The portfolio domain's behaviour: the account a routed portfolio read is for (named in the question, or the one in
/// focus), its tools' result summaries, and the conversation's account in focus (add-focus-state) — which ids are
/// accounts, which cards offered them, which reads move the focus and which need it, and what the model is told.
/// </summary>
public sealed partial class PortfolioBehaviour : IDomainBehaviour
{
    public string Domain => BuiltInDomains.Portfolio;

    public (IReadOnlyDictionary<string, object?>? Arguments, string? Reason) BindRead(string tool, string question,
        IReadOnlyDictionary<string, DecisionAnswer> answers, string? focus, double minConfidence)
    {
        var accounts = AccountIds(question);
        if (tool == PortfolioTools.ListAccounts)
        {
            // A question that names an account is about that account: the list is not the answer, so the model chooses.
            return accounts.Count == 0
                ? (new Dictionary<string, object?>(), null)
                : (null, $"{tool} takes no account id, the question names {accounts.Count}");
        }
        // A question that names no account is about the one in focus (add-focus-state). Which account is code's call,
        // not the engine's: an id is a value, not a closed set (docs/rules/jev-usage.md §2.1 E).
        if (accounts.Count == 0 && focus is not null)
        {
            return (new Dictionary<string, object?> { ["accountId"] = focus }, null);
        }
        return accounts.Count == 1
            ? (new Dictionary<string, object?> { ["accountId"] = accounts[0] }, null)
            : (null, $"{tool} needs one account id, the question has {accounts.Count}");
    }

    public string? Summarize(string tool, JsonElement s) => tool switch
    {
        PortfolioTools.GetPortfolio => $"{Str(s, "accountId")}: {Str(s, "modelPortfolio")}"
            + (s.TryGetProperty("outsideTolerance", out var drift) && drift.ValueKind == JsonValueKind.True ? ", outside tolerance" : ""),
        PortfolioTools.AumHistory when s.TryGetProperty("valuations", out var valuations) =>
            $"{Str(s, "accountId")}: {valuations.GetArrayLength()} quarter-end valuation(s)",
        PortfolioTools.ListAccounts when s.TryGetProperty("count", out var count) => $"{count.GetInt32()} account(s)",
        _ => null,
    };

    public bool OwnsFocus => true;

    public bool IsFocusId(string id) => AccountIdPattern().IsMatch(id);

    public IReadOnlyList<string> FocusIds(string question) => AccountIds(question);

    /// <summary>A card offers the account it is about, and every account a list of accounts shows.</summary>
    public IEnumerable<string> FocusIdsIn(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }
        if (content.TryGetProperty("accountId", out var id) && id.GetString() is { } one)
        {
            yield return one;
        }
        if (content.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in accounts.EnumerateArray())
            {
                if (a.TryGetProperty("accountId", out var aid) && aid.GetString() is { } listed)
                {
                    yield return listed;
                }
            }
        }
    }

    /// <summary>A read of one account's portfolio or AUM puts that account in focus; the list of accounts does not.</summary>
    public string? FocusFrom(string tool, JsonElement result) =>
        tool is PortfolioTools.GetPortfolio or PortfolioTools.AumHistory
        && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("accountId", out var read) ? read.GetString() : null;

    public bool NeedsFocus(string tool) => tool is PortfolioTools.GetPortfolio or PortfolioTools.AumHistory;

    /// <summary>The note the model gets with a focus: the validated id, and nothing else interpolated.</summary>
    public string FocusNote(string focus) => $"\n\n## Conversation focus\nIf the question names no account, it is about account {focus}.";

    public string ClearedFocusNote =>
        "The user has just cleared the account in focus. If this question names no account, ask which account they mean. "
        + "Do not assume an account from earlier in the conversation, and do not call a per-account tool until they name one.";

    public string ClearedFocusToolNote =>
        "Not called: the user cleared the account in focus and this question names no account. Ask which account they mean.";

    /// <summary>Distinct account ids, the platform's letter-dash-number form: A-1042, B-200, C-77 (upper-cased).</summary>
    public static IReadOnlyList<string> AccountIds(string question) =>
        AccountId().Matches(question).Select(m => m.Groups["id"].Value.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    [GeneratedRegex(@"(?<![\p{L}\d-])(?<id>[A-Za-z]-\d{2,})(?![\d-])")]
    private static partial Regex AccountId();

    [GeneratedRegex(@"^[A-Z]-\d{2,}$")]
    private static partial Regex AccountIdPattern();
}
