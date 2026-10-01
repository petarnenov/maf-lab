import {
  CopilotKitContext,
  CopilotKitCoreReact,
  EMPTY_SET,
} from '@copilotkit/react-core/v2/context';
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { authHeaders } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { RUNTIME_URL } from './agents';

/**
 * The one CopilotKit core of the app (agui-protocol-only, DECISIONS §74): every screen reaches every agent through it,
 * and only through it — over the AG-UI protocol, via CopilotKit's runtime. Headless: the page keeps its own components,
 * styles and theme. The caller's bearer token goes with every request, and the servers decide the tenant.
 */
export function AgentsProvider({ children }: { children: ReactNode }) {
  const { session } = useAuth();
  const token = session?.token ?? null;
  const [copilotkit] = useState(
    () =>
      new CopilotKitCoreReact({
        runtimeUrl: RUNTIME_URL,
        headers: authHeaders(token),
        deferInitialConnection: true,
        // The servers keep each conversation; a run carries only what is new in it, which is all an agent here reads.
        messageFilter: (messages) => messages.slice(-1),
      }),
  );

  useEffect(() => {
    copilotkit.setHeaders({ Authorization: token ? `Bearer ${token}` : null });
  }, [copilotkit, token]);

  const value = useMemo(() => ({ copilotkit, executingToolCallIds: EMPTY_SET }), [copilotkit]);
  return <CopilotKitContext.Provider value={value}>{children}</CopilotKitContext.Provider>;
}
