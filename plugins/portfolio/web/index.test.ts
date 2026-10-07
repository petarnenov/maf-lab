import { describe, expect, it } from 'vitest';
import portfolio from './index';

describe('portfolio tool labels', () => {
  it('says it is listing the accounts, then that it listed them', () => {
    const label = portfolio.toolLabels!.list_my_accounts;
    expect(label({ running: true, argumentSummary: '' })).toBe('Listing your accounts…');
    expect(label({ running: false, argumentSummary: '' })).toBe('Listed your accounts');
  });
});
