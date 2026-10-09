import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { CoveragePage } from './CoveragePage';

export default definePlugin({
  name: 'coverage',
  routes: [{ path: 'coverage', element: createElement(CoveragePage) }],
  nav: [{ to: '/coverage', label: 'Coverage' }],
});
