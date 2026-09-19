import type { ReactNode } from 'react';
import { useAuth } from '../auth/useAuth';
import styles from './Page.module.css';

/** Renders children only for FIRM_ADMIN. The API enforces this too; this is the UX guard. */
export function RequireAdmin({ children }: { children: ReactNode }) {
  const { session } = useAuth();
  if (!session) {
    return <p className={styles.notice}>Pick a dev persona in the header to continue.</p>;
  }
  if (session.user.role !== 'FIRM_ADMIN') {
    return (
      <div className={styles.denied} role="alert">
        <h1>Access denied</h1>
        <p>This page requires the FIRM_ADMIN role. You are signed in as {session.user.role}.</p>
      </div>
    );
  }
  return <>{children}</>;
}
