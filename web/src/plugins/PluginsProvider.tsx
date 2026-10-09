import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { apiRequest } from '../api/client';
import { useAuth } from '../auth/useAuth';
import type { MafWebPlugin } from './api';
import { DomainsContext, PluginsContext, type DomainInUse } from './context';
import type { PluginHealth, PluginRegistry } from './registry';

/** One plugin's web module, loaded on demand. */
export type PluginModuleLoader = () => Promise<{ default: MafWebPlugin }>;

/**
 * Every plugin's web part compiled into this build, by plugin name (its folder under plugins/). Built at build time; which
 * of them register is decided at run time by /api/plugins, so switching a plugin needs no web rebuild (decision 8).
 */
const bundled: Record<string, PluginModuleLoader> = Object.fromEntries(
  Object.entries(
    import.meta.glob<{ default: MafWebPlugin }>('../../../plugins/*/web/index.ts'),
  ).map(([path, load]) => [path.split('/').at(-3)!, load]),
);

interface PluginList {
  plugins: { name: string; health?: PluginHealth }[];
  domains?: DomainInUse[];
}

/**
 * Registers the web parts of the plugins in use. It asks /api/plugins anonymously before sign-in (only public plugins
 * answer then) and again after, and loads only the modules the answer names. A plugin that fails to load is left out;
 * the rest still register.
 */
export function PluginsProvider({
  children,
  modules = bundled,
}: {
  children: ReactNode;
  modules?: Record<string, PluginModuleLoader>;
}) {
  const { session } = useAuth();
  const token = session?.token ?? null;
  const inUse = useQuery({
    queryKey: ['plugins', token],
    queryFn: ({ signal }) => apiRequest<PluginList>(token, '/api/plugins', { signal }),
    staleTime: 30_000,
    refetchInterval: 30_000,
  });
  // The modules loaded, by the names in use; the registry is derived from them and the latest health, so a plugin whose
  // health changes re-renders without being imported again.
  const [loaded, setLoaded] = useState<readonly MafWebPlugin[]>([]);
  const [loadedNames, setLoadedNames] = useState<string | null>(null);
  const names = (inUse.data?.plugins ?? []).map((p) => p.name).join(',');
  // Old modules may finish loading after the caller or allowance changes. They never contribute unless
  // the current caller's latest registration names them, even while replacement modules are loading.
  const visible = useMemo(() => {
    const wanted = new Set(names.split(','));
    return loaded.filter((plugin) => wanted.has(plugin.name));
  }, [loaded, names]);

  useEffect(() => {
    let current = true;
    const wanted = names ? names.split(',').filter((name) => name in modules) : [];
    Promise.allSettled(wanted.map((name) => modules[name]())).then((loaded) => {
      if (!current) return;
      setLoaded(
        loaded.flatMap((result) => (result.status === 'fulfilled' ? [result.value.default] : [])),
      );
      setLoadedNames(names);
    });
    return () => {
      current = false;
    };
  }, [names, modules]);

  // Each loaded plugin is activated once, and deactivated when it leaves the set or the provider goes. The effect re-runs
  // on every change to the set, so the activations live in a ref, keyed by plugin name.
  const active = useRef(new Map<string, (() => void) | undefined>());
  useEffect(() => {
    const present = new Set(visible.map((plugin) => plugin.name));
    for (const [name, deactivate] of active.current) {
      if (present.has(name)) continue;
      active.current.delete(name);
      deactivate?.();
    }
    for (const plugin of visible) {
      if (active.current.has(plugin.name)) continue;
      const deactivate = plugin.activate?.();
      active.current.set(plugin.name, typeof deactivate === 'function' ? deactivate : undefined);
    }
  }, [visible]);
  useEffect(() => {
    const activations = active.current;
    return () => {
      for (const deactivate of activations.values()) deactivate?.();
      activations.clear();
    };
  }, []);

  const listed = inUse.data?.plugins;
  const registry = useMemo<PluginRegistry>(
    () => ({
      plugins: visible,
      ready: !inUse.isPending && loadedNames === names,
      health: Object.fromEntries((listed ?? []).map((p) => [p.name, p.health ?? 'unknown'])),
    }),
    [visible, listed, inUse.isPending, loadedNames, names],
  );

  // Known only once a signed-in answer is in: an anonymous one lists no domains (introduce-plugins 5h).
  const domains = token && inUse.data ? (inUse.data.domains ?? []) : undefined;

  return (
    <PluginsContext.Provider value={registry}>
      <DomainsContext.Provider value={domains}>{children}</DomainsContext.Provider>
    </PluginsContext.Provider>
  );
}
