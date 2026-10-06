"""Acme Wealth Partners proration helpers (quarterly in arrears)."""
from datetime import date, timedelta

LARGE_FLOW_THRESHOLD = 250_000


def days_in_period(start: date, end: date) -> int:
    """Inclusive number of days in a billing period."""
    return (end - start).days + 1


def days_managed(period_start: date, period_end: date, funded: date, closed: date | None = None) -> int:
    """Days the account was under management within the period."""
    first = max(period_start, funded)
    last = min(period_end, closed) if closed else period_end
    return max(0, (last - first).days + 1)


def average_daily_aum(daily_values: dict[date, float], start: date, end: date) -> float:
    """Average of daily balances; raises if any day is missing (PRORATION-GAP)."""
    missing = [start + timedelta(d) for d in range(days_in_period(start, end))
               if start + timedelta(d) not in daily_values]
    if missing:
        raise ValueError(f"PRORATION-GAP: {len(missing)} day(s) without valuation, first {missing[0]}")
    return sum(daily_values[start + timedelta(d)] for d in range(days_in_period(start, end))) / days_in_period(start, end)


def needs_cash_flow_proration(flows: list[float]) -> bool:
    """Any single contribution or withdrawal above the threshold triggers daily-balance averaging."""
    return any(abs(f) > LARGE_FLOW_THRESHOLD for f in flows)
