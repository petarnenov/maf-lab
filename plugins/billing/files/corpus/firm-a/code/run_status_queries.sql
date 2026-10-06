-- Acme Wealth Partners: billing run status queries used by the billing desk.

-- Failed runs in the current quarter with their failure reason.
SELECT run_id, period_start, period_end, failure_reason, updated_at
FROM billing_runs
WHERE firm_id = 'firm-a'
  AND status = 'failed'
  AND period_start >= DATE_TRUNC('quarter', CURRENT_DATE)
ORDER BY updated_at DESC;

-- Accounts without a fee schedule for a given period (root cause of FS-REQUIRED).
SELECT a.account_id, a.household_id
FROM accounts a
LEFT JOIN fee_schedule_assignments fsa
       ON fsa.account_id = a.account_id
      AND fsa.effective_from <= :period_start
      AND (fsa.effective_to IS NULL OR fsa.effective_to >= :period_end)
WHERE a.firm_id = 'firm-a'
  AND a.status = 'active'
  AND fsa.account_id IS NULL;

-- Invoices awaiting FIRM_ADMIN approval (gross total over $25,000).
SELECT i.invoice_id, i.household_id, i.gross_total, i.created_at
FROM invoices i
WHERE i.firm_id = 'firm-a'
  AND i.gross_total > 25000
  AND i.approval_state = 'AwaitingApproval'
ORDER BY i.gross_total DESC;

-- Runs stuck in running status for more than two hours.
SELECT run_id, updated_at
FROM billing_runs
WHERE firm_id = 'firm-a'
  AND status = 'running'
  AND updated_at < NOW() - INTERVAL '2 hours';
