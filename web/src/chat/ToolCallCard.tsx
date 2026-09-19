import type { ToolCallView } from './chatReducer';
import styles from './ToolCallCard.module.css';
import { toolCallLabel } from './toolLabels';

export function ToolCallCard({ call }: { call: ToolCallView }) {
  const state = call.status === 'running' ? 'running' : call.isError ? 'error' : 'finished';
  return (
    <div
      className={`${styles.card} ${styles[state]}`}
      data-testid="tool-call-card"
      data-state={state}
      role="status"
      aria-live="polite"
    >
      <div className={styles.header}>
        <span className={styles.indicator} aria-hidden="true" />
        <span className={styles.label}>{toolCallLabel(call)}</span>
        <code className={styles.tool}>{call.toolName}</code>
      </div>
      {call.argumentSummary && <div className={styles.args}>{call.argumentSummary}</div>}
      {call.status === 'finished' && (
        <div className={styles.result}>
          {call.resultSummary && <span>{call.resultSummary}</span>}
          {call.sourceCount !== undefined && (
            <span className={styles.count}>
              {call.sourceCount} {call.sourceCount === 1 ? 'source' : 'sources'}
            </span>
          )}
        </div>
      )}
    </div>
  );
}
