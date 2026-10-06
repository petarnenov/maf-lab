// Acme Wealth Partners: tiered fee calculation for ACME-TIER-2026.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Acme.Billing;

/// <summary>A single band of a tiered schedule: rate applies to assets between Floor and Ceiling.</summary>
public sealed record FeeBand(decimal Floor, decimal? Ceiling, decimal AnnualRate);

public static class AcmeTier2026
{
    public const string ScheduleCode = "ACME-TIER-2026";
    public const decimal AnnualMinimum = 2_500m;

    public static readonly IReadOnlyList<FeeBand> Bands = new[]
    {
        new FeeBand(0m, 1_000_000m, 0.0100m),
        new FeeBand(1_000_000m, 3_000_000m, 0.0080m),
        new FeeBand(3_000_000m, 8_000_000m, 0.0060m),
        new FeeBand(8_000_000m, null, 0.0045m),
    };
}

public static class TieredFeeCalculator
{
    /// <summary>Sum of band fees (tiered, not breakpoint): each band is charged its own rate.</summary>
    public static decimal AnnualFee(decimal householdAum, IReadOnlyList<FeeBand> bands)
    {
        if (householdAum <= 0) return 0m;
        decimal total = 0m;
        foreach (var band in bands)
        {
            var top = band.Ceiling ?? decimal.MaxValue;
            var inBand = Math.Min(householdAum, top) - band.Floor;
            if (inBand <= 0) break;
            total += inBand * band.AnnualRate;
        }
        return total;
    }

    /// <summary>Quarterly fee prorated by days under management, with the prorated annual minimum applied.</summary>
    public static decimal QuarterlyFee(decimal householdAum, int daysManaged, int daysInQuarter)
    {
        var fraction = (decimal)daysManaged / daysInQuarter;
        var fee = AnnualFee(householdAum, AcmeTier2026.Bands) / 4m * fraction;
        var minimum = AcmeTier2026.AnnualMinimum / 4m * fraction;
        return Math.Round(Math.Max(fee, minimum), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Allocates the household fee to accounts pro rata by account AUM.</summary>
    public static IDictionary<string, decimal> AllocateToAccounts(decimal householdFee, IDictionary<string, decimal> accountAum)
    {
        var total = accountAum.Values.Sum();
        if (total == 0) return accountAum.ToDictionary(kv => kv.Key, _ => 0m);
        var result = accountAum.ToDictionary(kv => kv.Key, kv => Math.Round(householdFee * kv.Value / total, 2));
        // Put any rounding residue on the largest account so the total matches exactly.
        var residue = householdFee - result.Values.Sum();
        var largest = accountAum.OrderByDescending(kv => kv.Value).First().Key;
        result[largest] += residue;
        return result;
    }
}
