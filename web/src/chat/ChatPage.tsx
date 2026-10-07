import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
} from 'react';
import { useNavigate, useParams } from 'react-router';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { useConversation, useUserKey } from '../history/historyApi';
import type { AssistantTurn, ChatState } from './chatReducer';
import styles from './ChatPage.module.css';
import { idle, step, type RecallState } from './promptHistory';
import { SourcesPanel } from './SourcesPanel';
import { CardView } from './cards/CardView';
import { langOf, type Lang } from '../shared/format';
import { ConfirmationCard } from './ConfirmationCard';
import { Markdown } from './Markdown';
import { ToolCallCard } from './ToolCallCard';
import { TurnFeedback } from './TurnFeedback';
import { useChatStream } from './useChatStream';
import { stepLabel } from './runStep';
import { Progress } from '../shared/Progress';
import { StopHint } from '../shared/StopHint';
import { useEscToStop } from '../shared/useEscToStop';
import type { ChatContext, ChatTurnView, TurnViewOverride } from '../plugins/api';
import { useDomains, usePlugins } from '../plugins/context';
import { PluginBoundary } from '../plugins/PluginBoundary';
import { contributions } from '../plugins/registry';

/** A plugin's pane, by the plugin and the pane's own id (introduce-plugins decision 8). */
type PaneTab = `plugin:${string}:${string}`;

