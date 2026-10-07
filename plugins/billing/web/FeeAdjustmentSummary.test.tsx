import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FeeAdjustmentSummary } from './FeeAdjustmentSummary';

describe('FeeAdjustmentSummary', () => {
  it('shows the account, the fee now, the change, the fee after and the period', () => {
    render(
      <FeeAdjustmentSummary
        schema={null}
        summary={{
          adjustmentId: 'adj_1',
          accountId: 'A-1042',
          accountName: 'Ridgeline Family Trust',
          currentFee: 1200,
          amount: -200,
          resultingFee: 1000,
          currency: 'USD',
          periodStart: '2026-10-01',
          periodEnd: '2026-10-31',
        }}
      />,
    );

    expect(screen.getByText('A-1042 — Ridgeline Family Trust')).toBeInTheDocument();
    expect(screen.getByText('1,200.00 USD')).toBeInTheDocument();
    expect(screen.getByText('-200.00 USD')).toBeInTheDocument();
    expect(screen.getByText('1,000.00 USD')).toBeInTheDocument();
    expect(screen.getByText('2026-10-01 to 2026-10-31')).toBeInTheDocument();
  });
});
