import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { EvalsPage } from './EvalsPage';
export default definePlugin({
  name: 'evals',
  routes: [{ path: 'evals', element: createElement(EvalsPage) }],
  nav: [{ to: '/evals', label: 'Evals' }],
});
