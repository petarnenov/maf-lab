import { Navigate, Route, Routes } from 'react-router';
import { FeedbackAdminPage } from './admin/FeedbackAdminPage';
import { ChatPage } from './chat/ChatPage';
import { CoveragePage } from './coverage/CoveragePage';
import { Layout } from './components/Layout';
import { RequireAdmin } from './components/RequireAdmin';
import { EvalsPage } from './evals/EvalsPage';
import { TopologyPage } from './topology/TopologyPage';
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
        <Route path="evals" element={<EvalsPage />} />
        <Route path="topology" element={<TopologyPage />} />
        <Route path="coverage" element={<CoveragePage />} />
        <Route
          path="admin/feedback"
          element={
            <RequireAdmin>
              <FeedbackAdminPage />
            </RequireAdmin>
          }
        />
        {/* Each plugin in use contributes its own pages (introduce-plugins decision 8), each inside its own boundary. */}
        {contributions(plugins, 'routes').map(({ plugin, item }) => (
          <Route
            key={`${plugin}:${item.path}`}
            path={item.path}
            element={
              <PluginBoundary plugin={plugin}>
                {item.admin ? <RequireAdmin>{item.element}</RequireAdmin> : item.element}
              </PluginBoundary>
            }
          />
        ))}
        <Route path="*" element={<Navigate to="/chat" replace />} />
      </Route>
    </Routes>
  );
}
