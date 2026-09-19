import { useState } from 'react';
import type { TraceEvent } from '../api/types';
import styles from './MonitorPanel.module.css';
import { McpTab, ModelTab, PromptTab, RetrievalTab, TimelineTab } from './MonitorTabs';
import {
  byKind,
  dataOf,
  firstOf,
  formatMs,
  mcpInstances,
  totalDuration,
  type TurnStartData,
} from './traceData';

const TABS = [
  { id: 'timeline', label: 'Timeline' },
  { id: 'model', label: 'Model' },
  { id: 'retrieval', label: 'Retrieval' },
  { id: 'mcp', label: 'MCP' },
  { id: 'prompt', label: 'Prompt & memory' },
] as const;

type TabId = (typeof TABS)[number]['id'];

export interface MonitorPanelProps {
  events: TraceEvent[];
  /** True while the turn is still streaming. */
  live?: boolean;
  loading?: boolean;
  error?: string | null;
  title?: string;
}

/** "Behind the scenes" view of one chat turn, built from its trace events. */
export function MonitorPanel({
  events,
  live,
  loading,
  error,
  title = 'Behind the scenes',
}: MonitorPanelProps) {
  const [tab, setTab] = useState<TabId>('timeline');
  const start = dataOf<TurnStartData>(firstOf(events, 'turn.start'));
  const mcp = mcpInstances(events);

  return (
    <section className={styles.panel} aria-label={title}>
      <header className={styles.header}>
        <h2 className={styles.title}>
          {title} {live && <span className={styles.live}>● live</span>}
        </h2>
        {events.length > 0 && (
          <div className={styles.stats} data-testid="monitor-stats">
            <span className={styles.chip}>{events.length} events</span>
            <span className={styles.chip}>total {formatMs(totalDuration(events))}</span>
            <span className={styles.chip}>
              {byKind(events, 'model.response').length} model calls
            </span>
            <span className={styles.chip}>{byKind(events, 'tool.call').length} tool calls</span>
            <span className={styles.chip}>{byKind(events, 'retrieval').length} searches</span>
            {start.apiInstance && <span className={styles.chip}>api: {start.apiInstance}</span>}
            {mcp.map((m) => (
              <span key={m} className={styles.chip}>
                mcp: {m}
              </span>
            ))}
          </div>
        )}
      </header>
      <nav className={styles.tabs} role="tablist" aria-label="Monitor views">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            className={`${styles.tab} ${tab === t.id ? styles.tabActive : ''}`}
            onClick={() => setTab(t.id)}
          >
            {t.label}
          </button>
        ))}
      </nav>
      <div className={styles.body} role="tabpanel">
        {error ? (
          <p className={styles.error} role="alert">
            {error}
          </p>
        ) : events.length === 0 ? (
          <p className={styles.empty}>
            {loading ? 'Loading trace…' : 'Ask a question to see what happens behind the scenes.'}
          </p>
        ) : tab === 'timeline' ? (
          <TimelineTab events={events} />
        ) : tab === 'model' ? (
          <ModelTab events={events} />
        ) : tab === 'retrieval' ? (
          <RetrievalTab events={events} />
        ) : tab === 'mcp' ? (
          <McpTab events={events} apiInstance={start.apiInstance} />
        ) : (
          <PromptTab events={events} />
        )}
      </div>
    </section>
  );
}
