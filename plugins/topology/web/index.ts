import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { TopologyPage } from './TopologyPage';
export default definePlugin({
  name: 'topology',
  routes: [{ path: 'topology', element: createElement(TopologyPage) }],
  nav: [{ to: '/topology', label: 'Topology' }],
});
