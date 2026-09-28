using System.Text.Json;
using System.Text.RegularExpressions;
using Maf.Lab.Domain.Portfolio;
using Maf.Lab.Domain.Tenancy;
using Maf.Lab.Retrieval.Billing;

namespace Maf.Lab.Portfolio.Store;

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
        : this(File.ReadAllText(SeedPaths.Resolve(configuration, "Portfolio:SeedPath", "portfolio-households.json")))
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
        var holdings = r.Holdings.Select(h =>
        {
            var actual = total == 0 ? 0 : Math.Round(h.MarketValue / total * 100, 1);
            return new HoldingView(h.AssetClass, h.MarketValue, h.TargetWeightPct, actual, Math.Round(actual - h.TargetWeightPct, 1));
        }).ToList();
        return new HouseholdPortfolio(r.AccountId, r.Name, r.HouseholdId, r.ModelPortfolio, r.DriftTolerancePct,
            holdings.Any(h => Math.Abs(h.DriftPct) > r.DriftTolerancePct), holdings, total, r.Currency, r.AsOf);
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

    private PortfolioRecord? Find(Principal principal, string accountId)
    {
        var id = Normalize(accountId);
        return _records.FirstOrDefault(r => r.FirmId == principal.FirmId.Value && Normalize(r.AccountId) == id);
    }

    /// <summary>"account A-1042", "a-1042" and "A1042" all mean the same account, as in billing.</summary>
    internal static string Normalize(string accountId) =>
        new(AccountWord().Replace(accountId ?? "", "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    [GeneratedRegex(@"^\s*account\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountWord();
}
