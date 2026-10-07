import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { PendingWrite } from '../api/types';
import { PluginsContext } from '../plugins/context';
import { renderWithProviders } from '../test/render';
import { ConfirmationCard } from './ConfirmationCard';

const confirmation = (expiresAt: string | null = null): PendingWrite => ({
  writeId: 'w_1',
  toolName: 'propose_fee_adjustment',
  summary: { accountId: 'A-1042', amount: -200, periodStart: '2026-10-01' },
  summarySchema: {
    type: 'object',
    properties: {
      accountId: { title: 'Account', type: 'string' },
      amount: { title: 'Adjustment', type: 'number' },
      periodStart: { title: 'From', type: 'string', format: 'date' },
    },
  },
  question: 'Apply a fee adjustment of -200.00 USD to A-1042 (Ridgeline Family Trust)?',
  expiresAt,
});

/** A plugin that renders this tool's summary itself, as a domain's web part may. */
const Rendered = ({ summary }: { summary: unknown; schema: unknown }) => (
  <p>rendered by the plugin: {(summary as { accountId: string }).accountId}</p>
);

describe('ConfirmationCard', () => {
  it("shows what a person needs in order to answer, from the summary's schema", () => {
    renderWithProviders(
      <ConfirmationCard confirmation={confirmation()} state="waiting" onAnswer={vi.fn()} />,
    );

    expect(screen.getByText(/Apply a fee adjustment of -200.00 USD/)).toBeInTheDocument();
    expect(screen.getAllByRole('term').map((t) => t.textContent)).toEqual([
      'Account',
      'Adjustment',
      'From',
    ]);
    expect(screen.getByText('A-1042')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Reject' })).toBeEnabled();
  });

  it("lets the write's plugin render its summary", () => {
    renderWithProviders(
      <PluginsContext.Provider
        value={{
          plugins: [{ name: 'fixture', confirmations: { propose_fee_adjustment: Rendered } }],
        }}
      >
        <ConfirmationCard confirmation={confirmation()} state="waiting" onAnswer={vi.fn()} />
      </PluginsContext.Provider>,
    );

    expect(screen.getByText('rendered by the plugin: A-1042')).toBeInTheDocument();
    expect(screen.queryByTestId('write-summary')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled();
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
