import { expect, it } from 'vitest';
import { loadSession, saveSession } from './session';
const adam = { userId: 'adam', tenantId: 'firm-a', role: 'USER' as const, label: 'Adam' };
it('drops a session saved before the rename, which has no tenant', () => {
  const expiresAt = new Date(Date.now() + 3600_000).toISOString();
  sessionStorage.setItem(
    'maf-lab.session',
    JSON.stringify({
      token: 't',
      expiresAt,
      user: { userId: 'adam', firmId: 'firm-a', role: 'ADVISOR', label: 'Adam' },
    }),
  );
  expect(loadSession()).toBeNull();

  saveSession({ token: 't', expiresAt, user: { ...adam, role: 'USER' } });
  expect(loadSession()?.user.tenantId).toBe('firm-a');
});
