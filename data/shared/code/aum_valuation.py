"""AUM valuation lookup used by the billing engine.

The engine never prices securities; it reads stored valuations and enforces
the staleness window (AUM-STALE) before any fee is calculated.
"""
from dataclasses import dataclass
from datetime import date, timedelta
from decimal import Decimal

STALENESS_BUSINESS_DAYS = 3


@dataclass(frozen=True)
class Valuation:
    account_id: str
    as_of: date
    market_value: Decimal
    excluded_value: Decimal = Decimal("0")
    manual_override: bool = False

    @property
    def billable(self) -> Decimal:
        return max(Decimal("0"), self.market_value - self.excluded_value)


def business_days_between(earlier: date, later: date) -> int:
    """Count weekdays strictly after `earlier` up to and including `later`."""
    days, current = 0, earlier
    while current < later:
        current += timedelta(days=1)
        if current.weekday() < 5:
            days += 1
    return days


def is_stale(valuation: Valuation | None, valuation_date: date) -> bool:
    """A missing valuation or one older than the window is stale."""
    if valuation is None:
        return True
    return business_days_between(valuation.as_of, valuation_date) > STALENESS_BUSINESS_DAYS


def stale_accounts(latest: dict[str, Valuation | None], valuation_date: date) -> list[str]:
    """Account ids whose latest valuation would fail the run with AUM-STALE."""
    return sorted(a for a, v in latest.items() if is_stale(v, valuation_date))


def household_aum(member_valuations: list[Valuation]) -> Decimal:
    """Combined billable AUM for breakpoint and tier evaluation."""
    return sum((v.billable for v in member_valuations), Decimal("0"))


def allocate_household_fee(fee: Decimal, members: list[Valuation]) -> dict[str, Decimal]:
    """Pro-rata allocation; the rounding remainder goes to the largest account."""
    total = household_aum(members)
    if total == 0:
        return {m.account_id: Decimal("0.00") for m in members}
    shares = {m.account_id: (fee * m.billable / total).quantize(Decimal("0.01")) for m in members}
    remainder = fee - sum(shares.values())
    largest = max(members, key=lambda m: m.billable).account_id
    shares[largest] += remainder
    return shares
