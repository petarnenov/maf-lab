import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { JevPage } from './JevPage';

/**
 * The Jev and intent statistics' web part (extract-insights-plugin): the Jev screen, behind the core's admin guard,
 * and its link in the main navigation.
 */
export default definePlugin({
  name: 'insights',
  routes: [{ path: 'admin/jev', element: createElement(JevPage), admin: true }],
  nav: [{ to: '/admin/jev', label: 'Jev' }],
});
