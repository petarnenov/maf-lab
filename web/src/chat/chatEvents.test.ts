import { describe, expect, it } from 'vitest';
import { toChatEvent } from './chatEvents';
import type { ConfirmationRequiredData } from '../api/types';

const frame = (event: string, data: unknown) => ({ event, data: JSON.stringify(data) });

describe('toChatEvent', () => {
  it('keeps a confirmation, which would otherwise vanish silently', () => {
    const confirmation: ConfirmationRequiredData = {
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
      question: 'Apply a fee adjustment of -200.00 USD to A-1042?',
      state: 'opaque',
    };

    const event = toChatEvent(frame('confirmation_required', confirmation));

    expect(event?.type).toBe('confirmation_required');
    expect((event?.data as ConfirmationRequiredData).adjustment.accountId).toBe('A-1042');
  });

  it('still drops an event nobody declared', () => {
    expect(toChatEvent(frame('something_new', {}))).toBeNull();
  });

  it('drops a malformed payload rather than throwing', () => {
    expect(toChatEvent({ event: 'confirmation_required', data: '{not json' })).toBeNull();
  });
});
