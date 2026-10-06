"""Termination refunds for Contoso Advisors quarterly-in-advance billing.

Refund = installment * remaining days / days in quarter (actual calendar days).
Household rebalancing fees already charged are never refunded.
"""
from datetime import date, timedelta
from decimal import Decimal, ROUND_HALF_UP

CENT = Decimal("0.01")


def quarter_bounds(d: date) -> tuple[date, date]:
    """Returns the first and last day of the calendar quarter containing d."""
    start_month = 3 * ((d.month - 1) // 3) + 1
    start = date(d.year, start_month, 1)
    next_start = date(d.year + (start_month == 10), (start_month + 3 - 1) % 12 + 1, 1)
    return start, next_start - timedelta(days=1)


def remaining_days(termination: date) -> int:
    """Days in the quarter after the termination date."""
    _, end = quarter_bounds(termination)
    return (end - termination).days


def refund_amount(installment: Decimal, termination: date) -> Decimal:
    start, end = quarter_bounds(termination)
    total = (end - start).days + 1
    return (installment * remaining_days(termination) / total).quantize(CENT, ROUND_HALF_UP)


def refund_method(open_accounts: int) -> str:
    """Credit on the next run if anything stays open, otherwise a check."""
    return "credit" if open_accounts > 0 else "check"
