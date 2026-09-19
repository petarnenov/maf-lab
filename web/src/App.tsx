import { Navigate, Route, Routes } from 'react-router';
import { FeedbackAdminPage } from './admin/FeedbackAdminPage';
import { IndexAdminPage } from './admin/IndexAdminPage';
import { ChatPage } from './chat/ChatPage';
import { Layout } from './components/Layout';
import { RequireAdmin } from './components/RequireAdmin';
import { EvalsPage } from './evals/EvalsPage';

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Navigate to="/chat" replace />} />
        <Route path="chat" element={<ChatPage />} />
        <Route path="evals" element={<EvalsPage />} />
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
