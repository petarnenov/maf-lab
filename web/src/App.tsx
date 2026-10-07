import { Navigate, Route, Routes } from 'react-router';
import { A2AAdminPage } from './admin/A2AAdminPage';
import { FeedbackAdminPage } from './admin/FeedbackAdminPage';
import { IndexAdminPage } from './admin/IndexAdminPage';
import { ChatPage } from './chat/ChatPage';
import { CoveragePage } from './coverage/CoveragePage';
import { CurriculumPage } from './curriculum/CurriculumPage';
import { Layout } from './components/Layout';
import { RequireAdmin } from './components/RequireAdmin';
import { EvalsPage } from './evals/EvalsPage';
import { JevPage } from './jev/JevPage';
import { TelemetryPage } from './telemetry/TelemetryPage';
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
        <Route path="telemetry" element={<TelemetryPage />} />
        <Route path="coverage" element={<CoveragePage />} />
        <Route path="curriculum" element={<CurriculumPage />} />
        <Route
          path="admin/index"
          element={
            <RequireAdmin>
              <IndexAdminPage />
            </RequireAdmin>
          }
        />
        <Route
          path="admin/feedback"
          element={
            <RequireAdmin>
              <FeedbackAdminPage />
            </RequireAdmin>
          }
        />
        <Route
          path="admin/jev"
          element={
            <RequireAdmin>
              <JevPage />
            </RequireAdmin>
          }
        />
        {/* The screen was "Jev intents" at /admin/intents; keep the old link working. */}
        <Route path="admin/intents" element={<Navigate to="/admin/jev" replace />} />
        <Route
          path="admin/a2a"
          element={
            <RequireAdmin>
              <A2AAdminPage />
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
