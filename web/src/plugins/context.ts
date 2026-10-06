import { createContext, useContext } from 'react';
import { emptyRegistry, type PluginRegistry } from './registry';

export const PluginsContext = createContext<PluginRegistry>(emptyRegistry);

/** The plugins in use for the signed-in user (none before sign-in unless one is public). */
export function usePlugins(): PluginRegistry {
  return useContext(PluginsContext);
}
