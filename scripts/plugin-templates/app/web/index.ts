import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { ${Name}Page } from './${Name}Page';

/** The $name plugin's web part: a page of its own and a link to it. */
export default definePlugin({
  name: '$name',
  routes: [{ path: '$name', element: createElement(${Name}Page) }],
  nav: [{ to: '/$name', label: '$title' }],
});
