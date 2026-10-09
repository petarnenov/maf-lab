import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi, useAuth, useUserKey } from '../plugins/api';
import type { ContentAccessGrant } from './contentAccess';
import styles from './Page.module.css';

interface ContentAccessEntries {
  grants: ContentAccessGrant[];
  nextCursor: number | null;
}

function Entries({ user, token }: { user: string; token: string }) {
  const api = useApi();
  const [before, setBefore] = useState<number | null>(null);
  const query = useQuery({
    queryKey: ['operator-content-access', user, token, before],
    queryFn: ({ signal }) =>
      api<ContentAccessEntries>(
        before === null
          ? '/api/admin/content-access'
          : `/api/admin/content-access?before=${before}`,
        { signal },
      ),
    refetchInterval: 5000,
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
    <section aria-label="Operator access to your content">
      <h2 className={styles.subheading}>Operator access to your content</h2>
      <p className={styles.notice}>
        Temporary content access requested by platform operators for this organization.
      </p>
      {query.isPending && <p role="status">Loading operator content access…</p>}
      {query.isError && (
        <p role="alert" className={styles.error}>
          Operator content access could not be loaded.
        </p>
      )}
      {query.data?.grants.length === 0 && (
        <p className={styles.notice}>No operator content access recorded.</p>
      )}
      {query.data && query.data.grants.length > 0 && (
        <div className={styles.scroll}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">Operator</th>
                <th scope="col">Reason</th>
                <th scope="col">Started</th>
                <th scope="col">Expires</th>
                <th scope="col">Ended</th>
              </tr>
            </thead>
            <tbody>
              {query.data.grants.map((grant) => (
                <tr key={grant.id}>
                  <td>{grant.operatorId}</td>
                  <td>{grant.reason}</td>
                  <td>
                    <time dateTime={grant.startedAt}>
                      {new Date(grant.startedAt).toLocaleString()}
                    </time>
                  </td>
                  <td>
                    <time dateTime={grant.expiresAt}>
                      {new Date(grant.expiresAt).toLocaleString()}
                    </time>
                  </td>
                  <td>
                    {grant.endedAt ? (
                      <time dateTime={grant.endedAt}>
                        {new Date(grant.endedAt).toLocaleString()}
                      </time>
                    ) : grant.endRequestedAt ? (
                      'Ending; awaiting confirmation'
                    ) : (
                      'Not confirmed ended'
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <div className={styles.actions}>
        {before !== null && (
          <button onClick={handleLatest} disabled={query.isFetching}>
            Latest content access
          </button>
        )}
        {query.data?.nextCursor != null && (
          <button onClick={handleEarlier} disabled={query.isFetching}>
            Earlier content access
          </button>
        )}
      </div>
    </section>
  );
}

/** Core metadata remains available even when Compliance is absent. */
export function OperatorContentAccess() {
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
