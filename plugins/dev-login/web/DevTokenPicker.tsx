import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { DevTokenRequest, DevTokenResponse, DevUser } from './types';
import { useAuth, useApi } from '@maf/plugin-api';
import styles from './DevTokenPicker.module.css';

const personaKey = (u: DevUser) => `${u.tenantId}/${u.userId}/${u.role}`;

export function DevTokenPicker() {
  const { session, setSession } = useAuth();
  const api = useApi();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const users = useQuery({
    queryKey: ['dev', 'users'],
    queryFn: ({ signal }) => api<DevUser[]>('/dev/users', { signal }),
    staleTime: Infinity,
  });

  async function choose(key: string, tenantId?: string) {
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
        tenantId: tenantId ?? user.tenantId,
        role: user.role,
        audience: 'api',
      };
      const minted = await api<DevTokenResponse>('/dev/token', {
        method: 'POST',
        body: request,
      });
      setSession({
        token: minted.token,
        expiresAt: minted.expiresAt,
        user: { ...user, tenantId: request.tenantId! },
      });
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
          value={
            session?.user.userId === 'operator'
              ? personaKey(users.data?.find((u) => u.userId === 'operator') ?? session.user)
              : session
                ? personaKey(session.user)
                : ''
          }
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
      {session?.user.userId === 'operator' && (
        <label>
          <span className={styles.personaLabel}>Organization</span>
          <select
            aria-label="Operator organization"
            value={session.user.tenantId}
            disabled={busy || users.isLoading}
            onChange={(event) =>
              void choose(
                personaKey(users.data!.find((u) => u.userId === 'operator')!),
                event.target.value,
              )
            }
          >
            {[...new Set(users.data?.map((u) => u.tenantId) ?? [])].map((tenant) => (
              <option key={tenant} value={tenant}>
                {tenant}
              </option>
            ))}
          </select>
        </label>
      )}
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
