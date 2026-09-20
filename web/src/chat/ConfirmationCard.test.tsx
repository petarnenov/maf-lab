import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '../test/render';
import { ConfirmationCard } from './ConfirmationCard';

const confirmation = (expiresAt: string | null = null) => ({
  callId: 'c1',
  toolName: 'propose_fee_adjustment',
  adjustmentId: 'adj_1',
  adjustment: {
    adjustmentId: 'adj_1',
    accountId: 'A-1042',
    accountName: 'Ridgeline Family Trust',
    currentFee: 1200,
    amount: -200,
    resultingFee: 1000,
    currency: 'USD',
    periodStart: '2026-10-01',
    periodEnd: '2026-10-31',
  },
  question: 'Apply a fee adjustment of -200.00 USD to A-1042 (Ridgeline Family Trust)?',
  state: 'opaque',
  expiresAt,
});

describe('ConfirmationCard', () => {
  it('shows what a person needs in order to answer', () => {
    renderWithProviders(
      <ConfirmationCard confirmation={confirmation()} state="waiting" onAnswer={vi.fn()} />,
    );

    expect(screen.getByText(/Apply a fee adjustment of -200.00 USD/)).toBeInTheDocument();
    expect(screen.getByText(/A-1042 — Ridgeline Family Trust/)).toBeInTheDocument();
    expect(screen.getByText('1,200.00 USD')).toBeInTheDocument();
    expect(screen.getByText('-200.00 USD')).toBeInTheDocument();
    expect(screen.getByText('1,000.00 USD')).toBeInTheDocument();
    expect(screen.getByText('2026-10-01 to 2026-10-31')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Reject' })).toBeEnabled();
  });

  it('claims nothing while it waits', () => {
    renderWithProviders(
      <ConfirmationCard confirmation={confirmation()} state="waiting" onAnswer={vi.fn()} />,
    );
    expect(screen.queryByText(/Applied/)).not.toBeInTheDocument();
  });

  it('answers approve and reject', async () => {
    const onAnswer = vi.fn();
    renderWithProviders(
      <ConfirmationCard confirmation={confirmation()} state="waiting" onAnswer={onAnswer} />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }));
    expect(onAnswer).toHaveBeenCalledWith(true);

    await userEvent.click(screen.getByRole('button', { name: 'Reject' }));
    expect(onAnswer).toHaveBeenCalledWith(false);
  });

  it('cannot be answered twice while an answer is in flight', async () => {
    const onAnswer = vi.fn();
    renderWithProviders(
      <ConfirmationCard confirmation={confirmation()} state="answering" onAnswer={onAnswer} />,
    );

    expect(screen.getByRole('button', { name: 'Approve' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Approve' }));
    expect(onAnswer).not.toHaveBeenCalled();
  });

  it('says when it stops being answerable', () => {
    renderWithProviders(
      <ConfirmationCard
        confirmation={confirmation('2026-10-01T15:30:00Z')}
        state="waiting"
        onAnswer={vi.fn()}
      />,
    );
    expect(screen.getByText(/Answerable until/)).toBeInTheDocument();
  });

  it.each([
    ['applied', 'Applied.'],
    ['declined', 'Declined. Nothing was changed.'],
    ['expired', 'too old to apply'],
    ['gone', 'no longer waiting'],
  ] as const)('stops asking once it is %s', (state, text) => {
    renderWithProviders(
      <ConfirmationCard confirmation={confirmation()} state={state} onAnswer={vi.fn()} />,
    );

    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.getByText(new RegExp(text))).toBeInTheDocument();
  });
});
