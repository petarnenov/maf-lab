import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse, renderWithProviders } from '@maf/testing';
import { DevTokenPicker } from './DevTokenPicker';

const adam = { userId: 'adam', tenantId: 'firm-a', role: 'USER', label: 'Adam' };

describe('DevTokenPicker (rename-firm-to-tenant)', () => {
  it('changes the operator organization by minting a new API token and keeps its identity', async () => {
    const operator = {
      userId: 'operator',
      tenantId: 'firm-a',
      role: 'PLATFORM_ADMIN',
      label: 'Operator',
    };
    const fetchMock = vi.fn((url: string) =>
      Promise.resolve(
        url.endsWith('/dev/users')
          ? jsonResponse([operator, adam, { ...adam, userId: 'bob', tenantId: 'firm-b' }])
          : jsonResponse({
              token: 'org-token',
              expiresAt: new Date(Date.now() + 3600_000).toISOString(),
            }),
      ),
    );
    vi.stubGlobal('fetch', fetchMock);
    renderWithProviders(<DevTokenPicker />, { session: null });
    await screen.findByRole('option', { name: /Operator/ });
    await userEvent.selectOptions(
      screen.getByLabelText('Dev persona'),
      'firm-a/operator/PLATFORM_ADMIN',
    );
    await screen.findByLabelText('Operator organization');
    await userEvent.selectOptions(screen.getByLabelText('Operator organization'), 'firm-b');
    await waitFor(() =>
      expect(screen.getByTestId('current-persona')).toHaveTextContent(
        'operator · firm-b · PLATFORM_ADMIN',
      ),
    );
    const requests = fetchMock.mock.calls.filter(([url]) => url.endsWith('/dev/token'));
    expect(requests).toHaveLength(2);
    const [, init] = requests.at(-1) as unknown as [string, RequestInit];
    expect(JSON.parse(String(init.body))).toEqual({
      userId: 'operator',
      tenantId: 'firm-b',
      role: 'PLATFORM_ADMIN',
      audience: 'api',
    });
  });
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
      audience: 'api',
    });
    expect(await screen.findByTestId('current-persona')).toHaveTextContent('adam · firm-a · USER');
  });
});
