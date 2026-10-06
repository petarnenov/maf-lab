import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';
import { apiRequest } from '../api/client';
import { useAuth } from '../auth/useAuth';
import type { MafWebPlugin } from './api';
import { DomainsContext, PluginsContext, type DomainInUse } from './context';
import { emptyRegistry, type PluginRegistry } from './registry';

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
  plugins: { name: string }[];
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
  });
  const [registry, setRegistry] = useState<PluginRegistry>(emptyRegistry);
  const names = (inUse.data?.plugins ?? []).map((p) => p.name).join(',');

  useEffect(() => {
    let current = true;
    const wanted = names ? names.split(',').filter((name) => name in modules) : [];
    Promise.allSettled(wanted.map((name) => modules[name]())).then((loaded) => {
      if (!current) return;
      setRegistry({
        plugins: loaded.flatMap((result) =>
          result.status === 'fulfilled' ? [result.value.default] : [],
        ),
      });
    });
    return () => {
      current = false;
    };
  }, [names, modules]);

  // Known only once a signed-in answer is in: an anonymous one lists no domains (introduce-plugins 5h).
  const domains = token && inUse.data ? (inUse.data.domains ?? []) : undefined;

  return (
    <PluginsContext.Provider value={registry}>
      <DomainsContext.Provider value={domains}>{children}</DomainsContext.Provider>
    </PluginsContext.Provider>
  );
}
