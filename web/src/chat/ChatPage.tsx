import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { useConversation, useUserKey } from '../history/historyApi';
import { HistorySidebar } from '../history/HistorySidebar';
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

export const TRACE_EXPIRED = 'Trace expired (kept 7 days).';

export function ChatPage() {
  const { session } = useAuth();
  const { conversationId: routeId } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const userKey = useUserKey();
  const { state, send, reset, hydrate } = useChatStream();
  const [draft, setDraft] = useState('');
  /** Assistant turn the monitor shows; null = follow the latest turn. */
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [historyCollapsed, setHistoryCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const endRef = useRef<HTMLDivElement>(null);

  // Opening /chat/:id loads the stored conversation unless it is already the one on screen.
  const needsLoad = Boolean(routeId) && routeId !== state.conversationId;
  const conversation = useConversation(routeId, needsLoad && Boolean(session));
  useEffect(() => {
    if (needsLoad && conversation.data && conversation.data.conversationId === routeId) {
      hydrate(conversation.data);
    }
  }, [needsLoad, conversation.data, routeId, hydrate]);
  const notFound =
    needsLoad && conversation.error instanceof ApiError && conversation.error.status === 404;

  // Leaving a conversation for /chat (e.g. "New conversation", browser back) starts fresh.
  const previousRoute = useRef(routeId);
  useEffect(() => {
    if (previousRoute.current && !routeId) {
      setSelectedKey(null);
      reset();
    }
    previousRoute.current = routeId;
  }, [routeId, reset]);

  // The first answer of a new conversation gives it an id: put it in the URL.
  useEffect(() => {
    if (!routeId && state.conversationId && !state.streaming) {
      void navigate(`/chat/${state.conversationId}`, { replace: true });
    }
  }, [routeId, state.conversationId, state.streaming, navigate]);

  // Another persona must never see this user's conversation.
  const previousUser = useRef(userKey);
  useEffect(() => {
    if (previousUser.current !== userKey) {
      previousUser.current = userKey;
      setSelectedKey(null);
      reset();
      if (routeId) void navigate('/chat', { replace: true });
    }
  }, [userKey, routeId, reset, navigate]);

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'end' });
  }, [state.turns]);

  const assistantTurns = state.turns.filter((t): t is AssistantTurn => t.role === 'assistant');
  const latest = assistantTurns[assistantTurns.length - 1];

  // A finished live turn changes the history list (new conversation, last activity, turn count).
  const latestFinishedTurnId =
    latest && !latest.restored && latest.status !== 'streaming' ? latest.turnId : undefined;
  useEffect(() => {
    if (latestFinishedTurnId) void queryClient.invalidateQueries({ queryKey: ['conversations'] });
  }, [latestFinishedTurnId, queryClient]);
  const selected = assistantTurns.find((t) => t.id === selectedKey) ?? latest;
  const liveEvents = selected ? traceFor(state.traces, selected.id) : [];
  const isStreaming = selected?.status === 'streaming';
  const traceExpired = selected?.traceAvailable === false;
  // Finished turns load their stored trace; live events show instantly meanwhile.
  const stored = useTurnTrace(selected?.turnId, {
    enabled: !isStreaming && !traceExpired,
    placeholder: liveEvents,
  });
  const events =
    isStreaming || !selected?.turnId || traceExpired
      ? liveEvents
      : (stored.data?.events ?? liveEvents);
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

  const loadingConversation = needsLoad && !notFound;

  function startNew() {
    setSelectedKey(null);
    setDrawerOpen(false);
    reset();
    void navigate('/chat');
  }

  function openConversation(id: string) {
    setSelectedKey(null);
    setDrawerOpen(false);
    if (id !== routeId) void navigate(`/chat/${id}`);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (state.streaming || loadingConversation || !draft.trim()) return;
    setSelectedKey(null);
    void send(draft);
    setDraft('');
  }

  return (
    <div className={`${styles.page} ${historyCollapsed ? styles.historyCollapsed : ''}`}>
      <div className={`${styles.historyPane} ${drawerOpen ? styles.drawerOpen : ''}`}>
        <HistorySidebar
          activeId={state.conversationId ?? routeId}
          onSelect={openConversation}
          onNew={startNew}
          onDeleted={(id) => {
            if (id === (state.conversationId ?? routeId)) startNew();
          }}
          collapsed={historyCollapsed && !drawerOpen}
          onToggleCollapsed={() => setHistoryCollapsed((c) => !c)}
        />
      </div>
      {drawerOpen && (
        <button
          type="button"
          className={styles.drawerBackdrop}
          aria-label="Close history"
          onClick={() => setDrawerOpen(false)}
        />
      )}

      <div className={styles.chatPane}>
        <div className={styles.toolbar}>
          <button
            type="button"
            className={styles.drawerToggle}
            aria-label="Show history"
            aria-expanded={drawerOpen}
            onClick={() => setDrawerOpen((o) => !o)}
          >
            ☰ History
          </button>
          <h1 className={styles.heading}>Chat</h1>
          <button type="button" onClick={startNew} disabled={state.turns.length === 0 && !routeId}>
            New conversation
          </button>
        </div>

        {notFound ? (
          <div className={styles.notFound} role="alert">
            <p>Conversation not found.</p>
            <p className={styles.muted}>It may have been deleted, or it belongs to another user.</p>
            <button type="button" onClick={startNew}>
              Start a new conversation
            </button>
          </div>
        ) : (
          <div className={styles.transcript} aria-live="polite">
            {loadingConversation && <p className={styles.empty}>Loading conversation…</p>}
            {!loadingConversation && state.turns.length === 0 && (
              <p className={styles.empty}>
                Ask a procedural question, e.g. “What is the procedure when a fee schedule is
                missing?”
              </p>
            )}
            {!loadingConversation &&
              state.turns.map((turn) =>
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
        )}

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
          <button
            type="submit"
            disabled={state.streaming || loadingConversation || notFound || !draft.trim()}
          >
            {state.streaming ? 'Answering…' : 'Send'}
          </button>
        </form>
      </div>

      <aside className={styles.monitorPane}>
        <MonitorPanel
          events={events}
          live={isStreaming}
          timeTravel={timeTravel}
          loading={!traceExpired && stored.isFetching && events.length === 0}
          error={
            traceExpired
              ? TRACE_EXPIRED
              : stored.isError && events.length === 0
                ? 'Could not load the trace for this turn.'
                : null
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
        <TurnFeedback
          key={turn.turnId}
          conversationId={conversationId}
          turnId={turn.turnId}
          initialSent={turn.feedbackKinds}
        />
      )}
    </div>
  );
}
