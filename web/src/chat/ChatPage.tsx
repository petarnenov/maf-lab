import { useQueryClient } from '@tanstack/react-query';
import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
} from 'react';
import { useNavigate, useParams } from 'react-router';
import { ApiError } from '../api/client';
import type { SourceRef } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { useConversation, useUserKey } from '../history/historyApi';
import { HistorySidebar } from '../history/HistorySidebar';
import { MonitorPanel } from '../monitor/MonitorPanel';
import { reasoningOf, reconstructTurn, type ReconstructedTurn } from '../monitor/reconstructTurn';
import { useTimeTravel } from '../monitor/useTimeTravel';
import { framesFor, traceFor } from '../monitor/traceReducer';
import { useTurnTrace } from '../monitor/useTurnTrace';
import type { AssistantTurn, ChatState } from './chatReducer';
import styles from './ChatPage.module.css';
import { idle, step, type RecallState } from './promptHistory';
import { SourcesPanel } from './SourcesPanel';
import { CodeSnippetsPanel } from './CodeSnippetsPanel';
import { codeSnippetsOf, snippetKey } from './codeSnippets';
import { CardView } from './cards/CardView';
import { langOf, words, type Lang } from './cards/format';
import { ConfirmationCard } from './ConfirmationCard';
import { Markdown } from './Markdown';
import { ToolCallCard } from './ToolCallCard';
import { TurnFeedback } from './TurnFeedback';
import { useChatStream } from './useChatStream';

export const TRACE_EXPIRED = 'Trace expired (kept 7 days).';

/** The right pane's two views (add-codebase-search). The monitor keeps its state while the other is shown. */
const PANE_TABS = [
  { id: 'scenes', label: 'Behind the scenes' },
  { id: 'code', label: 'Code snippets' },
] as const;
type PaneTab = (typeof PANE_TABS)[number]['id'];

