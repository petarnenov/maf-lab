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

/// <summary>
/// One asset class of a portfolio: what it is worth, how far it has drifted from the model's target, and the trade that
/// would bring it back (add-rebalance-plan). Numbers, flags and a fixed word only: no free text.
/// </summary>
/// <param name="DriftPct">Actual minus target weight, in percentage points.</param>
/// <param name="OutsideTolerance">Whether this class's drift is beyond the model's tolerance.</param>
/// <param name="TradeToTarget">What to buy (positive) or sell (negative), in whole units of the account's currency, to reach the
/// target weight at the current total. The trades of a portfolio sum to exactly zero.</param>
/// <param name="TradeSide">"buy", "sell" or "none", matching the sign of <paramref name="TradeToTarget"/>.</param>
/// <param name="WeightAfterPct">The weight the class would have after the trades.</param>
public sealed record HoldingView(
    string AssetClass,
    decimal MarketValue,
    decimal TargetWeightPct,
    decimal ActualWeightPct,
    decimal DriftPct,
    bool OutsideTolerance,
    decimal TradeToTarget,
    string TradeSide,
    decimal WeightAfterPct);

/// <summary>The words a trade's side is given in.</summary>
public static class TradeSides
{
    public const string Buy = "buy";
    public const string Sell = "sell";
    public const string None = "none";

    public static string Of(decimal trade) => trade > 0 ? Buy : trade < 0 ? Sell : None;
}

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
    DateOnly AsOf)
{
    /// <summary>
    /// Whether the plan calls for any trade: true exactly when a class is outside the tolerance. The trades are there
    /// either way, so the distance to target shows even when nothing needs doing.
    /// </summary>
    public bool RebalanceNeeded => OutsideTolerance;
}

/// <summary>One quarter-end valuation — the AUM the billing desk bills that quarter on.</summary>
/// <param name="ChangePct">Change from the previous quarter-end, null for the first.</param>
public sealed record AumPoint(DateOnly QuarterEnd, decimal Aum, decimal? ChangePct);

/// <summary>Result of get_aum_history: quarter-end AUM, oldest first.</summary>
public sealed record AumHistory(string AccountId, string HouseholdId, string Currency, IReadOnlyList<AumPoint> Valuations);

/// <summary>One account the caller can see: enough to name it and pick a per-account tool. No note, no holdings.</summary>
public sealed record AccountSummary(string AccountId, string Name, string HouseholdId, string ModelPortfolio, string Currency);

/// <summary>Result of list_my_accounts: every account of the caller's firm, ordered by account id.</summary>
public sealed record AccountList(int Count, IReadOnlyList<AccountSummary> Accounts);
