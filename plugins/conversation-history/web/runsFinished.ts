import type { PluginRunObserver } from '@maf/plugin-api';

/**
 * Tells the mounted list that a run has finished, so it reads itself again (a new conversation, its last activity, its
 * turn count). It holds no React and no query client: the list subscribes and refreshes through its own tree's client.
 */
export function createRunsFinished() {
  const listeners = new Set<() => void>();
  const observer: PluginRunObserver = {
    onRunEnd: (_runId, outcome) => {
      if (outcome !== 'finished') return;
      for (const listener of listeners) listener();
    },
  };
  return {
    observer,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

export type RunsFinished = ReturnType<typeof createRunsFinished>;

export const runsFinished = createRunsFinished();
