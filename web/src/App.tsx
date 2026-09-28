import { Navigate, Route, Routes } from 'react-router';
import { A2AAdminPage } from './admin/A2AAdminPage';
import { FeedbackAdminPage } from './admin/FeedbackAdminPage';
import { IndexAdminPage } from './admin/IndexAdminPage';
import { ChatPage } from './chat/ChatPage';
import { CurriculumPage } from './curriculum/CurriculumPage';
import { Layout } from './components/Layout';
import { RequireAdmin } from './components/RequireAdmin';
import { EvalsPage } from './evals/EvalsPage';
import { JevPage } from './jev/JevPage';
import { CompliancePage } from './compliance/CompliancePage';
import { TelemetryPage } from './telemetry/TelemetryPage';
import { TopologyPage } from './topology/TopologyPage';

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Navigate to="/chat" replace />} />
        {/* One optional-segment route keeps ChatPage mounted when a new conversation gets its URL. */}
        <Route path="chat/:conversationId?" element={<ChatPage />} />
        <Route path="evals" element={<EvalsPage />} />
        <Route path="topology" element={<TopologyPage />} />
        <Route path="telemetry" element={<TelemetryPage />} />
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
          path="admin/compliance"
          element={
            <RequireAdmin>
              <CompliancePage />
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
        <Route path="*" element={<Navigate to="/chat" replace />} />
      </Route>
    </Routes>
  );
}
