import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { A2AAdminPage } from './A2AAdminPage';

export default definePlugin({
  name: 'a2a',
  routes: [{ path: 'admin/a2a', element: createElement(A2AAdminPage), admin: true }],
  nav: [{ to: '/admin/a2a', label: 'Agent to agent', adminOnly: true }],
});
