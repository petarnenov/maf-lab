import { createElement } from 'react';
import { definePlugin } from '@maf/plugin-api';
import { CodePane } from './CodePane';
import { codeSnippetsOf, snippetKey } from './codeSnippets';

// The code plugin's web part (introduce-plugins 5.2): the Code snippets pane, what opening a code source does, and how
// the codebase search reads in the chat.
export default definePlugin({
  name: 'code',
  chatPanes: [
    {
      id: 'code',
      label: 'Code snippets',
      badge: (context) =>
        (context.turn ? codeSnippetsOf(context.turn.sources).length : 0) || undefined,
      render: (context) => createElement(CodePane, { context }),
    },
  ],
  sourceActions: [
    {
      kind: 'code',
      label: 'show in Code snippets',
      onOpen: (source, context) =>
        context.openPane(
          'code',
          snippetKey({
            path: source.sourcePath || source.docId,
            startLine: source.startLine ?? null,
          }),
        ),
    },
  ],
  toolLabels: {
    search_codebase: ({ running }) =>
      running ? 'Searching the codebase…' : 'Searched the codebase',
  },
});
