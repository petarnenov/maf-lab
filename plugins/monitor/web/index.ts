import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { MonitorPane } from './MonitorPane';
import { monitorStore } from './monitorStore';
import { StoredTracePanel } from './StoredTracePanel';

/**
 * The monitor's web part (introduce-plugins 5.3): "Behind the scenes" beside the chat, fed by the chat's runs through
 * its run observer and by the monitor's trace routes; and a reviewed turn's stored trace in the feedback review queue.
 */
export default definePlugin({
  name: 'monitor',
  chatPanes: [
    {
      id: 'scenes',
      label: 'Behind the scenes',
      render: (context) => createElement(MonitorPane, { context }),
    },
  ],
  runObservers: [monitorStore.observer],
  reviewPanels: [
    {
      id: 'trace',
      label: 'trace',
      render: ({ turnId }) =>
        createElement(StoredTracePanel, { turnId, title: `Behind the scenes — turn ${turnId}` }),
    },
  ],
});
