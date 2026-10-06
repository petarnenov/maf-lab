import { useState } from 'react';
import styles from './JsonView.module.css';

/** Dependency-free, collapsible JSON renderer for raw trace data. */
export function JsonView({ value, expandDepth = 1 }: { value: unknown; expandDepth?: number }) {
  return (
    <div className={styles.root} data-testid="json-view">
      <Node value={value} depth={0} expandDepth={expandDepth} />
    </div>
  );
}

function Node({
  name,
  value,
  depth,
  expandDepth,
}: {
  name?: string;
  value: unknown;
  depth: number;
  expandDepth: number;
}) {
  const [open, setOpen] = useState(depth < expandDepth);
  const label = name !== undefined ? <span className={styles.key}>{name}: </span> : null;

  if (value === null || typeof value !== 'object') {
    return (
      <div>
        {label}
        <Scalar value={value} />
      </div>
    );
  }

  const entries: [string, unknown][] = Array.isArray(value)
    ? value.map((v, i) => [String(i), v])
    : Object.entries(value as Record<string, unknown>);
  const summary = Array.isArray(value) ? `[${entries.length}]` : `{${entries.length}}`;

  return (
    <div>
      <button
        type="button"
        className={styles.toggle}
        aria-expanded={open}
        aria-label={`${open ? 'Collapse' : 'Expand'} ${name ?? 'value'}`}
        onClick={() => setOpen((o) => !o)}
      >
        {open ? '▾' : '▸'}
      </button>
      {label}
      <span className={styles.summary}>{summary}</span>
      {open && (
        <ul className={styles.children}>
          {entries.map(([k, v]) => (
            <li key={k}>
              <Node name={k} value={v} depth={depth + 1} expandDepth={expandDepth} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function Scalar({ value }: { value: unknown }) {
  if (typeof value === 'string') return <span className={styles.string}>&quot;{value}&quot;</span>;
  if (typeof value === 'number') return <span className={styles.number}>{value}</span>;
  return <span className={styles.literal}>{String(value)}</span>;
}
