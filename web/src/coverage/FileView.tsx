import { useMemo, useRef, type ReactNode } from 'react';
import type { CoverageFileDetail, CoverageLine, LineStatus } from '../api/types';
import { pct, shortSha, when } from './format';
import { useWindowedList } from './useWindowedList';
import styles from './CoveragePage.module.css';

const LINE_HEIGHT = 20;

/** A marker per status that is not just a colour. */
const MARKS: Record<LineStatus, { symbol: string; label: string }> = {
  covered: { symbol: '●', label: 'covered' },
  uncovered: { symbol: '✕', label: 'not covered' },
  partial: { symbol: '◐', label: 'partly covered' },
};

function tip(line: CoverageLine): string {
  const hits = `${line.hits.toLocaleString()} ${line.hits === 1 ? 'hit' : 'hits'}`;
  return line.branchesTotal > 0 ? `${hits} · ${line.branchesCovered}/${line.branchesTotal} branches` : hits;
}

/** A file's source with each executable line's status, and a summary of the whole file. */
export function FileView({ detail, children }: { detail: CoverageFileDetail; children?: ReactNode }) {
  const lines = useMemo(() => detail.source.replace(/\n$/, '').split('\n'), [detail.source]);
  const byLine = useMemo(() => new Map(detail.lines.map((l) => [l.line, l])), [detail.lines]);
  const scroller = useRef<HTMLDivElement>(null);
  const windowed = useWindowedList(scroller, lines.length, LINE_HEIGHT);
  const s = detail.summary;

  return (
    <section className={styles.filePane} aria-label={`File ${detail.path}`}>
      <header className={styles.fileHeader}>
        <h2 className={styles.filePath}>{detail.path}</h2>
        <dl className={styles.summary}>
          <div>
            <dt>Lines</dt>
            <dd>
              {s.linesCovered} / {s.linesTotal}
            </dd>
          </div>
          <div>
            <dt>Branches</dt>
            <dd>
              {s.branchesCovered} / {s.branchesTotal}
            </dd>
          </div>
          <div>
            <dt>Coverage</dt>
            <dd className={s.pct < s.threshold ? styles.belowText : undefined}>{pct(s.pct)}</dd>
          </div>
          <div>
            <dt>Threshold</dt>
            <dd>
              {s.threshold}% {s.thresholdIsOverride ? '(override)' : '(default)'}
            </dd>
          </div>
          <div>
            <dt>Measured</dt>
            <dd>{when(detail.measuredAt)}</dd>
          </div>
          <div>
            <dt>Commit</dt>
            <dd className={styles.mono}>
              {shortSha(detail.commit)}
              {detail.dirty ? ' (dirty)' : ''}
            </dd>
          </div>
        </dl>
        {children}
      </header>

      <div
        className={styles.code}
        ref={scroller}
        onScroll={windowed.onScroll}
        role="region"
        aria-label="Source"
      >
        <ol className={styles.codeLines} style={{ height: windowed.totalHeight }}>
          {lines.slice(windowed.start, windowed.end).map((text, i) => {
            const number = windowed.start + i + 1;
            const line = byLine.get(number);
            const mark = line ? MARKS[line.status] : null;
            return (
              <li
                key={number}
                className={`${styles.codeLine} ${line ? styles[line.status] : ''}`}
                style={{ top: windowed.offsetTop + i * LINE_HEIGHT }}
                data-line={number}
                data-status={line?.status}
                tabIndex={line ? 0 : undefined}
                title={line ? tip(line) : undefined}
              >
                <span className={styles.gutter}>
                  {mark && (
                    <span className={styles.mark} aria-label={mark.label}>
                      {mark.symbol}
                    </span>
                  )}
                </span>
                <span className={styles.lineNo}>{number}</span>
                <code className={styles.lineText}>{text}</code>
                {line && (
                  <span className={styles.tip} role="tooltip">
                    {tip(line)}
                  </span>
                )}
              </li>
            );
          })}
        </ol>
      </div>
    </section>
  );
}
