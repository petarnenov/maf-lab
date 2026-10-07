import type { ComponentType, ReactNode } from 'react';
import type { DataCard, SourceRef } from '../api/types';
import type { ToolCallView } from '../chat/chatReducer';

/** A source of an answer, as the chat holds it: what a plugin's source action and panes read. */
export type { SourceRef };
/** A data card and a tool call as the chat shows them: what a turn-view override carries. */
export type { DataCard, ToolCallView };

/** The error the api client throws, with its HTTP status: how a plugin tells a 404 from a failure. */
export { ApiError } from '../api/client';

/** The signed-in user's api client: a plugin's own routes, called as the user, with the core's errors and stop. */
export { useApi } from '../auth/useAuth';

/** The signed-in persona as a query key's part: a plugin's cached reads never show one user's data to another. */
export { useUserKey } from '../history/historyApi';

/**
 * The web plugin API (introduce-plugins decision 8): what a plugin's `web/index.ts` may contribute, through
 * `definePlugin`. The core never imports a plugin; a plugin imports only this file and `web/src/shared/` (ESLint
 * enforces both directions). Every field is optional and its own small type (interface segregation), so a plugin
 * declares exactly what it gives.
 */

/** A page of its own. `admin` puts it behind the core's admin guard. */
export interface PluginRoute {
  path: string;
  element: ReactNode;
  admin?: boolean;
}

/** A link in the main navigation: a route of the app (`to`) or another site (`href`, opened in a new tab). */
export interface PluginNavLink {
  label: string;
  to?: string;
  href?: string;
  adminOnly?: boolean;
}

/** The assistant turn the chat has selected, read-only: what a pane shows next to it. */
export interface ChatTurnView {
  /** Stable for the turn: a pane may remember it did something once per turn. */
  key: string;
  /** The question this turn answers. */
  question: string;
  sources: readonly SourceRef[];
  /** True for a turn reopened from history rather than answered in this session. */
  restored: boolean;
  /** The turn's server id, once the run has started; what a plugin's own routes know it by. */
  turnId?: string;
  /** True while the turn's run is going. */
  streaming: boolean;
  /** The answer as it stands. */
  text: string;
}

/**
 * How a plugin shows a turn as it was at an earlier step (time travel): the core renders it in the turn's bubble with a
 * banner saying `label` and a way back (`onExit`), and keeps the bubble read-only while it is shown.
 */
export interface TurnViewOverride {
  text: string;
  reasoning?: { text: string; ms?: number };
  toolCalls: readonly ToolCallView[];
  sources: readonly SourceRef[];
  cards: readonly DataCard[];
  /** What the banner says, e.g. "step 4 of 12". */
  label: string;
  /** A second line for the banner, when the view is partial. */
  note?: string;
  onExit: () => void;
}

/** What the chat gives a pane or a sidebar about the conversation on screen. */
export interface ChatContext {
  conversationId?: string;
  /** The selected assistant turn, when there is one. */
  turn?: ChatTurnView;
  /** Opens one of the chat's panes by id, with an optional value for it (a source to show, say). */
  openPane: (id: string, value?: unknown) => void;
  /** Shows a turn as it was at an earlier step, or (null) as it is: state the chat owns, set by a pane. */
  setTurnView?: (turnKey: string, view: TurnViewOverride | null) => void;
  /** Set for a sidebar's render: opens one of the user's conversations, as the chat does it. */
  openConversation?: (conversationId: string) => void;
  /** Set for a sidebar's render: leaves the conversation on screen for a new one, as the chat's own header does. */
  startNew?: () => void;
  /**
   * For a pane's own render only: whether it is the pane showing, and `value`, whatever that pane's own last `openPane`
   * call passed (the core never reads it). A pane stays mounted while hidden, so it keeps its state and can open itself.
   */
  pane?: { active: boolean; value?: unknown };
}

/** A tab in the chat's side pane. The pane is absent when no plugin contributes one. */
export interface PluginChatPane {
  id: string;
  label: string;
  badge?: (context: ChatContext) => number | undefined;
  render: (context: ChatContext) => ReactNode;
}

