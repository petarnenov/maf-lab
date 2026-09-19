// Tiered and breakpoint fee calculation for the billing engine.
// All amounts are decimal; rounding happens once at the account level.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tamp.Billing.Fees;

/// <summary>One band of a fee schedule. UpperBound == null means open-ended.</summary>
public sealed record FeeTier(decimal LowerBound, decimal? UpperBound, decimal AnnualRate);

public enum ScheduleKind { Flat, Tiered, Breakpoint }

public sealed record FeeSchedule(string Code, ScheduleKind Kind, IReadOnlyList<FeeTier> Tiers,
    decimal? AnnualMinimum = null, decimal? AnnualMaximum = null);

public static class TieredFeeCalculator
{
    /// <summary>Annual fee for a tiered schedule: each band's portion times its rate, summed.</summary>
    public static decimal AnnualTieredFee(decimal aum, IReadOnlyList<FeeTier> tiers)
    {
        decimal total = 0m;
        foreach (var tier in tiers.OrderBy(t => t.LowerBound))
        {
            if (aum <= tier.LowerBound) break;
            var top = tier.UpperBound is { } ub ? Math.Min(aum, ub) : aum;
            total += (top - tier.LowerBound) * tier.AnnualRate;
        }
        return total;
    }

    /// <summary>Annual fee for a breakpoint schedule: one rate applied to the whole balance.
    /// A balance exactly at a breakpoint falls into the higher band (lower rate).</summary>
    public static decimal AnnualBreakpointFee(decimal aum, IReadOnlyList<FeeTier> tiers)
    {
        var band = tiers.OrderBy(t => t.LowerBound).Last(t => aum >= t.LowerBound);
        return aum * band.AnnualRate;
    }

    /// <summary>Annual fee for any schedule kind, with minimum and maximum applied.</summary>
    public static decimal AnnualFee(decimal aum, FeeSchedule schedule)
    {
        var fee = schedule.Kind switch
        {
            ScheduleKind.Flat => aum * schedule.Tiers[0].AnnualRate,
            ScheduleKind.Tiered => AnnualTieredFee(aum, schedule.Tiers),
            ScheduleKind.Breakpoint => AnnualBreakpointFee(aum, schedule.Tiers),
            _ => throw new ArgumentOutOfRangeException(nameof(schedule))
        };
        if (schedule.AnnualMinimum is { } min && fee < min) fee = min;
        if (schedule.AnnualMaximum is { } max && fee > max) fee = max;
        return fee;
    }

    /// <summary>Scales an annual fee to a period using actual/365 and rounds to cents (banker's rounding).</summary>
    public static decimal PeriodFee(decimal annualFee, int billableDays) =>
        Math.Round(annualFee * billableDays / 365m, 2, MidpointRounding.ToEven);
}
