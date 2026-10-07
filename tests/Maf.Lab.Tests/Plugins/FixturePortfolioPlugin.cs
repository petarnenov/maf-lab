using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Plugins.Abstractions;

namespace Maf.Lab.Tests.Plugins;

/// <summary>
/// The portfolio-shaped domain the core tests run on (extract-portfolio): the descriptor of the fake server the shared
/// fakes speak (<c>FakeToolSource.WithPortfolio</c>, the holdings and accounts cards, "A-1043"), installed through the
/// plugin seam as a test-owned plugin. Its plugin name is not "portfolio", so the real plugin's code never joins it; its
/// domain id is. Sameness with the portfolio plugin's table is pinned by that plugin's own test, not assumed here.
/// </summary>
public sealed class FixturePortfolioPlugin : IMafPlugin, IContributesDomainBehaviour
{
    public const string PluginName = "fixture-portfolio";

    public string Name => PluginName;

    public IDomainBehaviour Behaviour { get; } = new StandInPortfolioBehaviour();

    /// <summary>The portfolio domain's [domain] table as of extract-portfolio, without its prompt (a file of the plugin's).</summary>
    public static DomainTable Domain { get; } = new()
    {
        Id = "portfolio",
        Order = 20,
        QuestionKey = "in_portfolio",
        Description = "Investment portfolios on a wealth-management platform: what accounts and households hold, model portfolios and "
            + "target weights, asset allocation, drift and tolerance bands, rebalancing, market value and quarter-end AUM valuations "
            + "and their price corrections, why an account's market value or AUM changed (market movement, contributions, "
            + "withdrawals), cash sweep, held-away assets, and investment performance and returns.",
        SearchTool = "search_portfolio_documents",
        GuardContext = GuardContexts.Documents,
        Subject = "investment portfolios",
        ReadTools = new Dictionary<string, string>
        {
            ["get_household_portfolio"] = "Returns one account's current portfolio by its account id: holdings, allocation against its model, drift and total value.",
            ["get_aum_history"] = "Returns one account's quarter-end AUM valuations by its account id, oldest first, with each quarter's change.",
            ["list_my_accounts"] = "Lists the accounts the signed-in user can access: id, name, household, model portfolio and currency. Takes no account id.",
        },
        CardTypes = new Dictionary<string, string>
        {
            ["get_household_portfolio"] = "maf-lab/holdings",
            ["get_aum_history"] = "maf-lab/aum-history",
            ["list_my_accounts"] = "maf-lab/accounts",
        },
        Intent = new DomainIntent { Data = "an account's portfolio: what an account holds, its allocation, drift or AUM" },
        ScopeSummary = new Dictionary<string, string>
        {
            ["en"] = "portfolios (holdings, model portfolios, drift, rebalancing, quarter-end AUM)",
            ["bg"] = "с портфейлите (позиции, моделни портфейли, отклонение, ребалансиране, AUM към края на тримесечието)",
        },
    };

    public static PluginManifest Manifest() => new()
    {
        Name = PluginName,
        Kind = PluginKinds.Mcp,
        Scope = PluginScopes.Tenant,
        Environments = ["dev", "qa"],
        Description = "A test fixture: the portfolio-shaped domain of the shared fakes",
        Progress = "None — a fixture",
        Stopping = "None — a fixture",
        Domain = Domain,
    };
}

/// <summary>
/// The stand-in portfolio domain's behaviour: a fixed, deliberately simple test double, the fixture's rule rather than
/// portfolio's. It owns the conversation's focus; an account id is a whole word of the shape A-1043; a per-account read
/// binds the one id the question names, or the one in focus; the account list binds nothing; a read of one account moves
/// the focus to it.
/// </summary>
public sealed partial class StandInPortfolioBehaviour : IDomainBehaviour
{
    public const string GetPortfolio = "get_household_portfolio";
    public const string AumHistory = "get_aum_history";
    public const string ListAccounts = "list_my_accounts";

    public string Domain => FixturePortfolioPlugin.Domain.Id;

    public (IReadOnlyDictionary<string, object?>? Arguments, string? Reason) BindRead(string tool, string question,
        IReadOnlyDictionary<string, DecisionAnswer> answers, string? focus, double minConfidence)
    {
        var ids = FocusIds(question);
        if (tool == ListAccounts)
        {
            return ids.Count == 0 ? (new Dictionary<string, object?>(), null) : (null, $"{tool} takes no account id");
        }
        if (ids.Count == 0 && focus is not null)
        {
            return (new Dictionary<string, object?> { ["accountId"] = focus }, null);
        }
        return ids.Count == 1
            ? (new Dictionary<string, object?> { ["accountId"] = ids[0] }, null)
            : (null, $"{tool} needs one account id, the question has {ids.Count}");
    }

    public bool OwnsFocus => true;

    public bool IsFocusId(string id) => AccountId().IsMatch(id);

    public IReadOnlyList<string> FocusIds(string question) =>
        [.. question.Split([' ', ',', '.', '?', '!', ';', ':', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToUpperInvariant()).Where(IsFocusId).Distinct(StringComparer.Ordinal)];

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
                if (a.TryGetProperty("accountId", out var listed) && listed.GetString() is { } each)
                {
                    yield return each;
                }
            }
        }
    }

    public string? FocusFrom(string tool, JsonElement result) =>
        tool is GetPortfolio or AumHistory && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("accountId", out var read)
            ? read.GetString() : null;

    public bool NeedsFocus(string tool) => tool is GetPortfolio or AumHistory;

    public string FocusNote(string focus) => $"\n\n## Conversation focus\nIf the question names no account, it is about account {focus}.";

    public string ClearedFocusNote =>
        "The user has just cleared the account in focus. If this question names no account, ask which account they mean. "
        + "Do not assume an account from earlier in the conversation, and do not call a per-account tool until they name one.";

    public string ClearedFocusToolNote =>
        "Not called: the user cleared the account in focus and this question names no account. Ask which account they mean.";

    [GeneratedRegex(@"^[A-Z]-\d{2,}$")]
    private static partial Regex AccountId();
}
