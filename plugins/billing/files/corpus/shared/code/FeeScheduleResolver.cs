// Resolves the fee schedule for an account: account assignment, then household, then firm default.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tamp.Billing.Fees;

public sealed record ScheduleAssignment(string ScheduleCode, DateOnly EffectiveFrom, DateOnly? EffectiveTo)
{
    public bool Covers(DateOnly start, DateOnly end) =>
        EffectiveFrom <= start && (EffectiveTo is null || EffectiveTo >= end);
}

public sealed record AccountBillingInfo(string AccountId, string? HouseholdId,
    IReadOnlyList<ScheduleAssignment> AccountAssignments);

public sealed class FeeScheduleResolver(
    IReadOnlyDictionary<string, IReadOnlyList<ScheduleAssignment>> householdAssignments,
    string? firmDefaultScheduleCode)
{
    /// <summary>Returns the schedule code for the period, or null if none resolves.</summary>
    public string? Resolve(AccountBillingInfo account, DateOnly periodStart, DateOnly periodEnd)
    {
        var direct = account.AccountAssignments.FirstOrDefault(a => a.Covers(periodStart, periodEnd));
        if (direct is not null) return direct.ScheduleCode;

        if (account.HouseholdId is { } hh && householdAssignments.TryGetValue(hh, out var list))
        {
            var inherited = list.FirstOrDefault(a => a.Covers(periodStart, periodEnd));
            if (inherited is not null) return inherited.ScheduleCode;
        }

        return firmDefaultScheduleCode;
    }

    /// <summary>Accounts that would fail the run with FS-REQUIRED.</summary>
    public IReadOnlyList<string> Unresolved(IEnumerable<AccountBillingInfo> accounts, DateOnly start, DateOnly end) =>
        accounts.Where(a => Resolve(a, start, end) is null).Select(a => a.AccountId).ToList();

    /// <summary>Failure reason text used by the run when accounts are unresolved.</summary>
    public static string? FailureReason(IReadOnlyList<string> unresolved) =>
        unresolved.Count == 0 ? null : $"FS-REQUIRED: fee schedule missing for {unresolved.Count} accounts";
}
