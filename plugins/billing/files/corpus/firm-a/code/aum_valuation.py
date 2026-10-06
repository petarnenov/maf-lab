"""Acme Wealth Partners AUM valuation and staleness checks."""
from dataclasses import dataclass
from datetime import date

STALE_BUSINESS_DAYS = {"PRIMARY": 3, "LEGACY": 7}
CARRY_FORWARD_REVIEW_LIMIT = 1_000_000


@dataclass
class Position:
    account_id: str
    custodian_tier: str  # "PRIMARY" or "LEGACY"
    quantity: float
    price: float
    as_of: date


def business_days_between(a: date, b: date) -> int:
    """Count weekdays strictly after a up to and including b."""
    days, cur = 0, a
    while cur < b:
        cur = date.fromordinal(cur.toordinal() + 1)
        if cur.weekday() < 5:
            days += 1
    return days


def is_stale(pos: Position, period_end: date) -> bool:
    """AUM-STALE if the position is older than the custodian's tolerance."""
    return business_days_between(pos.as_of, period_end) > STALE_BUSINESS_DAYS[pos.custodian_tier]


def account_aum(positions: list[Position]) -> float:
    return sum(p.quantity * p.price for p in positions)


def needs_firm_admin_review(carried_forward_value: float) -> bool:
    """Carried-forward alternative asset values above $1,000,000 need FIRM_ADMIN review."""
    return carried_forward_value > CARRY_FORWARD_REVIEW_LIMIT
