namespace Maf.Lab.Domain.Portfolio;

/// <summary>The portfolio domain's own Qdrant collection and BM25 vocabulary: never billing's.</summary>
public static class PortfolioCollections
{
    public const string Chunks = "maf_portfolio_chunks";
    public const string Meta = "maf_portfolio_meta";
}

/// <summary>Wire names of the tools the portfolio MCP server exposes.</summary>
public static class PortfolioTools
{
    public const string Search = "search_portfolio_documents";
    public const string GetPortfolio = "get_household_portfolio";
    public const string AumHistory = "get_aum_history";
    public const string ListAccounts = "list_my_accounts";
}

/// <summary>One asset class of a portfolio: what it is worth and how far it has drifted from the model's target.</summary>
/// <param name="DriftPct">Actual minus target weight, in percentage points.</param>
public sealed record HoldingView(string AssetClass, decimal MarketValue, decimal TargetWeightPct, decimal ActualWeightPct, decimal DriftPct);

/// <summary>Result of get_household_portfolio. Deliberately has no free-text note field.</summary>
public sealed record HouseholdPortfolio(
    string AccountId,
    string AccountName,
    string HouseholdId,
    string ModelPortfolio,
    decimal DriftTolerancePct,
    bool OutsideTolerance,
    IReadOnlyList<HoldingView> Holdings,
    decimal TotalMarketValue,
    string Currency,
    DateOnly AsOf);

/// <summary>One quarter-end valuation — the AUM the billing desk bills that quarter on.</summary>
/// <param name="ChangePct">Change from the previous quarter-end, null for the first.</param>
public sealed record AumPoint(DateOnly QuarterEnd, decimal Aum, decimal? ChangePct);

/// <summary>Result of get_aum_history: quarter-end AUM, oldest first.</summary>
public sealed record AumHistory(string AccountId, string HouseholdId, string Currency, IReadOnlyList<AumPoint> Valuations);

/// <summary>One account the caller can see: enough to name it and pick a per-account tool. No note, no holdings.</summary>
public sealed record AccountSummary(string AccountId, string Name, string HouseholdId, string ModelPortfolio, string Currency);

/// <summary>Result of list_my_accounts: every account of the caller's firm, ordered by account id.</summary>
public sealed record AccountList(int Count, IReadOnlyList<AccountSummary> Accounts);
