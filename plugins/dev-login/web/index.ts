import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { DevTokenPicker } from './DevTokenPicker';
export default definePlugin({
  name: 'dev-login',
  authControls: [{ id: 'personas', render: () => createElement(DevTokenPicker) }],
});
