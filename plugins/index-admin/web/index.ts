import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { IndexAdminPage } from './IndexAdminPage';

/**
 * The index admin's web part (extract-index-admin-plugin): the index administration screen, behind the core's admin
 * guard, and its link in the main navigation.
 */
export default definePlugin({
  name: 'index-admin',
  routes: [{ path: 'admin/index', element: createElement(IndexAdminPage), admin: true }],
  nav: [{ to: '/admin/index', label: 'Index admin' }],
});
