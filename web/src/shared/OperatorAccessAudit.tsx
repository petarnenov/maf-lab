import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi, useAuth, useUserKey } from '../plugins/api';
import styles from './Page.module.css';

interface OperatorEntries {
  actions: { id: number; at: string; operatorId: string }[];
  nextCursor: number | null;
}

function Entries({ user, token }: { user: string; token: string }) {
  const api = useApi();
  const [before, setBefore] = useState<number | null>(null);
  const query = useQuery({
    queryKey: ['operator-access-audit', user, token, before],
    queryFn: ({ signal }) =>
      api<OperatorEntries>(
        before === null
          ? '/api/admin/operator-audit'
          : `/api/admin/operator-audit?before=${before}`,
        { signal },
      ),
  });
  const handleEarlier = () => {
    if (query.data?.nextCursor != null) {
      setBefore(query.data.nextCursor);
    }
  };
  const handleLatest = () => {
    setBefore(null);
  };
  return (
    <section aria-label="Operator entries">
      <h2 className={styles.subheading}>Operator entries</h2>
      <p className={styles.notice}>Records when a platform operator entered this organization.</p>
      {query.isPending && <p role="status">Loading operator entries…</p>}
      {query.isError && (
        <p role="alert" className={styles.error}>
          Operator entries could not be loaded.
        </p>
      )}
      {query.data && (
        <>
          {query.data.actions.length > 0 && (
            <div className={styles.scroll}>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">When</th>
                    <th scope="col">Operator</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.actions.map((entry) => (
                    <tr key={entry.id}>
                      <td>
                        <time dateTime={entry.at}>{new Date(entry.at).toLocaleString()}</time>
                      </td>
                      <td>{entry.operatorId}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {query.data.actions.length === 0 && (
            <p className={styles.notice}>No operator entries recorded.</p>
          )}
        </>
      )}
      <div className={styles.actions}>
        {before !== null && (
          <button onClick={handleLatest} disabled={query.isFetching}>
            Latest entries
          </button>
        )}
        {query.data?.nextCursor != null && (
          <button onClick={handleEarlier} disabled={query.isFetching}>
            Earlier entries
          </button>
        )}
      </div>
    </section>
  );
}

/** Core, content-free entry metadata; the authenticated token selects the tenant. */
export function OperatorAccessAudit() {
  const { session } = useAuth();
  const user = useUserKey();
  return (
    <>
      {session?.user.role === 'TENANT_ADMIN' && (
        <Entries key={JSON.stringify([user, session.token])} user={user} token={session.token} />
      )}
    </>
  );
}
