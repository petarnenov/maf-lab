import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { CurriculumPage } from './CurriculumPage';

/** The curriculum map's web part (extract-curriculum-plugin): its page and its link in the main navigation. */
export default definePlugin({
  name: 'curriculum',
  routes: [{ path: 'curriculum', element: createElement(CurriculumPage) }],
  nav: [{ to: '/curriculum', label: 'Curriculum' }],
});
