using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Billing;
using Maf.Lab.Retrieval.Configuration;

namespace Maf.Lab.Portfolio.Store;

// names a domain until the extract-evals-plugin follow-up moves it (introduce-plugins 8.1)
// The portfolio host, which stays here until the eval stops hosting it in-process.
internal sealed record HoldingRecord(string AssetClass, decimal MarketValue, decimal TargetWeightPct);

internal sealed record AumRecord(DateOnly QuarterEnd, decimal Aum);

/// <summary>Seed record as stored. Has a free-text Note that must never leave this class.</summary>
internal sealed record PortfolioRecord(
    string FirmId,
    string AccountId,
    string Name,
    string HouseholdId,
    string ModelPortfolio,
    decimal DriftTolerancePct,
    string Currency,
    DateOnly AsOf,
    IReadOnlyList<HoldingRecord> Holdings,
    IReadOnlyList<AumRecord> AumHistory,
    string? Note);

/// <summary>
/// Read-only household portfolios backed by seed JSON, always scoped to the caller's firm. Account ids are the billing
/// domain's, so "A-1042" names the same account on both sides of the boundary.
/// </summary>
public sealed partial class PortfolioStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyList<PortfolioRecord> _records;

    public PortfolioStore(IConfiguration configuration)
        : this(SeedPaths.Read(configuration, "Portfolio:SeedPath", "portfolio-households.json"))
    {
    }

    internal PortfolioStore(string json) =>
        _records = JsonSerializer.Deserialize<List<PortfolioRecord>>(json, Json) ?? [];

    /// <summary>The account's portfolio, or null when it is not this firm's — the same answer as "there is no such account".</summary>
    public HouseholdPortfolio? Portfolio(Principal principal, string accountId)
    {
        if (Find(principal, accountId) is not { } r)
        {
            return null;
        }
        var total = r.Holdings.Sum(h => h.MarketValue);
        var trades = TradesToTarget(r.Holdings, total);
        var holdings = r.Holdings.Select((h, i) =>
        {
            var actual = total == 0 ? 0 : Math.Round(h.MarketValue / total * 100, 1);
            var drift = Math.Round(actual - h.TargetWeightPct, 1);
            var after = total == 0 ? 0 : Math.Round((h.MarketValue + trades[i]) / total * 100, 1);
            return new HoldingView(h.AssetClass, h.MarketValue, h.TargetWeightPct, actual, drift,
                Math.Abs(drift) > r.DriftTolerancePct, trades[i], TradeSides.Of(trades[i]), after);
        }).ToList();
        return new HouseholdPortfolio(r.AccountId, r.Name, r.HouseholdId, r.ModelPortfolio, r.DriftTolerancePct,
            holdings.Any(h => h.OutsideTolerance), holdings, total, r.Currency, r.AsOf);
    }

    /// <summary>
    /// The trade that brings each class to its target weight at the current total, in whole currency units. Rounding
    /// can leave the trades a unit or two off zero; that remainder goes to the largest exact trade (the first on a
    /// tie), so the plan only ever moves money between classes.
    /// </summary>
    private static decimal[] TradesToTarget(IReadOnlyList<HoldingRecord> holdings, decimal total)
    {
        var exact = holdings.Select(h => total * h.TargetWeightPct / 100 - h.MarketValue).ToArray();
        var trades = exact.Select(e => Math.Round(e, 0, MidpointRounding.ToEven)).ToArray();
        var remainder = -trades.Sum();
        if (remainder != 0 && trades.Length > 0)
        {
            var largest = 0;
            for (var i = 1; i < exact.Length; i++)
            {
                if (Math.Abs(exact[i]) > Math.Abs(exact[largest]))
                {
                    largest = i;
                }
            }
            trades[largest] += remainder;
        }
        // A rounded −0 is still zero, and says "none".
        return [.. trades.Select(t => t == 0 ? 0m : t)];
    }

    /// <summary>Quarter-end AUM, oldest first, with each quarter's change; null when the account is not this firm's.</summary>
    public AumHistory? History(Principal principal, string accountId)
    {
        if (Find(principal, accountId) is not { } r)
        {
            return null;
        }
        decimal? previous = null;
        var points = new List<AumPoint>();
        foreach (var v in r.AumHistory.OrderBy(v => v.QuarterEnd))
        {
            points.Add(new AumPoint(v.QuarterEnd, v.Aum, previous is { } p && p != 0 ? Math.Round((v.Aum - p) / p * 100, 1) : null));
            previous = v.Aum;
        }
        return new AumHistory(r.AccountId, r.HouseholdId, r.Currency, points);
    }

    /// <summary>Every account of the caller's firm, ordered by account id: exactly the ids <see cref="Find"/> accepts.</summary>
    public AccountList List(Principal principal)
    {
        var accounts = _records.Where(r => Owns(principal, r))
            .OrderBy(r => r.AccountId, StringComparer.Ordinal)
            .Select(r => new AccountSummary(r.AccountId, r.Name, r.HouseholdId, r.ModelPortfolio, r.Currency))
            .ToList();
        return new AccountList(accounts.Count, accounts);
    }

    private PortfolioRecord? Find(Principal principal, string accountId)
    {
        var id = Normalize(accountId);
        return _records.FirstOrDefault(r => Owns(principal, r) && Normalize(r.AccountId) == id);
    }

    /// <summary>The one firm rule of this store: a record is visible only to its own firm.</summary>
    private static bool Owns(Principal principal, PortfolioRecord record) => record.FirmId == principal.TenantId.Value;

    /// <summary>"account A-1042", "a-1042" and "A1042" all mean the same account, as in billing.</summary>
    internal static string Normalize(string accountId) =>
        new(AccountWord().Replace(accountId ?? "", "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    [GeneratedRegex(@"^\s*account\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountWord();
}
