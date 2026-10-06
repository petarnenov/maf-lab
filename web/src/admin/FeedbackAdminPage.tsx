import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { LabelRequest, ReviewQueueItem } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import styles from '../shared/Page.module.css';
import { formatDate } from '../shared/format';
import { usePlugins } from '../plugins/context';
import { PluginBoundary } from '../plugins/PluginBoundary';
import { contributions } from '../plugins/registry';
import { LabelForm } from './LabelForm';

const SIGNAL_LABELS: Record<string, string> = {
  negative_feedback: 'negative feedback',
  rephrased: 'question rephrased',
  no_tool_on_how_why: 'no tool on how/why',
  zero_retrieval_results: 'zero retrieval results',
  long_answer_without_sources: 'long answer without sources',
  guardrail_blocked: 'prompt refused by the content guard',
  out_of_scope: 'question outside every domain, fixed reply',
  guardrail_withheld: 'content withheld by the content guard',
  answer_not_grounded: 'answer not grounded in what the model read (Jev)',
  answer_not_relevant: 'answer does not address the question (Jev)',
};

export function FeedbackAdminPage() {
  const { session } = useAuth();
  const api = useApi();
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [savedId, setSavedId] = useState<string | null>(null);
  /** The plugin panel open for the selected turn, by plugin and id; none while the turn is just read. */
  const [openPanel, setOpenPanel] = useState<string | null>(null);
  const reviewPanels = contributions(usePlugins(), 'reviewPanels');

  const queue = useQuery({
    queryKey: ['admin', 'feedback', 'queue', session?.token],
    queryFn: ({ signal }) => api<ReviewQueueItem[]>('/api/admin/feedback/queue', { signal }),
  });

  const label = useMutation({
    mutationFn: ({ turnId, request }: { turnId: string; request: LabelRequest }) =>
      api<void>(`/api/admin/feedback/${encodeURIComponent(turnId)}/label`, {
        method: 'POST',
        body: request,
      }),
    onSuccess: (_, { turnId }) => {
      setSavedId(turnId);
      setSelectedId(null);
      void queryClient.invalidateQueries({ queryKey: ['admin', 'feedback', 'queue'] });
    },
  });

  const selected = queue.data?.find((item) => item.turnId === selectedId) ?? null;

  return (
    <div className={styles.page}>
      <h1 className={styles.heading}>Feedback review queue</h1>
      {queue.isLoading && <p className={styles.muted}>Loading queue…</p>}
      {queue.isError && (
        <p className={styles.error} role="alert">
          Could not load the review queue.
        </p>
      )}
      {savedId && (
        <p className={styles.pass} role="status">
          Label saved for turn {savedId}.
        </p>
      )}
      {queue.data && queue.data.length === 0 && (
        <p className={styles.muted}>No flagged turns. 🎉</p>
      )}
      {queue.data && queue.data.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>When</th>
              <th>User</th>
              <th>Question</th>
              <th>Signals</th>
              <th>Tools</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {queue.data.map((item) => (
              <tr
                key={item.turnId}
                className={`${styles.clickable} ${item.turnId === selectedId ? styles.selected : ''}`}
                onClick={() => {
                  setSelectedId(item.turnId);
                  setSavedId(null);
                  setOpenPanel(null);
                  label.reset();
                }}
              >
                <td>{formatDate(item.createdAt)}</td>
                <td>{item.userId}</td>
                <td>{item.question}</td>
                <td>
                  {item.signals.map((s) => (
                    <span key={s} className={styles.tag}>
                      {SIGNAL_LABELS[s] ?? s}
                    </span>
                  ))}
                  {item.feedbackKinds.map((k) => (
                    <span key={k} className={styles.tag}>
                      {k.replace('_', ' ')}
                    </span>
                  ))}
                </td>
                <td className={styles.mono}>
                  {item.toolCalls.length === 0
                    ? 'none'
                    : item.toolCalls.map((c) => `${c.toolName} (${c.sourceCount})`).join(', ')}
                </td>
                <td>{item.labeled ? 'labeled' : 'open'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {selected && (
        <>
          <details>
            <summary>Answer given</summary>
            <p style={{ whiteSpace: 'pre-wrap' }}>{selected.answer}</p>
          </details>
          {/* What the plugins in use show of a reviewed turn (the monitor's stored trace, say). */}
          {reviewPanels.map(({ plugin, item }) => {
            const id = `${plugin}:${item.id}`;
            const open = openPanel === id;
            return (
              <div key={id}>
                <button
                  type="button"
                  aria-expanded={open}
                  onClick={() => setOpenPanel(open ? null : id)}
                >
                  {open ? `Hide ${item.label}` : `Open ${item.label}`}
                </button>
                {open && (
                  <div style={{ height: '70vh', margin: '10px 0' }}>
                    <PluginBoundary plugin={plugin}>
                      {item.render({ turnId: selected.turnId })}
                    </PluginBoundary>
                  </div>
                )}
              </div>
            );
          })}
          <LabelForm
            key={selected.turnId}
            item={selected}
            submitting={label.isPending}
            onSubmit={(request) => label.mutate({ turnId: selected.turnId, request })}
          />
          {label.isError && (
            <p className={styles.error} role="alert">
              Could not save the label.
            </p>
          )}
        </>
      )}
    </div>
  );
}
