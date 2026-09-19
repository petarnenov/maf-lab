// Contoso Advisors flat-fee billing (CONTOSO-FLAT-100).
// The fee is charged per household and billed quarterly in advance.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Contoso.Billing;

public enum FlatFeeTier { Essential, Comprehensive, FamilyOffice }

public sealed record ContosoAccount(string AccountId, decimal MarketValue, bool Billable);

public static class ContosoFlatFeeCalculator
{
    public const string ScheduleCode = "CONTOSO-FLAT-100";

    // Annual fee per tier. Amounts are illustrative lab values.
    private static readonly Dictionary<FlatFeeTier, decimal> AnnualFees = new()
    {
        [FlatFeeTier.Essential] = 2_400m,
        [FlatFeeTier.Comprehensive] = 6_000m,
        [FlatFeeTier.FamilyOffice] = 15_000m,
    };

    /// Quarterly installment; rounding remainder goes to Q4 so the year sums exactly.
    public static decimal QuarterlyInstallment(FlatFeeTier tier, int quarter)
    {
        if (quarter is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(quarter));
        var annual = AnnualFees[tier];
        var q = Math.Round(annual / 4m, 2, MidpointRounding.AwayFromZero);
        return quarter == 4 ? annual - q * 3 : q;
    }

    /// Allocates the household installment across billable accounts by market value.
    /// Zero-balance and non-billable accounts receive nothing; the last account absorbs rounding.
    public static IReadOnlyDictionary<string, decimal> Allocate(decimal installment, IEnumerable<ContosoAccount> accounts)
    {
        var billable = accounts.Where(a => a.Billable && a.MarketValue > 0).ToList();
        var result = new Dictionary<string, decimal>();
        if (billable.Count == 0) return result;

        var total = billable.Sum(a => a.MarketValue);
        decimal allocated = 0m;
        for (var i = 0; i < billable.Count; i++)
        {
            var share = i == billable.Count - 1
                ? installment - allocated
                : Math.Round(installment * billable[i].MarketValue / total, 2, MidpointRounding.AwayFromZero);
            result[billable[i].AccountId] = share;
            allocated += share;
        }
        return result;
    }

    /// Prorates an installment by actual days for a partial quarter.
    public static decimal Prorate(decimal installment, DateOnly quarterStart, DateOnly quarterEnd, DateOnly from)
    {
        var totalDays = quarterEnd.DayNumber - quarterStart.DayNumber + 1;
        var billedDays = Math.Max(0, quarterEnd.DayNumber - from.DayNumber + 1);
        return Math.Round(installment * billedDays / totalDays, 2, MidpointRounding.AwayFromZero);
    }
}