export function ChatPage() {
  const { session } = useAuth();
  const { conversationId: routeId } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const userKey = useUserKey();
  const { state, send, reset, hydrate, answer, loadPending, toggleReasoning, setFocus } =
    useChatStream();
  const [draft, setDraft] = useState('');
  /** Assistant turn the monitor shows; null = follow the latest turn. */
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  /** The monitor is a panel a person opens and closes; it follows the latest turn while it is open. */
  const [monitorOpen, setMonitorOpen] = useState(true);
  /** Which view the right pane shows: the turn's trace, or the code that answers its question. */
  const [paneTab, setPaneTab] = useState<PaneTab>('scenes');
  /** The code snippet a source in an answer pointed at (add-codebase-domain). */
  const [highlight, setHighlight] = useState<string | null>(null);
  /** Turns whose code already switched the pane once: after that the person's choice of tab stands. */
  const autoSwitched = useRef(new Set<string>());
  const [historyCollapsed, setHistoryCollapsed] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const endRef = useRef<HTMLDivElement>(null);
  /** Recalling earlier prompts with the arrow keys (add-prompt-history-recall). */
  const recall = useRef<RecallState>(idle);
  const composerRef = useRef<HTMLTextAreaElement>(null);
  /** Set when a recall replaced the text: the caret goes to its end once the input shows it. */
  const caretToEnd = useRef(false);

  /**
   * The conversation the user is leaving. react-router applies a navigation a tick after it is asked for, so
   * without this the route still points at the old conversation while the state is already empty — and the cached
   * answer hydrates it straight back.
   */
  const leaving = useRef<string | null>(null);
  /** Set when a message creates a conversation, so only *that* id is written into the URL. */
  const urlNeedsId = useRef(false);

  // Opening /chat/:id loads the stored conversation unless it is already the one on screen, or being left.
  const needsLoad = Boolean(routeId) && routeId !== state.conversationId;
  const conversation = useConversation(routeId, needsLoad && Boolean(session));
  useEffect(() => {
    if (
      needsLoad &&
      routeId !== leaving.current &&
      conversation.data &&
      conversation.data.conversationId === routeId
    ) {
      hydrate(conversation.data);
      // The run that proposed it is gone; the proposal is not. Ask what this conversation is still waiting on.
      void loadPending(conversation.data.conversationId);
    }
  }, [needsLoad, conversation.data, routeId, hydrate, loadPending]);
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
    // Only once the route has actually moved: clearing it earlier would let the cache hydrate the old conversation.
    if (routeId !== leaving.current) {
      leaving.current = null;
    }
  }, [routeId, reset]);

  // A conversation the user just created by sending a message gives it an id: put it in the URL. Only then —
  // any other state holding an id the route does not is the user on their way out of it.
  useEffect(() => {
    if (urlNeedsId.current && !routeId && state.conversationId && !state.streaming) {
      urlNeedsId.current = false;
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

  // Another conversation on screen is another history: any recall of the last one ends.
  useEffect(() => {
    recall.current = idle;
  }, [state.conversationId, routeId]);

  useLayoutEffect(() => {
    if (!caretToEnd.current) return;
    caretToEnd.current = false;
    composerRef.current?.setSelectionRange(draft.length, draft.length);
  }, [draft]);

  /** ArrowUp/ArrowDown recall earlier prompts, but only where the caret has no line to move to. */
  function recallPrompt(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
    if (e.shiftKey || e.ctrlKey || e.altKey || e.metaKey || e.nativeEvent.isComposing) return;
    const { selectionStart, selectionEnd, value } = e.currentTarget;
    if (selectionStart !== selectionEnd) return;
    const up = e.key === 'ArrowUp';
    const lineAbove = selectionStart > 0 && value.lastIndexOf('\n', selectionStart - 1) !== -1;
    const lineBelow = value.indexOf('\n', selectionEnd) !== -1;
    if (up ? lineAbove : lineBelow) return;
    const history = state.turns.filter((t) => t.role === 'user').map((t) => t.text);
    const next = step(history, recall.current, up ? 'up' : 'down', draft);
    if (next === null) return;
    e.preventDefault();
    recall.current = next.state;
    caretToEnd.current = true;
    setDraft(next.text);
  }

  const assistantTurns = state.turns.filter((t): t is AssistantTurn => t.role === 'assistant');
  const latest = assistantTurns[assistantTurns.length - 1];

  // A finished live turn changes the history list (new conversation, last activity, turn count).
  const latestFinishedTurnId =
    latest && !latest.restored && latest.status !== 'streaming' ? latest.turnId : undefined;
  useEffect(() => {
    if (latestFinishedTurnId) void queryClient.invalidateQueries({ queryKey: ['conversations'] });
  }, [latestFinishedTurnId, queryClient]);
  const selected = assistantTurns.find((t) => t.id === selectedKey) ?? latest;
  const selectedIndex = selected ? state.turns.indexOf(selected) : -1;
  const usedCode = selected ? codeSnippetsOf(selected.sources) : [];
  // An answer given in this session that searched the codebase brings its code into view, once per turn; a turn
  // reopened from history does not move the pane.
  const streamingCodeTurn =
    selected && !selected.restored && usedCode.length > 0 ? selected.id : undefined;
  useEffect(() => {
    if (streamingCodeTurn && !autoSwitched.current.has(streamingCodeTurn)) {
      autoSwitched.current.add(streamingCodeTurn);
      setPaneTab('code');
    }
  }, [streamingCodeTurn]);
  const selectedQuestion =
    selectedIndex > 0 && state.turns[selectedIndex - 1].role === 'user'
      ? state.turns[selectedIndex - 1].text
      : '';
  const liveEvents = selected ? traceFor(state.traces, selected.id) : [];
  const liveFrames = selected ? framesFor(state.traces, selected.id) : [];
  const isStreaming = selected?.status === 'streaming';
  const traceExpired = selected?.traceAvailable === false;
  // Finished turns load their stored trace; live events show instantly meanwhile.
  const stored = useTurnTrace(selected?.turnId, {
    enabled: !isStreaming && !traceExpired,
    placeholder: liveEvents,
    placeholderFrames: liveFrames,
  });
  const events =
    isStreaming || !selected?.turnId || traceExpired
      ? liveEvents
      : (stored.data?.events ?? liveEvents);
  // The client's own copy is what actually arrived, malformed frames included, so it wins while this session has it.
  const frames = liveFrames.length > 0 ? liveFrames : (stored.data?.aguiFrames ?? []);
  // "None" means "not recorded" only once the server has answered and said so.
  const framesRecorded =
    liveFrames.length > 0 || isStreaming || !stored.isFetched || stored.data?.aguiFrames != null;
  // A turn that streamed in this session carries its own reasoning; a restored one reads it from its trace.
  const storedReasoning = reasoningOf(events);
  // Shared with the monitor: rewinding the trace also rewinds the selected answer in the chat.
  const timeTravel = useTimeTravel(events, selected?.id);
  const rewound: ReconstructedTurn | null =
    // Only a cursor the user moved rewinds the chat; one that follows the newest step never does.
    selected && timeTravel.state.cursor !== 'live' && timeTravel.cursor < events.length
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
    leaving.current = routeId ?? null;
    urlNeedsId.current = false;
    recall.current = idle;
    reset();
    void navigate('/chat');
  }

  function openConversation(id: string) {
    setSelectedKey(null);
    setDrawerOpen(false);
    leaving.current = null;
    if (id !== routeId) void navigate(`/chat/${id}`);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (state.streaming || loadingConversation || !draft.trim()) return;
    setSelectedKey(null);
    // Sending from /chat creates a conversation; its id belongs in the URL once the answer arrives.
    urlNeedsId.current = !routeId;
    void send(draft);
    setDraft('');
    recall.current = idle;
  }

  return (
    <div
      className={`${styles.page} ${historyCollapsed ? styles.historyCollapsed : ''} ${
        monitorOpen ? '' : styles.monitorClosed
      }`}
    >
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
                missing?” — or one that crosses into the portfolio domain: “Why did the fee on
                A-1042 go up this quarter? Did its AUM cross a tier?” — or ask about this lab’s
                code: “How does the code make a tool call idempotent?”
              </p>
            )}
            {!loadingConversation &&
              state.turns.map((turn, index) =>
                turn.role === 'user' ? (
                  <div key={turn.id} className={`${styles.bubble} ${styles.user}`}>
                    {turn.text}
                  </div>
                ) : (
                  <AssistantBubble
                    key={turn.id}
                    turn={turn}
                    question={
                      state.turns[index - 1]?.role === 'user' ? state.turns[index - 1].text : ''
                    }
                    conversationId={state.conversationId}
                    selected={monitorOpen && turn.id === selected?.id}
                    rewound={monitorOpen && turn.id === selected?.id ? rewound : null}
                    storedReasoning={turn.id === selected?.id ? storedReasoning : undefined}
                    onToggleReasoning={(open) => toggleReasoning(turn.id, open)}
                    onReturnToNow={() => timeTravel.dispatch({ type: 'goLive' })}
                    onShow={() => {
                      setSelectedKey(turn.id === latest?.id ? null : turn.id);
                      setMonitorOpen(true);
                    }}
                    onToggle={() => {
                      // The button on the turn already showing closes the panel; any other turn opens it there.
                      if (monitorOpen && turn.id === selected?.id) {
                        setMonitorOpen(false);
                        return;
                      }
                      setSelectedKey(turn.id === latest?.id ? null : turn.id);
                      setMonitorOpen(true);
                    }}
                    onAnswer={answer}
                    onOpenCode={(source) => {
                      setSelectedKey(turn.id === latest?.id ? null : turn.id);
                      setMonitorOpen(true);
                      setPaneTab('code');
                      setHighlight(
                        snippetKey({
                          path: source.sourcePath || source.docId,
                          startLine: source.startLine ?? null,
                        }),
                      );
                    }}
                    focus={state.focus?.accountId ?? null}
                    onFocus={(accountId) => setFocus({ accountId })}
                  />
                ),
              )}
            <div ref={endRef} />
          </div>
        )}

        {state.focus && (
          <FocusChip
            accountId={state.focus.accountId}
            lang={langOf(lastQuestion(state.turns))}
            onClear={() => setFocus(null)}
          />
        )}
        <form className={styles.composer} onSubmit={submit}>
          <textarea
            ref={composerRef}
            aria-label="Message"
            value={draft}
            rows={2}
            placeholder="Ask about billing, portfolios or the lab’s code: procedures, runs, fees, holdings, AUM, where something is implemented…"
            onChange={(e) => {
              // An edit makes the text a new draft: the next ArrowUp starts again from the newest prompt.
              recall.current = idle;
              setDraft(e.target.value);
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) submit(e);
              else recallPrompt(e);
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

      {monitorOpen && (
        <aside className={styles.monitorPane}>
          <div className={styles.paneTabs} role="tablist" aria-label="Right pane">
            {PANE_TABS.map((t) => (
              <button
                key={t.id}
                type="button"
                role="tab"
                id={`pane-tab-${t.id}`}
                aria-selected={paneTab === t.id}
                aria-controls={`pane-${t.id}`}
                className={`${styles.paneTab} ${paneTab === t.id ? styles.paneTabActive : ''}`}
                onClick={() => setPaneTab(t.id)}
              >
                {t.label}
                {t.id === 'code' && usedCode.length > 0 && (
                  <span className={styles.paneCount} aria-label={`${usedCode.length} used`}>
                    {usedCode.length}
                  </span>
                )}
              </button>
            ))}
          </div>
          <div
            className={styles.paneBody}
            role="tabpanel"
            id="pane-code"
            aria-labelledby="pane-tab-code"
            hidden={paneTab !== 'code'}
          >
            <CodeSnippetsPanel
              question={selectedQuestion}
              active={paneTab === 'code'}
              used={usedCode}
              highlight={highlight}
            />
          </div>
          <div
            className={styles.paneBody}
            role="tabpanel"
            id="pane-scenes"
            aria-labelledby="pane-tab-scenes"
            hidden={paneTab !== 'scenes'}
          >
            <MonitorPanel
              events={events}
              frames={frames}
              framesRecorded={framesRecorded}
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
          </div>
        </aside>
      )}
    </div>
  );
}

