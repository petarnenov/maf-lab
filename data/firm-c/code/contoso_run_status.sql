-- Contoso Advisors billing run status queries.
-- Tenant scoping is applied by the platform session; these queries never take a firm parameter.

-- Latest run per billing period with its status.
CREATE OR REPLACE VIEW contoso_latest_runs AS
SELECT r.run_id, r.period_start, r.period_end, r.status, r.failure_reason, r.updated_at
FROM billing_runs r
WHERE r.updated_at = (
    SELECT MAX(r2.updated_at) FROM billing_runs r2
    WHERE r2.period_start = r.period_start AND r2.period_end = r.period_end
);

-- Failed runs in the last two quarters, most recent first.
SELECT run_id, period_start, period_end, failure_reason, updated_at
FROM billing_runs
WHERE status = 'failed'
  AND period_start >= CURRENT_DATE - INTERVAL '6 months'
ORDER BY updated_at DESC;

-- Accounts that would fail a run with FS-REQUIRED (no schedule, billable).
SELECT a.account_id, a.household_id, h.household_name
FROM accounts a
JOIN households h ON h.household_id = a.household_id
LEFT JOIN schedule_assignments s ON s.account_id = a.account_id
WHERE a.billable = TRUE
  AND a.status = 'open'
  AND s.schedule_code IS NULL;

-- Pending household rebalancing fee adjustments for the next quarterly run.
SELECT adj.household_id, adj.amount, adj.note, adj.created_at
FROM adjustments adj
WHERE adj.reason_code = 'REBAL-FEE'
  AND adj.run_id IS NULL
ORDER BY adj.created_at;
