import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { AdminDashboard } from '@maf/shared/AdminDashboard';

export default definePlugin({
  name: 'tenant-admin',
  routes: [
    {
      path: 'admin',
      element: createElement(AdminDashboard, { platform: false }),
      admin: true,
    },
  ],
  nav: [{ to: '/admin', label: 'Tenant admin', adminOnly: true }],
});
