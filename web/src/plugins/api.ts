import type { ComponentType, ReactNode } from 'react';

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

/** What the chat gives a pane or a sidebar about the conversation on screen. */
export interface ChatContext {
  conversationId?: string;
  /** Opens one of the chat's panes by id, with an optional value for it (a source to show, say). */
  openPane: (id: string, value?: unknown) => void;
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
  onOpen: (source: unknown, context: ChatContext) => void;
}

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
  monitorTabs?: readonly PluginMonitorTab[];
  runObservers?: readonly PluginRunObserver[];
}

/** Declares a plugin's web part. An identity function: the type is the contract. */
export function definePlugin(plugin: MafWebPlugin): MafWebPlugin {
  return plugin;
}
