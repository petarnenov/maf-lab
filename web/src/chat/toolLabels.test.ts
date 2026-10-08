import { describe, expect, it } from 'vitest';
import { toolCallLabel } from './toolLabels';

describe('toolCallLabel', () => {
  it('names the run it is checking', () => {
    expect(
      toolCallLabel({
        toolName: 'get_billing_run_status',
        argumentSummary: 'runId=4417',
        status: 'running',
      }),
    ).toBe('Checking run 4417');
  });

  it('names the Bulgarian history search, running and done', () => {
    const call = { toolName: 'search_bulgarian_history', argumentSummary: '' };
    expect(toolCallLabel({ ...call, status: 'running' })).toBe('Searching Bulgarian history…');
    expect(toolCallLabel({ ...call, status: 'finished' })).toBe('Searched Bulgarian history');
  });

  it('says a compliance review is under way and roughly how long it takes', () => {
    const label = toolCallLabel({
      toolName: 'propose_fee_adjustment',
      argumentSummary: 'accountId=A-1042',
      status: 'running',
    });

    expect(label).toMatch(/compliance/i);
    expect(label).toMatch(/30s/);
  });

  it('says it is listing the accounts, then that it listed them', () => {
    expect(
      toolCallLabel({ toolName: 'list_my_accounts', argumentSummary: '', status: 'running' }),
    ).toBe('Listing your accounts…');
    expect(
      toolCallLabel({ toolName: 'list_my_accounts', argumentSummary: '', status: 'finished' }),
    ).toBe('Listed your accounts');
  });

  it('speaks in the past once a call has finished', () => {
    expect(
      toolCallLabel({ toolName: 'search_documents', argumentSummary: '', status: 'finished' }),
    ).toBe('Searched documentation');
  });
});
