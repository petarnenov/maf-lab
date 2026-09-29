import { useState } from 'react';
import type { SourceRef } from '../api/types';
import styles from './SourcesPanel.module.css';

/** How many sources show before the list is expanded. */
const COLLAPSED = 5;

/**
 * The sources of an answer. A documentation source expands to its excerpt; a code source (add-codebase-domain) reads as
 * its place and, given `onOpenCode`, opens the Code snippets tab on that snippet.
 */
export function SourcesPanel({
  sources,
  onOpenCode,
}: {
  sources: SourceRef[];
  onOpenCode?: (source: SourceRef) => void;
}) {
  const [open, setOpen] = useState<string | null>(null);
  const [all, setAll] = useState(false);
  if (sources.length === 0) return null;
  // A long list reads as noise under a short answer: the first few, and the rest a click away.
  const shown = all ? sources : sources.slice(0, COLLAPSED);

  return (
    <section className={styles.panel} aria-label="Sources">
      <h3 className={styles.title}>Sources ({sources.length})</h3>
      <ul className={styles.list}>
        {shown.map((source, index) => {
          const key = `${source.docId}#${source.sectionPath}#${index}`;
          const expanded = open === key;
          if (source.kind === 'code' && onOpenCode) {
            const path = source.sourcePath || source.docId;
            const slash = path.lastIndexOf('/');
            const file = path.slice(slash + 1);
            const folder = slash > 0 ? path.slice(0, slash) : '';
            const lines =
              source.startLine != null
                ? `:${source.startLine}–${source.endLine ?? source.startLine}`
                : '';
            const detail = [folder, source.symbol].filter(Boolean).join(' › ');
            return (
              <li key={key}>
                <button
                  type="button"
                  className={`${styles.section} ${styles.code}`}
                  onClick={() => onOpenCode(source)}
                  title={`${source.sectionPath} — show in Code snippets`}
                  aria-label={`${path}${lines} ${source.symbol ?? ''} — show in Code snippets`.trim()}
                >
                  <span className={styles.codeHead}>
                    <span className={styles.codeFile}>
                      {file}
                      <span className={styles.codeLines}>{lines}</span>
                    </span>
                    <span className={styles.codeOpen} aria-hidden="true">
                      ↗
                    </span>
                  </span>
                  {detail && <span className={styles.codeDetail}>{detail}</span>}
                </button>
              </li>
            );
          }
          return (
            <li key={key}>
              <button
                type="button"
                className={styles.section}
                aria-expanded={expanded}
                onClick={() => setOpen(expanded ? null : key)}
              >
                <span className={styles.sectionPath}>
                  {source.sectionPath || source.sourcePath}
                </span>
                <span className={styles.path}>{source.sourcePath}</span>
              </button>
              {expanded && <blockquote className={styles.snippet}>{source.snippet}</blockquote>}
            </li>
          );
        })}
      </ul>
      {sources.length > COLLAPSED && (
        <button type="button" className={styles.more} onClick={() => setAll(!all)}>
          {all ? 'Show fewer' : `Show all ${sources.length}`}
        </button>
      )}
    </section>
  );
}