export function ChatPage() {
  const { session } = useAuth();
  const { conversationId: routeId } = useParams();
  const navigate = useNavigate();
  const userKey = useUserKey();
  const plugins = usePlugins();
  const pluginPanes = contributions(plugins, 'chatPanes');
  // The sidebar's chrome is the chat's; it is there only while a plugin in use fills it (the conversation list).
  const sidebars = contributions(plugins, 'chatSidebars');
  const sidebarLabel = sidebars[0]?.item.label;
  // With no domain in use the assistant answers nothing (introduce-plugins 5h): say so before anyone types.
  const domains = useDomains();
  const noDomain = domains?.length === 0;
  const scopes = listed((domains ?? []).flatMap((d) => (d.scope.en ? [d.scope.en] : [])));
  const { state, send, cancel, reset, hydrate, answer, loadPending, toggleReasoning, setFocus } =
    useChatStream();
  const [draft, setDraft] = useState('');
  /** Assistant turn the side pane shows; null = follow the latest turn. */
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  /** The side pane is a panel a person opens and closes; it follows the latest turn while it is open. */
  const [paneOpen, setPaneOpen] = useState(true);
  /** Which plugin pane the side pane shows; the first one until a person or a plugin picks another. */
  const [paneTab, setPaneTab] = useState<PaneTab | null>(null);
  const paneIds = pluginPanes.map(({ plugin, item }) => `plugin:${plugin}:${item.id}` as const);
  const activePane = paneTab && paneIds.includes(paneTab) ? paneTab : paneIds[0];
  const activePaneLabel = pluginPanes[paneIds.indexOf(activePane)]?.item.label;
  // With no plugin pane in use there is no side pane at all.
  const sidePane = paneOpen && paneIds.length > 0;
  /** The value each pane was last opened with (a source to show, say), by pane id. */
  const [paneValues, setPaneValues] = useState<Record<string, unknown>>({});
  /** Opens a plugin's pane by its id, with an optional value for it. */
  const openPane = (id: string, value?: unknown) => {
    const plugin = pluginPanes.find(({ item }) => item.id === id);
    if (!plugin) return;
    setPaneTab(`plugin:${plugin.plugin}:${plugin.item.id}`);
    if (value !== undefined) setPaneValues((values) => ({ ...values, [id]: value }));
    setPaneOpen(true);
  };
  /** Turns a pane shows as they were at an earlier step (the monitor's time travel), by turn key. */
  const [turnViews, setTurnViews] = useState<Record<string, TurnViewOverride>>({});
  const setTurnView = useCallback((turnKey: string, view: TurnViewOverride | null) => {
    setTurnViews((views) => {
      if ((views[turnKey] ?? null) === view) return views;
      if (view) return { ...views, [turnKey]: view };
      return Object.fromEntries(Object.entries(views).filter(([key]) => key !== turnKey));
    });
  }, []);
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

  // Esc stops the answer in progress, wherever the focus is on this page (stop-anything).
  useEscToStop(state.streaming, cancel);

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
  const selected = assistantTurns.find((t) => t.id === selectedKey) ?? latest;
  const selectedIndex = selected ? state.turns.indexOf(selected) : -1;
  const selectedQuestion =
    selectedIndex > 0 && state.turns[selectedIndex - 1].role === 'user'
      ? state.turns[selectedIndex - 1].text
      : '';
  /** A turn as a plugin's pane or source action sees it: read-only. */
  const turnView = (turn: AssistantTurn, question: string): ChatTurnView => ({
    key: turn.id,
    question,
    sources: turn.sources,
    restored: turn.restored === true,
    turnId: turn.turnId,
    streaming: turn.status === 'streaming',
    text: turn.text,
  });
  /** What a plugin's pane is told about the chat: the selected turn, and how it opens a pane. */
  const chatContext: ChatContext = {
    conversationId: state.conversationId ?? routeId,
    turn: selected ? turnView(selected, selectedQuestion) : undefined,
    openPane,
    setTurnView,
  };

  if (!session) {
    return <p className={styles.empty}>Pick a dev persona in the header to start chatting.</p>;
  }

  const loadingConversation = needsLoad && !notFound;
  // In the narrow-screen drawer the sidebar is always open.
  const collapsed = historyCollapsed && !drawerOpen;

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
    if (noDomain || state.streaming || loadingConversation || !draft.trim()) return;
    setSelectedKey(null);
    // Sending from /chat creates a conversation; its id belongs in the URL once the answer arrives.
    urlNeedsId.current = !routeId;
    void send(draft);
    setDraft('');
    recall.current = idle;
  }

  return (
    <div
      className={`${styles.page} ${
        !sidebarLabel ? styles.noSidebar : historyCollapsed ? styles.historyCollapsed : ''
      } ${sidePane ? '' : styles.monitorClosed}`}
    >
      {sidebarLabel && (
        <div className={`${styles.historyPane} ${drawerOpen ? styles.drawerOpen : ''}`}>
          <nav
            id="chat-sidebar"
            className={`${styles.sidebar} ${collapsed ? styles.rail : ''}`}
            aria-label={sidebarLabel}
          >
            {collapsed ? (
              <button
                type="button"
                className={styles.iconButton}
                aria-label={`Expand ${sidebarLabel.toLowerCase()}`}
                onClick={() => setHistoryCollapsed(false)}
              >
                ☰
              </button>
            ) : (
              <div className={styles.sidebarHeader}>
                <h2 className={styles.sidebarHeading}>{sidebarLabel}</h2>
                <button
                  type="button"
                  className={styles.iconButton}
                  aria-label={`Collapse ${sidebarLabel.toLowerCase()}`}
                  onClick={() => setHistoryCollapsed(true)}
                >
                  ⟨
                </button>
              </div>
            )}
            {/* Mounted while collapsed, so a sidebar keeps its state (the search it holds). */}
            {sidebars.map(({ plugin, item }) => (
              <div key={`${plugin}:${item.id}`} className={styles.sidebarBody} hidden={collapsed}>
                <PluginBoundary plugin={plugin}>
                  {item.render({ ...chatContext, openConversation, startNew })}
                </PluginBoundary>
              </div>
            ))}
          </nav>
        </div>
      )}
      {sidebarLabel && drawerOpen && (
        <button
          type="button"
          className={styles.drawerBackdrop}
          aria-label={`Close ${sidebarLabel.toLowerCase()}`}
          onClick={() => setDrawerOpen(false)}
        />
      )}

      <div className={styles.chatPane}>
        <div className={styles.toolbar}>
          {sidebarLabel && (
            <button
              type="button"
              className={styles.drawerToggle}
              aria-label={`Show ${sidebarLabel.toLowerCase()}`}
              aria-expanded={drawerOpen}
              aria-controls="chat-sidebar"
              onClick={() => setDrawerOpen((o) => !o)}
            >
              ☰ {sidebarLabel}
            </button>
          )}
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
              <p
                id={noDomain ? 'no-domain-notice' : undefined}
                className={styles.empty}
                role={noDomain ? 'status' : undefined}
              >
                {noDomain
                  ? NO_DOMAIN
                  : scopes
                    ? `Ask a question about ${scopes}.`
                    : 'Ask a question to start.'}
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
                    selected={sidePane && turn.id === selected?.id}
                    paneLabel={activePaneLabel}
                    override={
                      sidePane && turn.id === selected?.id ? (turnViews[turn.id] ?? null) : null
                    }
                    onToggleReasoning={(open) => toggleReasoning(turn.id, open)}
                    onShow={() => {
                      setSelectedKey(turn.id === latest?.id ? null : turn.id);
                      setPaneOpen(true);
                    }}
                    onToggle={() => {
                      // The button on the turn already showing closes the pane; any other turn opens it there.
                      if (sidePane && turn.id === selected?.id) {
                        setPaneOpen(false);
                        return;
                      }
                      setSelectedKey(turn.id === latest?.id ? null : turn.id);
                      setPaneOpen(true);
                    }}
                    onAnswer={answer}
                    sourceContext={{
                      conversationId: state.conversationId ?? routeId,
                      turn: turnView(
                        turn,
                        state.turns[index - 1]?.role === 'user' ? state.turns[index - 1].text : '',
                      ),
                      // A source opened from a turn shows that turn beside its pane.
                      openPane: (id, value) => {
                        setSelectedKey(turn.id === latest?.id ? null : turn.id);
                        openPane(id, value);
                      },
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
            disabled={noDomain}
            aria-describedby={noDomain ? 'no-domain-notice' : undefined}
            placeholder={
              noDomain
                ? 'No domain is enabled'
                : scopes
                  ? `Ask about ${scopes}…`
                  : 'Ask a question…'
            }
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
            disabled={
              noDomain || state.streaming || loadingConversation || notFound || !draft.trim()
            }
          >
            {state.streaming ? 'Answering…' : 'Send'}
          </button>
        </form>
      </div>

      {sidePane && (
        <aside className={styles.monitorPane}>
          <div className={styles.paneTabs} role="tablist" aria-label="Right pane">
            {pluginPanes.map(({ plugin, item }) => {
              const id = `plugin:${plugin}:${item.id}` as const;
              const badge = item.badge?.(chatContext);
              return (
                <button
                  key={id}
                  type="button"
                  role="tab"
                  id={`pane-tab-${id}`}
                  aria-selected={activePane === id}
                  aria-controls={`pane-${id}`}
                  className={`${styles.paneTab} ${activePane === id ? styles.paneTabActive : ''}`}
                  onClick={() => setPaneTab(id)}
                >
                  {item.label}
                  {badge ? (
                    <span className={styles.paneCount} aria-label={`${badge} items`}>
                      {badge}
                    </span>
                  ) : null}
                </button>
              );
            })}
          </div>
          {pluginPanes.map(({ plugin, item }) => {
            const id = `plugin:${plugin}:${item.id}`;
            return (
              <div
                key={id}
                className={styles.paneBody}
                role="tabpanel"
                id={`pane-${id}`}
                aria-labelledby={`pane-tab-${id}`}
                hidden={activePane !== id}
              >
                {/* Mounted while hidden, so a pane keeps its state and can open itself (introduce-plugins 5.2). */}
                <PluginBoundary plugin={plugin}>
                  {item.render({
                    ...chatContext,
                    pane: { active: activePane === id, value: paneValues[item.id] },
                  })}
                </PluginBoundary>
              </div>
            );
          })}
        </aside>
      )}
    </div>
  );
}

function AssistantBubble({
  turn,
  conversationId,
  selected,
  paneLabel,
  onToggleReasoning,
  override,
  onShow,
  onToggle,
  onAnswer,
  sourceContext,
  question,
  focus,
  onFocus,
}: {
  turn: AssistantTurn;
  /** The question this turn answers: its cards are written in that question's language. */
  question: string;
  conversationId?: string;
  selected: boolean;
  /** The side pane's label, for the button that opens it on this turn; none when no plugin has a pane. */
  paneLabel?: string;
  onToggleReasoning: (open: boolean) => void;
  /** Set when a pane shows this turn as it was at an earlier step; the bubble is read-only meanwhile. */
  override: TurnViewOverride | null;
  /** Clicking the bubble shows this turn in the side pane; it never closes it. */
  onShow: () => void;
  /** The button opens the side pane on this turn, or closes it when this turn is the one showing. */
  onToggle: () => void;
  onAnswer: (writeId: string, approve: boolean) => void;
  /** What a source's action is told: this turn, and how to open a pane beside it (introduce-plugins 5.2). */
  sourceContext: ChatContext;
  /** The account in focus, and how to choose another (add-focus-state). */
  focus: string | null;
  onFocus: (accountId: string) => void;
}) {
  const rewound = override;
  const toolCalls = rewound ? rewound.toolCalls : turn.toolCalls;
  const text = rewound ? rewound.text : turn.text;
  const sources = rewound ? rewound.sources : turn.sources;
  const cards = rewound ? rewound.cards : (turn.cards ?? []);
  const reasoning = rewound
    ? (rewound.reasoning ?? { text: '' })
    : { text: turn.reasoning, ms: turn.reasoningMs };
  return (
    <div
      className={`${styles.bubble} ${styles.assistant} ${selected ? styles.selected : ''}`}
      data-testid="assistant-turn"
      data-selected={selected}
      onClick={onShow}
    >
      {paneLabel && (
        <button
          type="button"
          className={styles.traceButton}
          aria-pressed={selected}
          onClick={(e) => {
            e.stopPropagation();
            onToggle();
          }}
        >
          {selected ? `Showing ${paneLabel.toLowerCase()}` : paneLabel}
        </button>
      )}
      {rewound && (
        <div className={styles.rewindBanner} role="status" data-testid="rewind-banner">
          <span>⏪ Viewing {rewound.label}</span>
          {rewound.note && <span className={styles.rewindNote}>{rewound.note}</span>}
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              rewound.onExit();
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
        rewound && <div className={styles.thinking}>Thinking…</div>
      )}
      {/* The run as it goes, in the page's theme: what its open step says it is doing (progress-feedback). */}
      {turn.status === 'streaming' && !rewound && (!text || turn.step) && (
        <div className={styles.thinking} data-testid="run-progress">
          <Progress label={stepLabel(turn.step)} />
        </div>
      )}
      {/* For as long as the run goes, however far its answer has got: Esc stops it — or the stop is on its way. */}
      {turn.status === 'streaming' && !rewound && <StopHint stopping={Boolean(turn.stopping)} />}
      {turn.stopped && (
        <div className={styles.stopped} data-testid="turn-stopped" role="status">
          Stopped.
        </div>
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
          onAnswer={(approve) => onAnswer(turn.confirmation!.writeId, approve)}
        />
      )}
      <SourcesPanel sources={sources} context={sourceContext} />
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

/** The focus chip's words, in each language. */
const focusWords = {
  bg: { focus: 'Фокус', clearFocus: 'Изчисти фокуса' },
  en: { focus: 'Focus', clearFocus: 'Clear focus' },
} as const;

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
  const w = focusWords[lang];
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

/** The api's own reply to a turn with no domain in use (NoDomain.ReplyEnglish), shown before the first message. */
export const NO_DOMAIN =
  'No domain is enabled for this assistant yet, so it cannot answer questions. Your administrator can enable one.';

/** "a", "a and b", "a, b and c". */
function listed(items: readonly string[]): string {
  return items.length <= 1
    ? (items[0] ?? '')
    : `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;
}