function AssistantBubble({
  turn,
  conversationId,
  selected,
  storedReasoning,
  onToggleReasoning,
  rewound,
  onReturnToNow,
  onShow,
  onToggle,
  onAnswer,
  onOpenCode,
  question,
  focus,
  onFocus,
}: {
  turn: AssistantTurn;
  /** The question this turn answers: its cards are written in that question's language. */
  question: string;
  conversationId?: string;
  selected: boolean;
  /** The reasoning out of this turn's stored trace, for a turn that did not stream in this session. */
  storedReasoning?: { text: string; ms?: number };
  onToggleReasoning: (open: boolean) => void;
  /** Set when time travel shows this turn at an earlier step. */
  rewound: ReconstructedTurn | null;
  onReturnToNow: () => void;
  /** Clicking the bubble shows this turn in the monitor; it never closes it. */
  onShow: () => void;
  /** The button opens the monitor on this turn, or closes it when this turn is the one showing. */
  onToggle: () => void;
  onAnswer: (adjustmentId: string, approve: boolean) => void;
  /** A code source was chosen: show it in the Code snippets tab (add-codebase-domain). */
  onOpenCode: (source: SourceRef) => void;
  /** The account in focus, and how to choose another (add-focus-state). */
  focus: string | null;
  onFocus: (accountId: string) => void;
}) {
  const toolCalls = rewound ? rewound.toolCalls : turn.toolCalls;
  const text = rewound ? rewound.text : turn.text;
  const sources = rewound ? rewound.sources : turn.sources;
  const cards = rewound ? rewound.cards : (turn.cards ?? []);
  // What the turn itself streamed wins; a restored turn has only what its trace kept.
  const reasoning = rewound
    ? { text: rewound.reasoning, ms: rewound.reasoningMs }
    : turn.reasoning
      ? { text: turn.reasoning, ms: turn.reasoningMs }
      : (storedReasoning ?? { text: '' });
  return (
    <div
      className={`${styles.bubble} ${styles.assistant} ${selected ? styles.selected : ''}`}
      data-testid="assistant-turn"
      data-selected={selected}
      onClick={onShow}
    >
      <button
        type="button"
        className={styles.traceButton}
        aria-pressed={selected}
        onClick={(e) => {
          e.stopPropagation();
          onToggle();
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
      {reasoning.text && (
        <ReasoningBlock
          text={reasoning.text}
          ms={reasoning.ms}
          // Until the answer starts the model is still working, whatever a stretch of reasoning has just done.
          thinking={text === ''}
          // The answer's first text closes the block; until someone says otherwise, that is what decides it.
          open={turn.reasoningOpen ?? text === ''}
          onToggle={onToggleReasoning}
        />
      )}
      {toolCalls.map((call) => (
        <ToolCallCard key={call.callId} call={call} />
      ))}
      {cards.map((card) => (
        <CardView
          key={card.messageId}
          card={card}
          question={question}
          focus={focus}
          // A rewound turn shows what was; choosing from it would act on the past.
          onFocus={rewound ? undefined : onFocus}
        />
      ))}
      {text ? (
        <div className={styles.text}>
          <Markdown text={text} />
        </div>
      ) : (
        (turn.status === 'streaming' || rewound) && <div className={styles.thinking}>Thinking…</div>
      )}
      {turn.error && (
        <div
          className={`${styles.error} ${styles[turn.errorKind ?? 'unexpected'] ?? ''}`}
          data-testid="turn-error"
          data-kind={turn.errorKind ?? 'unexpected'}
          role="alert"
        >
          {turn.error}
        </div>
      )}
      {turn.confirmation && !rewound && (
        <ConfirmationCard
          confirmation={turn.confirmation}
          state={turn.confirmationState ?? 'waiting'}
          onAnswer={(approve) => onAnswer(turn.confirmation!.adjustmentId, approve)}
        />
      )}
      <SourcesPanel sources={sources} onOpenCode={onOpenCode} />
      {turn.status !== 'streaming' && turn.turnId && conversationId && (
        <TurnFeedback
          hasConfirmation={turn.confirmation !== undefined}
          key={turn.turnId}
          conversationId={conversationId}
          turnId={turn.turnId}
          initialSent={turn.feedbackKinds}
        />
      )}
    </div>
  );
}

/** What the model thought on its way to the answer, set apart from what it said. */
function ReasoningBlock({
  text,
  ms,
  thinking,
  open,
  onToggle,
}: {
  text: string;
  ms?: number;
  thinking: boolean;
  open: boolean;
  onToggle: (open: boolean) => void;
}) {
  const label = thinking
    ? 'Thinking…'
    : ms === undefined
      ? 'Thought'
      : `Thought for ${(ms / 1000).toFixed(1)} s`;
  return (
    <div className={styles.reasoning} data-testid="reasoning">
      <button
        type="button"
        className={styles.reasoningSummary}
        aria-expanded={open}
        onClick={(e) => {
          e.stopPropagation();
          onToggle(!open);
        }}
      >
        <span aria-hidden="true">{open ? '▾' : '▸'}</span> {label}
      </button>
      {open && <div className={styles.reasoningText}>{text}</div>}
    </div>
  );
}

/** The question the latest turn answered: the chip speaks its language. */
function lastQuestion(turns: ChatState['turns']): string {
  for (let i = turns.length - 1; i >= 0; i--) {
    const turn = turns[i];
    if (turn.role === 'user') return turn.text;
  }
  return '';
}

/** The account the conversation is about (add-focus-state), with a way to let go of it. */
function FocusChip({
  accountId,
  lang,
  onClear,
}: {
  accountId: string;
  lang: Lang;
  onClear: () => void;
}) {
  const w = words[lang];
  return (
    <div className={styles.focusChip} data-testid="focus-chip">
      <span>
        {w.focus}: <strong>{accountId}</strong>
      </span>
      <button type="button" aria-label={w.clearFocus} onClick={onClear}>
        ✕
      </button>
    </div>
  );
}
