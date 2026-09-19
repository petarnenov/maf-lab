import { useState } from 'react';
import type { SourceRef } from '../api/types';
import styles from './SourcesPanel.module.css';

export function SourcesPanel({ sources }: { sources: SourceRef[] }) {
  const [open, setOpen] = useState<string | null>(null);
  if (sources.length === 0) return null;

  return (
    <section className={styles.panel} aria-label="Sources">
      <h3 className={styles.title}>Sources ({sources.length})</h3>
      <ul className={styles.list}>
        {sources.map((source, index) => {
          const key = `${source.docId}#${source.sectionPath}#${index}`;
          const expanded = open === key;
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
    </section>
  );
}
