import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { CompliancePage } from './CompliancePage';

// The compliance plugin's web part (extract-compliance-plugin): the audit screen — verify the record, browse it and
// export it — for a tenant administrator, at the path it always had.
export default definePlugin({
  name: 'compliance',
  routes: [{ path: 'admin/compliance', element: createElement(CompliancePage), admin: true }],
  nav: [{ to: '/admin/compliance', label: 'Compliance' }],
});
