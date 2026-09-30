import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { authHeaders } from '../api/client';
import type { RunSummary } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { SseParser } from '../chat/sseParser';
import { coverageKeys } from './keys';

/**
 * A run as it happens: while the agent has it, its server-sent events replace the snapshot the page loaded. Each
 * change of state refreshes the tree and the file, so a verified candidate or a failure shows without a reload. A
 * candidate waits for a person, not the agent, so it is not followed.
 */
export function useRunEvents(run: RunSummary | null | undefined): RunSummary | null {
  const { session } = useAuth();
  const client = useQueryClient();
  const [live, setLive] = useState<RunSummary | null>(null);
  const id = run?.id;
  const follow = !!run && run.active && run.state !== 'candidate';
  const token = session?.token ?? null;

  useEffect(() => {
    if (!follow || !id) return;
    const abort = new AbortController();
    let lastState: string | null = null;
    const apply = (summary: RunSummary) => {
      setLive(summary);
      if (lastState !== null && summary.state !== lastState) {
        void client.invalidateQueries({ queryKey: coverageKeys.all });
      }
      lastState = summary.state;
    };
    void (async () => {
      try {
        const response = await fetch(`/api/coverage/runs/${encodeURIComponent(id)}/events`, {
          headers: { Accept: 'text/event-stream', ...authHeaders(token) },
          signal: abort.signal,
        });
        if (!response.ok || !response.body) return;
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        const parser = new SseParser();
        for (;;) {
          const { done, value } = await reader.read();
          const frames = done ? parser.flush() : parser.push(decoder.decode(value, { stream: true }));
          for (const frame of frames) {
            if (frame.data) apply(JSON.parse(frame.data) as RunSummary);
          }
          if (done) break;
        }
        void client.invalidateQueries({ queryKey: coverageKeys.all });
      } catch {
        // Aborted on unmount, or the connection dropped: the page's own data is still there, and a reload follows again.
      }
    })();
    return () => abort.abort();
  }, [follow, id, token, client]);

  return live && live.id === id ? live : (run ?? null);
}
