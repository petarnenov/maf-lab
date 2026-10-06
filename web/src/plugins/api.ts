import type { ComponentType, ReactNode } from 'react';
import type { SourceRef } from '../api/types';

/** A source of an answer, as the chat holds it: what a plugin's source action and panes read. */
export type { SourceRef };

/** The signed-in user's api client: a plugin's own routes, called as the user, with the core's errors and stop. */
export { useApi } from '../auth/useAuth';

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
}

/** What the chat gives a pane or a sidebar about the conversation on screen. */
export interface ChatContext {
  conversationId?: string;
  /** The selected assistant turn, when there is one. */
  turn?: ChatTurnView;
  /** Opens one of the chat's panes by id, with an optional value for it (a source to show, say). */
  openPane: (id: string, value?: unknown) => void;
  /**
   * For a pane's own render only: whether it is the pane showing, and the value it was last opened with. A pane stays
   * mounted while hidden, so it keeps its state and can open itself.
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

/** A sidebar left of the chat (the conversation list is one). */
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

/** A data card's renderer, by its AG-UI activity type. */
export type PluginCards = Record<string, ComponentType<{ content: unknown }>>;

/** A tab of the monitor (the monitor plugin owns the panel; others add tabs to it). */
export interface PluginMonitorTab {
  id: string;
  label: string;
  render: (context: { events: readonly unknown[] }) => ReactNode;
}

/** An observer of chat runs: how a plugin sees a run's AG-UI events without the core knowing why. */
export interface PluginRunObserver {
  onRunStart?: (runId: string) => void;
  onEvent?: (runId: string, event: unknown) => void;
  onRunEnd?: (runId: string) => void;
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
  toolLabels?: PluginToolLabels;
  monitorTabs?: readonly PluginMonitorTab[];
  runObservers?: readonly PluginRunObserver[];
}

/** Declares a plugin's web part. An identity function: the type is the contract. */
export function definePlugin(plugin: MafWebPlugin): MafWebPlugin {
  return plugin;
}
