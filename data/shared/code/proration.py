"""Proration helpers: billable days, day-count conventions, and segment validation.

Raises ProrationGapError (maps to failure code PRORATION-GAP) when schedule
segments inside a billing period overlap or leave gaps.
"""
from dataclasses import dataclass
from datetime import date, timedelta
from decimal import Decimal


class ProrationGapError(Exception):
    """Inconsistent account timeline; the run fails with PRORATION-GAP."""


@dataclass(frozen=True)
class Segment:
    start: date
    end: date  # inclusive
    schedule_code: str


def billable_days(period_start: date, period_end: date,
                  funded: date | None, closed: date | None) -> int:
    """Inclusive count of days the account was billable inside the period."""
    start = max(period_start, funded or period_start)
    end = min(period_end, closed or period_end)
    if end < start:
        return 0
    return (end - start).days + 1


def day_count_fraction(days: int, convention: str = "ACT/365") -> Decimal:
    """Fraction of a year represented by `days` under the firm's convention."""
    if convention == "ACT/365":
        return Decimal(days) / Decimal(365)
    if convention == "ACT/360":
        return Decimal(days) / Decimal(360)
    if convention == "30/360":
        # Approximation used for whole-month periods: each month counts as 30 days.
        months = round(days / 30.4375)
        return Decimal(months * 30) / Decimal(360)
    raise ValueError(f"unknown day-count convention: {convention}")


def validate_segments(period_start: date, period_end: date, segments: list[Segment]) -> None:
    """Segments must be contiguous and non-overlapping across the whole period."""
    ordered = sorted(segments, key=lambda s: s.start)
    expected = period_start
    for seg in ordered:
        if seg.start != expected:
            raise ProrationGapError(
                f"PRORATION-GAP: segment {seg.schedule_code} starts {seg.start}, expected {expected}")
        expected = seg.end + timedelta(days=1)
    if expected != period_end + timedelta(days=1):
        raise ProrationGapError(f"PRORATION-GAP: coverage ends {expected - timedelta(days=1)}")


def prorated_fee(annual_fee: Decimal, days: int, convention: str = "ACT/365") -> Decimal:
    """Period fee rounded to cents with banker's rounding."""
    return (annual_fee * day_count_fraction(days, convention)).quantize(Decimal("0.01"))
