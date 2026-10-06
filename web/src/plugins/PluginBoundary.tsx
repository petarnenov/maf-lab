import type { ReactNode } from 'react';
import { ErrorBoundary } from '../shared/ErrorBoundary';

/** One plugin's contribution, inside its own error boundary: a plugin that throws shows its error, the rest keep working. */
export function PluginBoundary({ plugin, children }: { plugin: string; children: ReactNode }) {
  return <ErrorBoundary area={`The ${plugin} plugin`}>{children}</ErrorBoundary>;
}