/**
 * A sidebar left of the chat (the conversation list is one). Content only: the chat draws the sidebar's landmark,
 * heading, collapse and narrow-screen drawer, all named after `label`, and keeps the content mounted while collapsed.
 * Several sidebars stack in the one slot.
 */
export interface PluginChatSidebar {
  id: string;
  label: string;
  render: (context: ChatContext) => ReactNode;
}

/** A button under an assistant's answer. */
export interface PluginTurnAction {
  id: string;
  label: string;
  onClick: (context: ChatContext, turnKey: string) => void;
}

/** What opening a source of a kind does (a code source opens the code pane, say). */
export interface PluginSourceAction {
  kind: string;
  /** The action's name, as the source's button says it ("show in Code snippets"). */
  label: string;
  onOpen: (source: SourceRef, context: ChatContext) => void;
}

/**
 * How a tool call reads in the chat, by tool name: from what the chat knows of the call — whether it is still running
 * and a one-line summary of its arguments. A tool no plugin labels reads with the core's generic label.
 */
export type PluginToolLabels = Record<
  string,
  (call: { running: boolean; argumentSummary: string }) => string
>;

/** What a data card's renderer is given: the card's content, and the turn it answers (extract-portfolio). */
export interface PluginCardProps {
  content: unknown;
  /** The question the card answers, so it can write in that question's language. */
  question: string;
  /** The entity in focus, so a card can say it is the one (add-focus-state). */
  focus?: string | null;
  /** Puts an entity in focus; absent where choosing makes no sense (time travel). */
  onFocus?: (id: string) => void;
}

/** A data card's renderer, by its AG-UI activity type. */
export type PluginCards = Record<string, ComponentType<PluginCardProps>>;

/**
 * How a plugin shows the summary of a write waiting for a person, by the write tool's name. It overrides the card's
 * rendering from the summary's schema for that tool; display only — the card keeps the question and the two answers.
 */
export type PluginConfirmations = Record<
  string,
  ComponentType<{ summary: unknown; schema: unknown }>
>;

/** A tab of the monitor (the monitor plugin owns the panel; others add tabs to it). */
export interface PluginMonitorTab {
  id: string;
  label: string;
  render: (context: { events: readonly unknown[] }) => ReactNode;
}

/**
 * An observer of chat runs: how a plugin sees a run's AG-UI events — the official event objects CopilotKit delivered —
 * without the core knowing why. `turnKey` is the chat's assistant turn the run answers into.
 */
export interface PluginRunObserver {
  onRunStart?: (run: { runId: string; turnKey: string; conversationId?: string }) => void;
  onEvent?: (runId: string, event: { type: string } & Record<string, unknown>) => void;
  onRunEnd?: (runId: string, outcome: 'finished' | 'stopped' | 'failed') => void;
}

/** A panel of the feedback review queue, for one reviewed turn (another user's, by its server id). */
export interface PluginReviewPanel {
  id: string;
  /** The toggle's label, e.g. "trace". */
  label: string;
  render: (context: { turnId: string }) => ReactNode;
}

export interface MafWebPlugin {
  /** The plugin's name: its folder under plugins/, and the name /api/plugins lists. */
  name: string;
  routes?: readonly PluginRoute[];
  nav?: readonly PluginNavLink[];
  chatPanes?: readonly PluginChatPane[];
  chatSidebars?: readonly PluginChatSidebar[];
  turnActions?: readonly PluginTurnAction[];
  sourceActions?: readonly PluginSourceAction[];
  cards?: PluginCards;
  confirmations?: PluginConfirmations;
  toolLabels?: PluginToolLabels;
  monitorTabs?: readonly PluginMonitorTab[];
  runObservers?: readonly PluginRunObserver[];
  reviewPanels?: readonly PluginReviewPanel[];
}

/** Declares a plugin's web part. An identity function: the type is the contract. */
export function definePlugin(plugin: MafWebPlugin): MafWebPlugin {
  return plugin;
}
