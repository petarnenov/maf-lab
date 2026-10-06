"""Contoso Advisors household rebalancing fee rules.

The household rebalancing fee is charged when a client requests an
out-of-cycle rebalance touching two or more accounts. Firm-initiated
rebalances are always exempt; waivers are recorded as zero adjustments.
"""
from dataclasses import dataclass
from datetime import date
from decimal import Decimal
from typing import Optional

HOUSEHOLD_REBALANCING_FEE = Decimal("150.00")
WAIVER_REASONS = {"first-after-onboarding", "hardship", "life-event"}


@dataclass(frozen=True)
class RebalanceRequest:
    household_id: str
    requested_on: date
    client_initiated: bool
    accounts_affected: int
    during_scheduled_review: bool
    waiver_reason: Optional[str] = None


@dataclass(frozen=True)
class Adjustment:
    household_id: str
    amount: Decimal
    reason_code: str
    note: str


def is_chargeable(req: RebalanceRequest) -> bool:
    """True when the request qualifies for the household rebalancing fee."""
    return (
        req.client_initiated
        and not req.during_scheduled_review
        and req.accounts_affected >= 2
    )


def build_adjustment(req: RebalanceRequest) -> Optional[Adjustment]:
    """Returns the adjustment to post on the next quarterly run, or None if exempt."""
    if not is_chargeable(req):
        return None
    if req.waiver_reason:
        if req.waiver_reason not in WAIVER_REASONS:
            raise ValueError(f"unknown waiver reason: {req.waiver_reason}")
        # Waived fees are still recorded at zero for the audit trail.
        return Adjustment(req.household_id, Decimal("0.00"), "REBAL-FEE",
                          f"waived - {req.waiver_reason} (request {req.requested_on:%Y-%m-%d})")
    return Adjustment(req.household_id, HOUSEHOLD_REBALANCING_FEE, "REBAL-FEE",
                      f"household rebalancing fee (request {req.requested_on:%Y-%m-%d})")


def pick_debit_account(balances: dict[str, Decimal], instructed: Optional[str] = None) -> str:
    """Largest account pays unless the client gave a debit instruction."""
    if instructed and instructed in balances:
        return instructed
    return max(balances, key=balances.get)
