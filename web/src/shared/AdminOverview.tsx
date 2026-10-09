import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi, useUserKey } from '../plugins/api';
import styles from './Page.module.css';

interface Overview {
  turns?: number;
  activeUsers?: number;
  domains?: Record<string, number>;
  actions?: {
    id: number;
    at: string;
    actor: string;
    action: string;
    arguments: string;
    outcome: string;
  }[];
  nextCursor?: number | null;
}

/** Content-free tenant usage and permission audit. The token alone selects the organization. */
export function AdminOverview({ platform }: { platform: boolean }) {
  const api = useApi();
  const user = useUserKey();
  const path = platform ? '/api/platform/plugin-audit' : '/api/admin/usage';
  const [before, setBefore] = useState<number | null>(null);
  const query = useQuery({
    queryKey: ['admin-overview', path, user, before],
    queryFn: ({ signal }) =>
      api<Overview>(before === null ? path : `${path}?before=${before}`, { signal }),
  });
  return (
    <section aria-label={platform ? 'Permission audit' : 'Tenant usage'}>
      <h2 className={styles.subheading}>
        {platform ? 'Permission audit' : 'Usage over the last 30 days'}
      </h2>
      {query.isPending && <p role="status">Loading…</p>}
      {query.isError && (
        <p role="alert" className={styles.error}>
          {query.error.message}
        </p>
      )}
      {query.data &&
        (platform ? (
          <>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th>When</th>
                  <th>Actor</th>
                  <th>Action</th>
                  <th>Change</th>
                </tr>
              </thead>
              <tbody>
                {query.data.actions?.map((row) => (
                  <tr key={row.id}>
                    <td>{new Date(row.at).toLocaleString()}</td>
                    <td>{row.actor}</td>
                    <td>{row.action}</td>
                    <td>{row.arguments}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {query.data.actions?.length === 0 && (
              <p className={styles.notice}>No permission changes recorded.</p>
            )}
            <div className={styles.actions}>
              {before !== null && <button onClick={() => setBefore(null)}>Latest changes</button>}
              {query.data.nextCursor != null && (
                <button onClick={() => setBefore(query.data!.nextCursor!)}>Earlier changes</button>
              )}
            </div>
          </>
        ) : (
          <>
            <p>
              {query.data.turns} turns · {query.data.activeUsers} active users
            </p>
            <ul>
              {Object.entries(query.data.domains ?? {}).map(([name, count]) => (
                <li key={name}>
                  {name}: {count} turns
                </li>
              ))}
            </ul>
          </>
        ))}
    </section>
  );
}
