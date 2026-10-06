import { Route, Routes } from 'react-router';
import { ChatPage } from '../chat/ChatPage';
import type { MafWebPlugin } from '../plugins/api';
import { PluginsContext } from '../plugins/context';
import { renderWithProviders } from './render';

/**
 * The chat page with the given plugins in use: how a plugin's tests see its parts inside the core's chat. It answers
 * /chat/:conversationId as the app does, so a test can open a stored conversation by its route.
 */
export function renderChat(
  plugins: readonly MafWebPlugin[],
  options?: Parameters<typeof renderWithProviders>[1],
) {
  return renderWithProviders(
    <PluginsContext.Provider value={{ plugins }}>
      <Routes>
        <Route path="chat/:conversationId?" element={<ChatPage />} />
        <Route path="*" element={<ChatPage />} />
      </Routes>
    </PluginsContext.Provider>,
    options,
  );
}
