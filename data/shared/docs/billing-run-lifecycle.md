# Billing Run Lifecycle

## What a Billing Run Is

A billing run is a single execution of the fee calculation for one firm and one billing period. It records the period start and end dates, the valuation date, the number of accounts processed, and the outcome. Runs are identified by a numeric run id that is unique across the platform. Only one active run may exist for a given firm and period at a time, which prevents duplicate invoices from being generated for the same clients. Every attempt of a run is kept in the history for auditing.

## Statuses

### Pending, Running, Completed and Failed

**Pending.** A run is pending when it has been created but not yet picked up by the billing worker. Pending runs can be cancelled by OPS or FIRM_ADMIN users without side effects. Runs normally stay pending for less than a few minutes; a run pending for longer usually indicates that the worker queue is paused for maintenance.

**Running.** A running run is actively processing accounts. Progress is reported as the number of accounts completed out of the total. A running run cannot be edited or cancelled from the user interface, because partial results are written to a staging area and must be finished or rolled back by the worker.

**Completed.** A completed run has calculated fees for every account in scope and passed validation. Its results are frozen: invoices can be generated, and any later correction must be made through adjustments or credits rather than by editing the run.

**Failed.** A failed run stopped because of a blocking problem. The run records a failure reason that starts with a failure code, such as FS-REQUIRED or AUM-STALE, followed by a human-readable explanation. No invoices are produced from a failed run. After the root cause is fixed, the run can be re-run, which creates a new attempt under the same run id.

## Transitions

### Allowed Paths

The allowed transitions are pending to running, running to completed, running to failed, and failed back to pending when a re-run is requested. A completed run never changes status. If a completed run needs to be reversed, a firm administrator voids its invoices and starts a new run for the same period. Any other transition, such as completed back to running or pending directly to failed without a worker attempt, is rejected by the platform and logged as an error. Cancelling a pending run removes it from the queue and records the user and reason, and the period can then receive a new run.
