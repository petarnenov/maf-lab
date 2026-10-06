-- Billing run status queries. Every query is scoped by firm_id; the value is
-- bound from the authenticated principal, never from user input.

-- Latest attempt per run for a firm, newest period first.
SELECT r.run_id, r.status, r.period_start, r.period_end, r.account_count,
       r.failure_reason, r.updated_at
FROM billing_runs r
WHERE r.firm_id = @firm_id
ORDER BY r.period_end DESC, r.updated_at DESC;

-- Failed runs with their failure code (text before the first colon).
SELECT r.run_id,
       SUBSTRING(r.failure_reason, 1, CHARINDEX(':', r.failure_reason) - 1) AS failure_code,
       r.failure_reason, r.period_start, r.period_end
FROM billing_runs r
WHERE r.firm_id = @firm_id
  AND r.status = 'failed'
ORDER BY r.updated_at DESC;

-- Accounts without a resolvable fee schedule for a period (FS-REQUIRED candidates).
SELECT a.account_id, a.account_number, a.household_id
FROM accounts a
LEFT JOIN schedule_assignments sa
       ON sa.account_id = a.account_id
      AND sa.effective_from <= @period_start
      AND (sa.effective_to IS NULL OR sa.effective_to >= @period_end)
LEFT JOIN schedule_assignments hs
       ON hs.household_id = a.household_id
      AND hs.effective_from <= @period_start
      AND (hs.effective_to IS NULL OR hs.effective_to >= @period_end)
WHERE a.firm_id = @firm_id
  AND a.billable = 1
  AND sa.schedule_code IS NULL
  AND hs.schedule_code IS NULL;

-- Runs stuck in pending or running for more than 30 minutes.
SELECT r.run_id, r.status, r.updated_at
FROM billing_runs r
WHERE r.firm_id = @firm_id
  AND r.status IN ('pending', 'running')
  AND r.updated_at < DATEADD(MINUTE, -30, SYSUTCDATETIME());

-- Status counts per period for the run dashboard.
SELECT r.period_start, r.period_end, r.status, COUNT(*) AS runs
FROM billing_runs r
WHERE r.firm_id = @firm_id
GROUP BY r.period_start, r.period_end, r.status
ORDER BY r.period_start DESC;
