import { App } from '../App';
import type { MafWebPlugin } from '../plugins/api';
import { PluginsContext } from '../plugins/context';
import { renderWithProviders } from './render';

/** The real application router/navigation with explicit plugin contributions, for a plugin's route tests. */
export function renderPluginApp(
  plugins: readonly MafWebPlugin[],
  options?: Parameters<typeof renderWithProviders>[1],
) {
  return renderWithProviders(
    <PluginsContext.Provider value={{ plugins }}>
      <App />
    </PluginsContext.Provider>,
    options,
  );
}
