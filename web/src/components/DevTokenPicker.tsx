import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { apiRequest } from '../api/client';
import type { DevTokenRequest, DevTokenResponse, DevUser } from '../api/types';
import { useAuth } from '../auth/useAuth';
import styles from './Layout.module.css';

const personaKey = (u: DevUser) => `${u.tenantId}/${u.userId}/${u.role}`;

export function DevTokenPicker() {
  const { session, setSession } = useAuth();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const users = useQuery({
    queryKey: ['dev', 'users'],
    queryFn: ({ signal }) => apiRequest<DevUser[]>(null, '/dev/users', { signal }),
    staleTime: Infinity,
  });

  async function choose(key: string) {
    setError(null);
    if (!key) {
      setSession(null);
      return;
    }
    const user = users.data?.find((u) => personaKey(u) === key);
    if (!user) return;
    setBusy(true);
    try {
      const request: DevTokenRequest = {
        userId: user.userId,
        tenantId: user.tenantId,
        role: user.role,
      };
      const minted = await apiRequest<DevTokenResponse>(null, '/dev/token', {
        method: 'POST',
        body: request,
      });
      setSession({ token: minted.token, expiresAt: minted.expiresAt, user });
    } catch {
      setError('Could not mint a dev token.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className={styles.persona}>
      <label>
        <span className={styles.personaLabel}>Persona</span>
        <select
          aria-label="Dev persona"
          value={session ? personaKey(session.user) : ''}
          disabled={busy || users.isLoading}
          onChange={(e) => void choose(e.target.value)}
        >
          <option value="">{users.isError ? 'Dev issuer unavailable' : 'Signed out'}</option>
          {users.data?.map((u) => (
            <option key={personaKey(u)} value={personaKey(u)}>
              {u.label} ({u.tenantId} · {u.role})
            </option>
          ))}
        </select>
      </label>
      {session && (
        <span className={styles.badge} data-testid="current-persona">
          {session.user.userId} · {session.user.tenantId} · {session.user.role}
        </span>
      )}
      {error && (
        <span className={styles.personaError} role="alert">
          {error}
        </span>
      )}
    </div>
  );
}
