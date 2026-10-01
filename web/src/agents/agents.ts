import type { AbstractAgent } from '@ag-ui/client';
import type { CopilotKitCoreReact } from '@copilotkit/react-core/v2/context';

/** Where CopilotKit's runtime is: it reaches every agent of this system (agui-protocol-only). */
export const RUNTIME_URL = '/copilotkit';

/** How long a screen waits for the runtime to say which agents it has. */
const CONNECT_TIMEOUT_MS = 10_000;

/**
 * An agent by its runtime name, once CopilotKit has heard from its runtime. The runtime is asked the first time any
 * screen needs an agent, not on every page. Undefined when the runtime does not answer or has no such agent.
 */
export async function agentNamed(
  copilotkit: CopilotKitCoreReact,
  id: string,
): Promise<AbstractAgent | undefined> {
  const known = copilotkit.getAgent(id);
  if (known) return known;
  if (copilotkit.runtimeConnectionStatus === 'error') return undefined;
  await new Promise<void>((resolve) => {
    const timer = setTimeout(done, CONNECT_TIMEOUT_MS);
    const subscription = copilotkit.subscribe({
      onRuntimeConnectionStatusChanged: ({ status }) => {
        if (status === 'connected' || status === 'error') done();
      },
    });
    function done() {
      clearTimeout(timer);
      subscription.unsubscribe();
      resolve();
    }
    if (copilotkit.runtimeConnectionStatus === 'disconnected') copilotkit.connect();
  });
  return copilotkit.getAgent(id);
}
