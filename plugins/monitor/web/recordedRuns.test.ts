import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { createMonitorStore } from './monitorStore';
import type { TraceEvent } from './types';

/** One run captured from the stack by `scripts/capture_ui_events.sh` (the chat's own tests replay the same runs). */
interface RecordedRun {
  id: string;
  frames: { event: string; data: { type: string } & Record<string, unknown> }[];
  /** The run's trace as the trace API served it once the run had ended. */
  trace: TraceEvent[];
  expect: { traceSteps: number; aguiFrames: number };
}

const runs: RecordedRun[] = readFileSync(
  join(process.cwd(), '..', 'evals', 'ui-events.jsonl'),
  'utf8',
)
  .split('\n')
  .filter((line) => line.trim().length > 0)
  .map((line) => JSON.parse(line) as RecordedRun);

describe('runs recorded from the running stack, as the monitor sees them', () => {
  it.each(runs.map((run) => [run.id, run] as const))(
    '%s gives the monitor every frame and every trace step',
    (_id, run) => {
      const store = createMonitorStore(() => 0);
      // As the chat hands them over: the run's start, each official event, its end; the trace from the trace API.
      store.observer.onRunStart?.({ runId: 'r1', turnKey: 'a1' });
      for (const f of run.frames) store.observer.onEvent?.('r1', f.data);
      store.observer.onRunEnd?.('r1', 'finished');
      store.appendTrace('r1', run.trace);

      expect(store.events('a1')).toHaveLength(run.expect.traceSteps);
      // Every frame that crossed the wire, including the ones the chat itself makes no use of.
      expect(store.frames('a1')).toHaveLength(run.expect.aguiFrames);
      expect(store.frames('a1').map((f) => f.seq)).toEqual(run.frames.map((_, i) => i + 1));
    },
  );
});
