import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { A2AActivity } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import styles from '../components/Page.module.css';
import { formatDate } from '../evals/format';

/**
 * What the agents have been doing: what partners asked of this system, what it asked of the reviewer, and
 * every push delivery. A task that is still running can be stopped from here — it is this firm's data being
 * worked on.
 */
export function A2AAdminPage() {
  const { session } = useAuth();
  const api = useApi();
  const queryClient = useQueryClient();

  const activity = useQuery({
    queryKey: ['admin', 'a2a', session?.token],
    queryFn: () => api<A2AActivity>('/api/admin/a2a'),
  });

  const cancel = useMutation({
    mutationFn: (taskId: string) =>
      api<{ taskId: string; state: string }>(`/api/admin/a2a/tasks/${taskId}/cancel`, {
        method: 'POST',
      }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['admin', 'a2a'] }),
  });

  if (activity.isPending) return <p className={styles.muted}>Loading…</p>;
  if (activity.error) return <p role="alert">The A2A activity could not be loaded.</p>;

  const { inbound, outbound, deliveries } = activity.data!;
  const nothing = inbound.length === 0 && outbound.length === 0 && deliveries.length === 0;

  return (
    <section className={styles.page}>
      <header className={styles.header}>
        <h1>Agent to agent</h1>
        <button type="button" onClick={() => void activity.refetch()}>
          Refresh
        </button>
      </header>

      {nothing ? (
        <p className={styles.muted} data-testid="a2a-empty">
          No other agent has talked to this system yet.
        </p>
      ) : (
        <>
          <h2>From partner systems</h2>
          {inbound.length === 0 ? (
            <p className={styles.muted}>Nothing has arrived.</p>
          ) : (
            <table data-testid="a2a-inbound">
              <thead>
                <tr>
                  <th>Partner</th>
                  <th>Operation</th>
                  <th>Task</th>
                  <th>State</th>
                  <th>Updated</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {inbound.map((t) => (
                  <tr key={t.taskId} data-state={t.state}>
                    <td>{t.partnerId}</td>
                    <td>{t.operation}</td>
                    <td>
                      <code>{t.taskId.slice(0, 12)}</code>
                    </td>
                    <td>{t.state}</td>
                    <td>{formatDate(t.updatedAt)}</td>
                    <td>
                      {t.cancellable && (
                        <button
                          type="button"
                          disabled={cancel.isPending}
                          onClick={() => cancel.mutate(t.taskId)}
                        >
                          Cancel
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <h2>Asked of another agent</h2>
          {outbound.length === 0 ? (
            <p className={styles.muted}>Nothing has been asked.</p>
          ) : (
            <table data-testid="a2a-outbound">
              <thead>
                <tr>
                  <th>Agent</th>
                  <th>Task</th>
                  <th>Outcome</th>
                  <th>Took</th>
                  <th>When</th>
                </tr>
              </thead>
              <tbody>
                {outbound.map((c, i) => (
                  <tr key={`${c.taskId}-${i}`}>
                    <td>{c.agent}</td>
                    <td>
                      <code>{c.taskId.slice(0, 12)}</code>
                    </td>
                    <td>{c.outcome}</td>
                    <td>{c.durationMs} ms</td>
                    <td>{formatDate(c.at)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <h2>Push deliveries</h2>
          {deliveries.length === 0 ? (
            <p className={styles.muted}>Nothing has been delivered.</p>
          ) : (
            <table data-testid="a2a-deliveries">
              <thead>
                <tr>
                  <th>Task</th>
                  <th>State</th>
                  <th>Attempts</th>
                  <th>Delivered</th>
                  <th>When</th>
                </tr>
              </thead>
              <tbody>
                {deliveries.map((d, i) => (
                  <tr key={`${d.taskId}-${d.state}-${i}`}>
                    <td>
                      <code>{d.taskId.slice(0, 12)}</code>
                    </td>
                    <td>{d.state}</td>
                    <td>{d.attempts}</td>
                    <td>{d.delivered ? 'yes' : `no — ${d.error ?? 'unknown'}`}</td>
                    <td>{formatDate(d.at)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}
