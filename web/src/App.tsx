import { Navigate, Route, Routes } from 'react-router';
import { ChatPage } from './chat/ChatPage';
import { Layout } from './components/Layout';
import { RequireAdmin } from './components/RequireAdmin';
import { PluginBoundary } from './plugins/PluginBoundary';
import { usePlugins } from './plugins/context';
import { contributions } from './plugins/registry';

export function App() {
  const plugins = usePlugins();
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Navigate to="/chat" replace />} />
        {/* One optional-segment route keeps ChatPage mounted when a new conversation gets its URL. */}
        <Route path="chat/:conversationId?" element={<ChatPage />} />
        {/* Each plugin in use contributes its own pages (introduce-plugins decision 8), each inside its own boundary. */}
        {contributions(plugins, 'routes').map(({ plugin, item }) => (
          <Route
            key={`${plugin}:${item.path}`}
            path={item.path}
            element={
              <PluginBoundary plugin={plugin}>
                {item.platformAdmin ? (
                  <RequireAdmin role="PLATFORM_ADMIN">{item.element}</RequireAdmin>
                ) : item.admin ? (
                  <RequireAdmin>{item.element}</RequireAdmin>
                ) : (
                  item.element
                )}
              </PluginBoundary>
            }
          />
        ))}
        <Route
          path="*"
          element={
            plugins.ready === false ? (
              <p role="status">Loading pages…</p>
            ) : (
              <Navigate to="/chat" replace />
            )
          }
        />
      </Route>
    </Routes>
  );
}
