import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { AdminDashboard } from '@maf/shared/AdminDashboard';

export default definePlugin({
  name: 'platform-admin',
  routes: [
    {
      path: 'platform',
      element: createElement(AdminDashboard, { platform: true }),
      platformAdmin: true,
    },
  ],
  nav: [{ to: '/platform', label: 'Platform admin', platformAdminOnly: true }],
});
