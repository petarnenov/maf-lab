import { useEffect, useMemo, useSyncExternalStore } from 'react';
import { useApi, type ChatContext } from '@maf/plugin-api';
import { MonitorPanel } from './MonitorPanel';
import { monitorStore, type MonitorStore } from './monitorStore';
import { reconstructTurn } from './reconstructTurn';
import type { TraceEvent } from './types';
import { useTimeTravel } from './useTimeTravel';
import { isNoTrace, useTurnTrace } from './useTurnTrace';

/** How often the pane reads a live run's trace while it shows it. */
export const TRACE_POLL_MS = 500;

/** What the pane says for a turn whose stored trace the monitor no longer keeps. */
export const TRACE_EXPIRED = 'Trace expired (kept 7 days).';

/**
 * "Behind the scenes" for the chat's selected turn: its trace and AG-UI frames, live while it runs and stored once it
 * has, with time travel that shows the turn's bubble as it was at the step (the chat's turn view override).
 */
export function MonitorPane({
  context,
  store = monitorStore,
}: {
  context: ChatContext;
  store?: MonitorStore;
}) {
  const { turn, setTurnView } = context;
  const active = context.pane?.active ?? true;
  const key = turn?.key;
  // Read for its changes: what the store holds for this turn is taken from it below.
  useSyncExternalStore(store.subscribe, store.getState);
  const runId = key ? store.runOf(key) : undefined;
  const liveEvents = key ? store.events(key) : NO_EVENTS;
  const liveFrames = key ? store.frames(key) : [];
  const isStreaming = turn?.streaming ?? false;
  useLiveTrace(store, runId, isStreaming, active);

  // Finished turns load their stored trace; live events show instantly meanwhile.
  const stored = useTurnTrace(turn?.turnId, {
    enabled: !isStreaming,
    placeholder: liveEvents,
    placeholderFrames: liveFrames,
  });
  const expired = isNoTrace(stored.error) && liveEvents.length === 0;
  const events =
    isStreaming || !turn?.turnId || expired ? liveEvents : (stored.data?.events ?? liveEvents);
  // The client's own copy is what actually arrived, malformed frames included, so it wins while this session has it.
  const frames = liveFrames.length > 0 ? liveFrames : (stored.data?.aguiFrames ?? []);
  // "None" means "not recorded" only once the server has answered and said so.
  const framesRecorded =
    liveFrames.length > 0 || isStreaming || !stored.isFetched || stored.data?.aguiFrames != null;

  // Shared with the chat: rewinding the trace also rewinds the selected answer.
  const timeTravel = useTimeTravel(events, key);
  const { dispatch } = timeTravel;
  // Only a cursor the person moved rewinds the chat; one that follows the newest step never does.
  const rewoundAt =
    turn && timeTravel.state.cursor !== 'live' && timeTravel.cursor < events.length
      ? timeTravel.cursor
      : null;
  const text = turn?.text ?? '';
  const sources = turn?.sources;
  const rewound = useMemo(
    () =>
      rewoundAt === null
        ? null
        : reconstructTurn(events, rewoundAt, { text, sources: [...(sources ?? [])] }),
    [events, rewoundAt, text, sources],
  );

  useEffect(() => {
    if (!key || !setTurnView || !rewound) return;
    setTurnView(key, {
      text: rewound.text,
      reasoning: { text: rewound.reasoning, ms: rewound.reasoningMs },
      toolCalls: rewound.toolCalls,
      sources: rewound.sources,
      cards: rewound.cards,
      label: rewound.stepLabel,
      note: rewound.textRecorded ? undefined : 'answer text not recorded for this turn',
      onExit: () => dispatch({ type: 'goLive' }),
    });
    // The turn is shown as it is again once the pane lets go of it: another step, another turn, or no pane.
    return () => setTurnView(key, null);
  }, [key, setTurnView, rewound, dispatch]);

  return (
    <MonitorPanel
      events={events}
      frames={frames}
      framesRecorded={framesRecorded}
      live={isStreaming}
      timeTravel={timeTravel}
      loading={!expired && stored.isFetching && events.length === 0}
      error={
        expired
          ? TRACE_EXPIRED
          : stored.isError && events.length === 0
            ? 'Could not load the trace for this turn.'
            : null
      }
    />
  );
}

const NO_EVENTS: TraceEvent[] = [];

/**
 * Follows a run's trace while the pane shows it: the trace API, polled while the run goes, and read once more when it
 * has ended or the pane opens on it, so the end of the trace is not lost. Closing the pane, another turn, or the run's
 * end aborts the read in flight.
 */
function useLiveTrace(
  store: MonitorStore,
  runId: string | undefined,
  live: boolean,
  active: boolean,
) {
  const api = useApi();
  useEffect(() => {
    if (!runId || !active) return;
    const controller = new AbortController();
    let reading: Promise<void> = Promise.resolve();
    // One read at a time, so each picks up where the last one stopped.
    const read = () =>
      (reading = reading.then(async () => {
        try {
          const body = await api<{ events: TraceEvent[] }>(
            `/api/runs/${encodeURIComponent(runId)}/trace?after=${store.lastSeq(runId)}`,
            { signal: controller.signal },
          );
          store.appendTrace(runId, body.events);
        } catch {
          // The monitor is a view; a missed read is caught up by the next one.
        }
      }));
    void read();
    const timer = live ? setInterval(() => void read(), TRACE_POLL_MS) : undefined;
    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [api, store, runId, live, active]);
}
