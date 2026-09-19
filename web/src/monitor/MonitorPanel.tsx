import { useState } from 'react';
import type { TraceEvent } from '../api/types';
import { JsonView } from './JsonView';
import styles from './MonitorPanel.module.css';
import { TimeTravelBar } from './TimeTravelBar';
import { timeTravelKeyHandler } from './timeTravelKeys';
import { useTimeTravel, type TimeTravel } from './useTimeTravel';
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
  /** Shared time-travel state (the chat page also rewinds the answer); otherwise the panel keeps its own. */
  timeTravel?: TimeTravel;
  /** Identifies the turn for the panel's own time-travel state. */
  resetKey?: string;
}

/** "Behind the scenes" view of one chat turn, built from its trace events. */
export function MonitorPanel({
  events,
  live,
  loading,
  error,
  title = 'Behind the scenes',
  timeTravel,
  resetKey,
}: MonitorPanelProps) {
  const [tab, setTab] = useState<TabId>('timeline');
  const own = useTimeTravel(events, resetKey);
  const tt = timeTravel ?? own;
  const cursor = Math.min(tt.cursor, events.length);
  // Every view shows the turn as it was after `cursor` steps.
  const visible = events.slice(0, cursor);
  const current = cursor > 0 ? events[cursor - 1] : undefined;
  const start = dataOf<TurnStartData>(firstOf(visible, 'turn.start'));
  const mcp = mcpInstances(visible);

  return (
    <section
      className={styles.panel}
      aria-label={title}
      tabIndex={0}
      aria-keyshortcuts="ArrowLeft ArrowRight Space Home End"
      onKeyDown={timeTravelKeyHandler(tt.dispatch, tt.state.playing)}
    >
      <header className={styles.header}>
        <h2 className={styles.title}>
          {title} {live && <span className={styles.live}>● live</span>}
        </h2>
        {events.length > 0 && (
          <div className={styles.stats} data-testid="monitor-stats">
            <span className={styles.chip}>
              {visible.length === events.length
                ? `${events.length} events`
                : `${visible.length} of ${events.length} events`}
            </span>
            <span className={styles.chip}>total {formatMs(totalDuration(visible))}</span>
            <span className={styles.chip}>
              {byKind(visible, 'model.response').length} model calls
            </span>
            <span className={styles.chip}>{byKind(visible, 'tool.call').length} tool calls</span>
            <span className={styles.chip}>{byKind(visible, 'retrieval').length} searches</span>
            {start.apiInstance && <span className={styles.chip}>api: {start.apiInstance}</span>}
            {mcp.map((m) => (
              <span key={m} className={styles.chip}>
                mcp: {m}
              </span>
            ))}
          </div>
        )}
      </header>
      {events.length > 0 && (
        <>
          <TimeTravelBar
            events={events}
            state={tt.state}
            dispatch={tt.dispatch}
            cursor={cursor}
            live={live}
          />
          <ThisStep event={current} />
        </>
      )}
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
          <TimelineTab
            events={events}
            cursor={cursor}
            onSeek={(step) => tt.dispatch({ type: 'seek', cursor: step })}
          />
        ) : tab === 'model' ? (
          <ModelTab events={visible} />
        ) : tab === 'retrieval' ? (
          <RetrievalTab events={visible} />
        ) : tab === 'mcp' ? (
          <McpTab events={visible} apiInstance={start.apiInstance} />
        ) : (
          <PromptTab events={visible} />
        )}
      </div>
    </section>
  );
}

/** What the step under the cursor added. */
function ThisStep({ event }: { event?: TraceEvent }) {
  return (
    <details className={styles.thisStep} open aria-label="This step">
      <summary>
        <strong>This step</strong>{' '}
        {event ? (
          <>
            <span className={styles.mono}>#{event.seq}</span>{' '}
            <span className={styles.chip}>{event.kind}</span> {event.title} · +
            {formatMs(event.atMs)}
            {event.durationMs ? ` · ${formatMs(event.durationMs)}` : ''}
          </>
        ) : (
          'before the turn started'
        )}
      </summary>
      {event && <JsonView value={event.data} expandDepth={1} />}
    </details>
  );
}
