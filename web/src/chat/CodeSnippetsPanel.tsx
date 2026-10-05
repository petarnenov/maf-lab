import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { StopHint } from '../components/StopHint';
import { useEscToStop } from '../components/useEscToStop';
import type { CodeSnippet } from '../api/types';
import { groupByFile, snippetKey, useCodeSnippets } from './codeSnippets';
import styles from './CodeSnippetsPanel.module.css';

export interface CodeSnippetsPanelProps {
  /** The question of the turn the pane follows. */
  question: string;
  /** True while the tab is shown: related code is fetched only then. */
  active: boolean;
  /**
   * The snippets the answer itself used (its search_codebase sources). When there are any the tab shows them and fetches
   * nothing: what the answer rests on, not a second search that could disagree with it (add-codebase-domain).
   */
  used?: CodeSnippet[];
  /** The snippet a code source in the answer pointed at: scrolled to and outlined. */
  highlight?: string | null;
}

/** "Code snippets": the code the answer used, or else the repository's related code for the turn's question. */
export function CodeSnippetsPanel({
  question,
  active,
  used = [],
  highlight = null,
}: CodeSnippetsPanelProps) {
  const answered = used.length > 0;
  const query = useCodeSnippets(question, active && !answered);
  // A search still running stops on Esc (stop-anything): the request is aborted, and the code server stops searching.
  const queryClient = useQueryClient();
  const [stopped, setStopped] = useState(false);
  useEscToStop(active && query.isFetching, () => {
    setStopped(true);
    void queryClient.cancelQueries({ queryKey: ['code-snippets', question] });
  });

  let body;
  let label: string | null = null;
  if (answered) {
    label = 'Used in this answer';
    body = <Files snippets={used} highlight={highlight} />;
  } else if (!question.trim()) {
    body = <p className={styles.note}>Ask a question to see the code that answers it.</p>;
  } else if (query.isPending && query.fetchStatus !== 'idle') {
    body = (
      <>
        <p className={styles.note}>Searching the codebase…</p>
        <StopHint stopping={false} />
      </>
    );
  } else if (stopped && !query.data) {
    body = (
      <p className={styles.note} role="status" data-testid="snippets-stopped">
        Stopped.{' '}
        <button
          type="button"
          onClick={() => {
            setStopped(false);
            void query.refetch();
          }}
        >
          Search again
        </button>
      </p>
    );
  } else if (query.isError) {
    body = (
      <p className={styles.error} role="alert">
        Code search is unavailable right now.
      </p>
    );
  } else if (query.data && query.data.results.length === 0) {
    body = (
      <p className={styles.note}>
        No code matches this question.
        {query.data.refineHint && <span className={styles.hint}> {query.data.refineHint}</span>}
      </p>
    );
  } else if (query.data) {
    label = 'Related code — not used by this answer';
    body = <Files snippets={query.data.results} highlight={null} />;
  }

  return (
    <section className={styles.panel} aria-label="Code snippets">
      <header className={styles.header}>
        <h2 className={styles.title}>Code snippets</h2>
        {question.trim() && (
          <p className={styles.question} title={question}>
            for “{question}”
          </p>
        )}
        {label && (
          <p
            className={`${styles.provenance} ${answered ? styles.used : styles.related}`}
            data-testid="code-provenance"
          >
            {label}
          </p>
        )}
      </header>
      <div className={styles.body}>{body}</div>
    </section>
  );
}

function Files({ snippets, highlight }: { snippets: CodeSnippet[]; highlight: string | null }) {
  const groups = groupByFile(snippets);
  return (
    <ol className={styles.files}>
      {groups.map((g) => (
        <li key={g.path} className={styles.file} data-testid="code-file">
          <div className={styles.path} title={g.path}>
            {g.path}
          </div>
          {g.snippets.map((s) => (
            <SnippetView
              key={`${s.startLine}-${s.section}`}
              snippet={s}
              highlighted={highlight === snippetKey(s)}
            />
          ))}
        </li>
      ))}
    </ol>
  );
}

function SnippetView({ snippet, highlighted }: { snippet: CodeSnippet; highlighted: boolean }) {
  const ref = useRef<HTMLElement>(null);
  useEffect(() => {
    if (highlighted) ref.current?.scrollIntoView?.({ block: 'nearest', behavior: 'smooth' });
  }, [highlighted]);

  const lines = snippet.snippet.split('\n');
  const first = snippet.startLine ?? 1;
  return (
    <figure
      ref={ref}
      className={`${styles.snippet} ${highlighted ? styles.highlighted : ''}`}
      data-testid="code-snippet"
      data-highlighted={highlighted || undefined}
    >
      <figcaption className={styles.caption}>
        {snippet.startLine !== null && (
          <span className={styles.lines}>
            lines {snippet.startLine}–{snippet.endLine ?? snippet.startLine}
          </span>
        )}
        {snippet.symbol && <code className={styles.symbol}>{snippet.symbol}</code>}
        {!snippet.symbol && snippet.kind === 'docs' && (
          <span className={styles.symbol}>{snippet.section.split(' > ').slice(1).join(' › ')}</span>
        )}
      </figcaption>
      <pre className={styles.code} data-language={snippet.language}>
        {lines.map((line, i) => (
          <span key={i} className={styles.line}>
            {snippet.startLine !== null && (
              <span className={styles.number} aria-hidden="true">
                {first + i}
              </span>
            )}
            <span className={styles.text}>{line}</span>
          </span>
        ))}
      </pre>
    </figure>
  );
}
