import { ChatPage } from '../chat/ChatPage';
import type { MafWebPlugin } from '../plugins/api';
import { PluginsContext } from '../plugins/context';
import { renderWithProviders } from './render';

/** The chat page with the given plugins in use: how a plugin's tests see its parts inside the core's chat. */
export function renderChat(
  plugins: readonly MafWebPlugin[],
  options?: Parameters<typeof renderWithProviders>[1],
) {
  return renderWithProviders(
    <PluginsContext.Provider value={{ plugins }}>
      <ChatPage />
    </PluginsContext.Provider>,
    options,
  );
}
