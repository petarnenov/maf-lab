import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { FeedbackAdminPage } from './FeedbackAdminPage';

export default definePlugin({
  name: 'feedback-review',
  routes: [{ path: 'admin/feedback', element: createElement(FeedbackAdminPage), admin: true }],
  nav: [{ to: '/admin/feedback', label: 'Feedback review', adminOnly: true }],
  tenantAdminSections: [
    {
      id: 'feedback-review',
      label: 'Feedback review',
      render: () => createElement(FeedbackAdminPage),
    },
  ],
});
