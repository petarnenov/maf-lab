import { describe, expect, it } from 'vitest';
import type { MafWebPlugin } from './api';
import { confirmationRenderers } from './registry';

const Billing = () => null;
const Other = () => null;

describe('confirmationRenderers', () => {
  it('maps each write tool to the first plugin that renders it', () => {
    const plugins: MafWebPlugin[] = [
      { name: 'billing', confirmations: { propose_fee_adjustment: Billing } },
      { name: 'other', confirmations: { propose_fee_adjustment: Other, other_write: Other } },
      { name: 'none' },
    ];

    const renderers = confirmationRenderers({ plugins });

    expect(renderers.propose_fee_adjustment).toBe(Billing);
    expect(renderers.other_write).toBe(Other);
    expect(Object.keys(renderers)).toEqual(['propose_fee_adjustment', 'other_write']);
  });
});
