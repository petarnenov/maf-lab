// Contoso Advisors billing period close checks.
using System;
using System.Collections.Generic;

namespace Contoso.Billing;

public enum RunStatus { Pending, Running, Completed, Failed }

public sealed record PeriodState(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    RunStatus LatestRunStatus,
    int UnpublishedInvoices,
    int OpenCustodianExceptions,
    bool ReconciliationSaved);

public static class ContosoPeriodClose
{
    /// Returns the reasons the period cannot be closed yet; empty means ready.
    public static IReadOnlyList<string> Blockers(PeriodState p)
    {
        var blockers = new List<string>();
        if (p.LatestRunStatus != RunStatus.Completed)
            blockers.Add($"latest run is {p.LatestRunStatus.ToString().ToLowerInvariant()}, expected completed");
        if (p.UnpublishedInvoices > 0)
            blockers.Add($"{p.UnpublishedInvoices} invoice(s) not yet published");
        if (p.OpenCustodianExceptions > 0)
            blockers.Add($"{p.OpenCustodianExceptions} custodian exception(s) unresolved");
        if (!p.ReconciliationSaved)
            blockers.Add("custodian reconciliation not filed in billing binder");
        return blockers;
    }

    /// Target: close within fifteen business days of the run start date.
    public static DateOnly CloseTarget(DateOnly runStart)
    {
        var d = runStart;
        var added = 0;
        while (added < 15)
        {
            d = d.AddDays(1);
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) added++;
        }
        return d;
    }

    /// Quarterly-in-advance: the run for a quarter starts on its first business day.
    public static DateOnly RunStartFor(int year, int quarter)
    {
        var d = new DateOnly(year, 3 * (quarter - 1) + 1, 1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }
}
