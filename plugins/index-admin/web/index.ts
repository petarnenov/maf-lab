import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { IndexAdminPage } from './IndexAdminPage';

/**
 * The index administration screen, contributed to the platform dashboard and available through its own guarded route
 * when the dashboard shell is absent.
 */
export default definePlugin({
  name: 'index-admin',
  routes: [{ path: 'admin/index', element: createElement(IndexAdminPage), platformAdmin: true }],
  nav: [{ to: '/admin/index', label: 'Index admin', platformAdminOnly: true }],
  platformAdminSections: [
    {
      id: 'index-admin',
      label: 'Index administration',
      render: () => createElement(IndexAdminPage),
    },
  ],
});
