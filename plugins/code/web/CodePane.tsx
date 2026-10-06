import { useEffect, useRef } from 'react';
import type { ChatContext } from '@maf/plugin-api';
import { CodeSnippetsPanel } from './CodeSnippetsPanel';
import { codeSnippetsOf } from './codeSnippets';

/**
 * The Code snippets pane beside the chat (add-codebase-search), as the code plugin's pane (introduce-plugins 5.2). It
 * stays mounted while hidden, so it keeps what it showed and can bring itself into view: an answer given in this
 * session that searched the codebase opens the pane once per turn; a turn reopened from history does not move it.
 */
export function CodePane({ context }: { context: ChatContext }) {
  const turn = context.turn;
  const used = turn ? codeSnippetsOf(turn.sources) : [];
  const opened = useRef(new Set<string>());
  const openPane = useRef(context.openPane);
  useEffect(() => {
    openPane.current = context.openPane;
  });
  const fresh = turn && !turn.restored && used.length > 0 ? turn.key : undefined;
  useEffect(() => {
    if (fresh && !opened.current.has(fresh)) {
      opened.current.add(fresh);
      openPane.current('code');
    }
  }, [fresh]);
  const highlight = typeof context.pane?.value === 'string' ? context.pane.value : null;
  return (
    <CodeSnippetsPanel
      question={turn?.question ?? ''}
      active={context.pane?.active ?? false}
      used={used}
      highlight={highlight}
    />
  );
}
