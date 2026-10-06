import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { HistorySidebar } from './HistorySidebar';
import { runsFinished } from './runsFinished';

/**
 * The conversation list's web part (introduce-plugins 5.4, decision 5y): the caller's conversations beside the chat,
 * searched, opened, renamed and deleted, read again whenever a run finishes. The chat owns the sidebar's chrome.
 */
export default definePlugin({
  name: 'conversation-history',
  chatSidebars: [
    {
      id: 'history',
      label: 'History',
      render: (context) => createElement(HistorySidebar, { context }),
    },
  ],
  runObservers: [runsFinished.observer],
});
