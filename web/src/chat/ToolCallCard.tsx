import { usePlugins } from '../plugins/context';
import { toolLabels } from '../plugins/registry';
import type { ToolCallView } from './chatReducer';
import styles from './ToolCallCard.module.css';
import { toolCallLabel } from './toolLabels';

export function ToolCallCard({ call }: { call: ToolCallView }) {
  const labels = toolLabels(usePlugins());
  const state =
    call.status === 'running'
      ? 'running'
      : call.isError
        ? 'error'
        : call.stopped
          ? 'stopped'
          : 'finished';
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
        <span className={styles.label}>{toolCallLabel(call, labels)}</span>
        <code className={styles.tool}>{call.toolName}</code>
      </div>
      {call.argumentSummary && <div className={styles.args}>{call.argumentSummary}</div>}
      {call.status === 'finished' && (
        <div className={styles.result}>
          {call.stopped && !call.resultSummary && <span>stopped</span>}
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
