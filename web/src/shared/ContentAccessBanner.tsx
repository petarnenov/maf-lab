import { useAuth } from '../plugins/api';
import { useContentAccess } from './contentAccess';
import styles from './ContentAccess.module.css';

function Banner() {
  const { session } = useAuth();
  const { query } = useContentAccess();
  const grant = query.data?.grant;
  const unresolved = grant != null && grant.endedAt == null;
  return (
    <aside className={styles.banner} aria-label="Platform operator access">
      <p>
        Acting for organization {session?.user.tenantId} as {session?.user.userId}.
      </p>
      {unresolved && (
        <p role="status">
          {query.data?.active && !grant.endRequestedAt
            ? 'Content access is active.'
            : 'Content access is ending; completion is not confirmed.'}{' '}
          Reason: {grant.reason}. Started{' '}
          <time dateTime={grant.startedAt}>{new Date(grant.startedAt).toLocaleString()}</time>;
          expires{' '}
          <time dateTime={grant.expiresAt}>{new Date(grant.expiresAt).toLocaleString()}</time>.
        </p>
      )}
      {query.isPending && <p role="status">Checking content access…</p>}
      {query.isError && (
        <p role="alert">
          Content access status could not be confirmed. Completion has not been acknowledged.
        </p>
      )}
    </aside>
  );
}

export function ContentAccessBanner() {
  const { session } = useAuth();
  return (
    <>
      {session?.user.role === 'PLATFORM_ADMIN' && (
        <Banner key={JSON.stringify([session.user, session.token])} />
      )}
    </>
  );
}
