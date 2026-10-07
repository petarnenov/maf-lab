import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { A2AAdminPage } from './A2AAdminPage';

/**
 * The a2a plugin's web part (extract-a2a): the firm's view of what the agents have been doing — the tasks partners
 * started, the consultations of other agents and the webhook deliveries — behind the core's admin guard, and its link in
 * the main navigation.
 */
export default definePlugin({
  name: 'a2a',
  routes: [{ path: 'admin/a2a', element: createElement(A2AAdminPage), admin: true }],
  nav: [{ to: '/admin/a2a', label: 'Agent to agent' }],
});
