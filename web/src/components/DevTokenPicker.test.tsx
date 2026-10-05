import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadSession, saveSession } from '../auth/session';
import { jsonResponse, renderWithProviders } from '../test/render';
import { DevTokenPicker } from './DevTokenPicker';

const adam = { userId: 'adam', tenantId: 'firm-a', role: 'USER', label: 'Adam' };

describe('DevTokenPicker (rename-firm-to-tenant)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
  });

  it('asks for a token with only the core shape: user, tenant and role', async () => {
    const fetchMock = vi.fn((url: string) =>
      Promise.resolve(
        url.endsWith('/dev/users')
          ? jsonResponse([adam])
          : jsonResponse({ token: 't', expiresAt: new Date(Date.now() + 3600_000).toISOString() }),
      ),
    );
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(<DevTokenPicker />, { session: null });

    await screen.findByRole('option', { name: /Adam/ });
    await userEvent.selectOptions(screen.getByLabelText('Dev persona'), 'firm-a/adam/USER');

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        expect.stringMatching(/\/dev\/token$/),
        expect.anything(),
      ),
    );
    const [, init] = fetchMock.mock.calls.find(([url]) =>
      String(url).endsWith('/dev/token'),
    ) as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({
      userId: 'adam',
      tenantId: 'firm-a',
      role: 'USER',
    });
    expect(await screen.findByTestId('current-persona')).toHaveTextContent('adam · firm-a · USER');
  });

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
});
