import { Navigate, Route, Routes } from 'react-router';
import { FeedbackAdminPage } from './admin/FeedbackAdminPage';
import { IndexAdminPage } from './admin/IndexAdminPage';
import { ChatPage } from './chat/ChatPage';
import { Layout } from './components/Layout';
import { RequireAdmin } from './components/RequireAdmin';
import { EvalsPage } from './evals/EvalsPage';
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
        <Route path="*" element={<Navigate to="/chat" replace />} />
      </Route>
    </Routes>
  );
}
