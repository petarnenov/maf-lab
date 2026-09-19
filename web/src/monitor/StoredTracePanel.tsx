import { ApiError } from '../api/client';
import { MonitorPanel } from './MonitorPanel';
import { useTurnTrace } from './useTurnTrace';

/** Loads a finished turn's stored trace and shows it in the monitor. */
export function StoredTracePanel({ turnId, title }: { turnId: string; title?: string }) {
  const trace = useTurnTrace(turnId);
  const error = trace.isError
    ? trace.error instanceof ApiError && trace.error.status === 404
      ? 'No trace for this turn (it may be older than the retention period).'
      : 'Could not load the trace.'
    : null;
  return (
    <MonitorPanel
      events={trace.data?.events ?? []}
      loading={trace.isLoading}
      error={error}
      title={title}
      resetKey={turnId}
    />
  );
}
