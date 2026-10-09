import { useMemo, useRef, useState } from 'react';
import type { CoverageTree as Tree } from './types';
import { buildTree, flatten, type SortKey, type TreeView } from './treeModel';
import { pct } from './format';
import { TreeRunBadge } from './TreeRunBadge';
import { liveRunIds } from './treeRuns';
import { useWindowedList } from './useWindowedList';
import styles from './CoveragePage.module.css';

const ROW_HEIGHT = 30;

/** The source tree with coverage per file and folder, sortable and filterable. */
export function CoverageTree({
  tree,
  selected,
  onSelect,
}: {
  tree: Tree;
  selected: string | null;
  onSelect: (path: string) => void;
}) {
  const [view, setView] = useState<TreeView>({ sort: 'name', dir: 'asc', filter: '', belowOnly: false });
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
  const nodes = useMemo(() => buildTree(tree, view), [tree, view]);
  const live = useMemo(() => liveRunIds(tree.files), [tree]);
  const rows = useMemo(() => flatten(nodes, collapsed), [nodes, collapsed]);
  const scroller = useRef<HTMLDivElement>(null);
  const windowed = useWindowedList(scroller, rows.length, ROW_HEIGHT, 20);

  const toggle = (path: string) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });

  return (
    <section className={styles.treePane} aria-label="Source tree">
      <div className={styles.treeControls}>
        <input
          type="search"
          aria-label="Filter by path"
          placeholder="Filter by path"
          value={view.filter}
          onChange={(e) => setView({ ...view, filter: e.target.value })}
        />
        <label>
          Sort{' '}
          <select
            aria-label="Sort by"
            value={view.sort}
            onChange={(e) => setView({ ...view, sort: e.target.value as SortKey })}
          >
            <option value="name">Name</option>
            <option value="coverage">Coverage</option>
          </select>
        </label>
        <button
          type="button"
          aria-label={view.dir === 'asc' ? 'Ascending' : 'Descending'}
          onClick={() => setView({ ...view, dir: view.dir === 'asc' ? 'desc' : 'asc' })}
        >
          {view.dir === 'asc' ? '↑' : '↓'}
        </button>
        <label className={styles.toggle}>
          <input
            type="checkbox"
            checked={view.belowOnly}
            onChange={(e) => setView({ ...view, belowOnly: e.target.checked })}
          />{' '}
          Below threshold only
        </label>
      </div>

      {rows.length === 0 ? (
        <p className={styles.muted}>No files match.</p>
      ) : (
        <div className={styles.treeScroll} ref={scroller} onScroll={windowed.onScroll}>
          <ul className={styles.treeList} style={{ height: windowed.totalHeight }}>
            {rows.slice(windowed.start, windowed.end).map(({ node, depth }, i) => (
              <li
                key={node.path}
                className={styles.treeRow}
                style={{ top: windowed.offsetTop + i * ROW_HEIGHT, paddingLeft: 8 + depth * 14 }}
              >
                {node.kind === 'folder' ? (
                  <button
                    type="button"
                    className={styles.treeItem}
                    aria-expanded={!collapsed.has(node.path)}
                    onClick={() => toggle(node.path)}
                  >
                    <span className={styles.caret} aria-hidden>
                      {collapsed.has(node.path) ? '▸' : '▾'}
                    </span>
                    <span className={styles.name}>{node.name}/</span>
                    <span className={styles.pct}>{pct(node.pct)}</span>
                  </button>
                ) : (
                  <button
                    type="button"
                    className={`${styles.treeItem} ${selected === node.path ? styles.selectedRow : ''}`}
                    aria-current={selected === node.path ? 'true' : undefined}
                    onClick={() => onSelect(node.path)}
                    title={node.path}
                  >
                    <span className={styles.name}>{node.name}</span>
                    {node.file.belowThreshold && (
                      <span className={styles.below} title={`Below its ${node.file.threshold}% threshold`}>
                        <span aria-hidden>▼</span> below
                      </span>
                    )}
                    {node.file.candidate && (
                      <span className={styles.candidate}>candidate {pct(node.file.candidate.pct)}</span>
                    )}
                    <TreeRunBadge file={node.file} live={!!node.file.run && live.has(node.file.run.id)} />
                    <span className={styles.pct}>{pct(node.pct)}</span>
                    <span className={styles.threshold}>/ {node.file.threshold}%</span>
                  </button>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}
