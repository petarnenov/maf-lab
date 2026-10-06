import { createContext, useContext } from 'react';
import { emptyRegistry, type PluginRegistry } from './registry';

export const PluginsContext = createContext<PluginRegistry>(emptyRegistry);

/** A domain in use: its id, and how it is named for a user, by language. */
export interface DomainInUse {
  id: string;
  scope: Partial<Record<'en' | 'bg', string>>;
}

/** The domains in use for the signed-in user; undefined while not known (signed out, or not loaded yet). */
export const DomainsContext = createContext<readonly DomainInUse[] | undefined>(undefined);

export function useDomains(): readonly DomainInUse[] | undefined {
  return useContext(DomainsContext);
}

/** The plugins in use for the signed-in user (none before sign-in unless one is public). */
export function usePlugins(): PluginRegistry {
  return useContext(PluginsContext);
}
