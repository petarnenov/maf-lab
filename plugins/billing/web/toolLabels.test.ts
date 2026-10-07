import { describe, expect, it } from 'vitest';
import { billingToolLabels } from './toolLabels';

describe('billing tool labels', () => {
  it('names the run it is checking', () => {
    expect(
      billingToolLabels.get_billing_run_status({ running: true, argumentSummary: 'runId=4417' }),
    ).toBe('Checking run 4417');
  });

  it('says a compliance review is under way and roughly how long it takes', () => {
    const label = billingToolLabels.propose_fee_adjustment({
      running: true,
      argumentSummary: 'accountId=A-1042',
    });

    expect(label).toMatch(/compliance/i);
    expect(label).toMatch(/30s/);
  });

  it('speaks in the past once a call has finished', () => {
    expect(billingToolLabels.search_documents({ running: false, argumentSummary: '' })).toBe(
      'Searched documentation',
    );
  });
});
