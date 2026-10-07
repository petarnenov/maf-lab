import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { startBrowserTracing } from './browserTracing';
import { TelemetryPage } from './TelemetryPage';

/**
 * The observability plugin's web part (extract-observability-plugin): the Telemetry screen, and the browser's own spans,
 * started while the plugin is in use and stopped when it leaves, so a chat run's request carries the trace it started.
 */
export default definePlugin({
  name: 'observability',
  activate: () => startBrowserTracing(),
  routes: [{ path: 'telemetry', element: createElement(TelemetryPage) }],
  nav: [{ label: 'Telemetry', to: '/telemetry' }],
});
