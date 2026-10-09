import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import type { RunSummary } from './types';
import { coverageKeys } from './keys';
import { useRunStream } from './runStream';

/**
 * A run as it happens, for its one-line status: while the agent has it, the run's AG-UI stream replaces the snapshot
 * the page loaded. Each change of state, and the run's end, refreshes the tree and the file, so a verified candidate
 * or a failure shows without a reload. A candidate waits for a person, not the agent, so it is not followed.
 */
export function useRunEvents(run: RunSummary | null | undefined): RunSummary | null {
  const client = useQueryClient();
  const follow = !!run && run.active && run.state !== 'candidate';
  const { summary, ended } = useRunStream(follow ? run.id : null);
  // Compared with the state the page loaded, not the previous event: a burst of events may render only its last.
  const lastState = useRef<string | null>(run?.state ?? null);

  useEffect(() => {
    if (!summary) return;
    if (summary.state !== lastState.current) {
      void client.invalidateQueries({ queryKey: coverageKeys.all });
    }
    lastState.current = summary.state;
  }, [summary, client]);

  useEffect(() => {
    if (ended) void client.invalidateQueries({ queryKey: coverageKeys.all });
  }, [ended, client]);

  return summary && summary.id === run?.id ? summary : (run ?? null);
}
