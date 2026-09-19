import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useAuth } from '../auth/useAuth';
import { MonitorPanel } from '../monitor/MonitorPanel';
import { reconstructTurn, type ReconstructedTurn } from '../monitor/reconstructTurn';
import { useTimeTravel } from '../monitor/useTimeTravel';
import { traceFor } from '../monitor/traceReducer';
import { useTurnTrace } from '../monitor/useTurnTrace';
import type { AssistantTurn } from './chatReducer';
import styles from './ChatPage.module.css';
import { SourcesPanel } from './SourcesPanel';
import { ToolCallCard } from './ToolCallCard';
import { TurnFeedback } from './TurnFeedback';
import { useChatStream } from './useChatStream';

export function ChatPage() {
  const { session } = useAuth();
  const { state, send, reset } = useChatStream();
  const [draft, setDraft] = useState('');
  /** Assistant turn the monitor shows; null = follow the latest turn. */
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'end' });
  }, [state.turns]);

  const assistantTurns = state.turns.filter((t): t is AssistantTurn => t.role === 'assistant');
  const latest = assistantTurns[assistantTurns.length - 1];
  const selected = assistantTurns.find((t) => t.id === selectedKey) ?? latest;
  const liveEvents = selected ? traceFor(state.traces, selected.id) : [];
  const isStreaming = selected?.status === 'streaming';
  // Finished turns load their stored trace; live events show instantly meanwhile.
  const stored = useTurnTrace(selected?.turnId, { enabled: !isStreaming, placeholder: liveEvents });
  const events =
    isStreaming || !selected?.turnId ? liveEvents : (stored.data?.events ?? liveEvents);
  // Shared with the monitor: rewinding the trace also rewinds the selected answer in the chat.
  const timeTravel = useTimeTravel(events, selected?.id);
  const rewound: ReconstructedTurn | null =
    selected && events.length > 0 && timeTravel.cursor < events.length
      ? reconstructTurn(events, timeTravel.cursor, {
          text: selected.text,
          sources: selected.sources,
        })
      : null;

  if (!session) {
    return <p className={styles.empty}>Pick a dev persona in the header to start chatting.</p>;
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (state.streaming || !draft.trim()) return;
    setSelectedKey(null);
    void send(draft);
    setDraft('');
  }

  return (
    <div className={styles.page}>
      <div className={styles.chatPane}>
        <div className={styles.toolbar}>
          <h1 className={styles.heading}>Chat</h1>
          <button
            type="button"
            onClick={() => {
              setSelectedKey(null);
              reset();
            }}
            disabled={state.turns.length === 0}
          >
            New conversation
          </button>
        </div>

        <div className={styles.transcript} aria-live="polite">
          {state.turns.length === 0 && (
            <p className={styles.empty}>
              Ask a procedural question, e.g. “What is the procedure when a fee schedule is
              missing?”
            </p>
          )}
          {state.turns.map((turn) =>
            turn.role === 'user' ? (
              <div key={turn.id} className={`${styles.bubble} ${styles.user}`}>
                {turn.text}
              </div>
            ) : (
              <AssistantBubble
                key={turn.id}
                turn={turn}
                conversationId={state.conversationId}
                selected={turn.id === selected?.id}
                rewound={turn.id === selected?.id ? rewound : null}
                onReturnToNow={() => timeTravel.dispatch({ type: 'goLive' })}
                onSelect={() => setSelectedKey(turn.id === latest?.id ? null : turn.id)}
              />
            ),
          )}
          <div ref={endRef} />
        </div>

        <form className={styles.composer} onSubmit={submit}>
          <textarea
            aria-label="Message"
            value={draft}
            rows={2}
            placeholder="Ask about billing procedures, runs, or fee schedules…"
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) submit(e);
            }}
          />
          <button type="submit" disabled={state.streaming || !draft.trim()}>
            {state.streaming ? 'Answering…' : 'Send'}
          </button>
        </form>
      </div>

      <aside className={styles.monitorPane}>
        <MonitorPanel
          events={events}
          live={isStreaming}
          timeTravel={timeTravel}
          loading={stored.isFetching && events.length === 0}
          error={
            stored.isError && events.length === 0 ? 'Could not load the trace for this turn.' : null
          }
        />
      </aside>
    </div>
  );
}

function AssistantBubble({
  turn,
  conversationId,
  selected,
  rewound,
  onReturnToNow,
  onSelect,
}: {
  turn: AssistantTurn;
  conversationId?: string;
  selected: boolean;
  /** Set when time travel shows this turn at an earlier step. */
  rewound: ReconstructedTurn | null;
  onReturnToNow: () => void;
  onSelect: () => void;
}) {
  const toolCalls = rewound ? rewound.toolCalls : turn.toolCalls;
  const text = rewound ? rewound.text : turn.text;
  const sources = rewound ? rewound.sources : turn.sources;
  return (
    <div
      className={`${styles.bubble} ${styles.assistant} ${selected ? styles.selected : ''}`}
      data-testid="assistant-turn"
      data-selected={selected}
      onClick={onSelect}
    >
      <button
        type="button"
        className={styles.traceButton}
        aria-pressed={selected}
        onClick={(e) => {
          e.stopPropagation();
          onSelect();
        }}
      >
        {selected ? 'Showing behind the scenes' : 'Behind the scenes'}
      </button>
      {rewound && (
        <div className={styles.rewindBanner} role="status" data-testid="rewind-banner">
          <span>⏪ Viewing {rewound.stepLabel}</span>
          {!rewound.textRecorded && (
            <span className={styles.rewindNote}>answer text not recorded for this turn</span>
          )}
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onReturnToNow();
            }}
          >
            Return to now
          </button>
        </div>
      )}
      {toolCalls.map((call) => (
        <ToolCallCard key={call.callId} call={call} />
      ))}
      {text ? (
        <div className={styles.text}>{text}</div>
      ) : (
        (turn.status === 'streaming' || rewound) && <div className={styles.thinking}>Thinking…</div>
      )}
      {turn.error && (
        <div className={styles.error} role="alert">
          {turn.error}
        </div>
      )}
      <SourcesPanel sources={sources} />
      {turn.status !== 'streaming' && turn.turnId && conversationId && (
        <TurnFeedback conversationId={conversationId} turnId={turn.turnId} />
      )}
    </div>
  );
}
