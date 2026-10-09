import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderWithProviders } from '../test';
import { PluginSwitches, type AdminPlugin } from './PluginSwitches';

const billing: AdminPlugin = {
  name: 'billing',
  description: 'Fee billing',
  scope: 'tenant',
  health: 'ok',
  environments: ['dev', 'qa', 'stage', 'prod'],
  private: false,
  allowed: true,
  enabled: true,
};
function fixture(platform = false, extra: AdminPlugin[] = []) {
  let current = { ...billing };
  const writes: { path: string; body: Record<string, boolean> }[] = [];
  const fetchMock = vi.fn(async (path: string, init?: RequestInit) => {
    if (init?.method === 'PUT') {
      const body = JSON.parse(String(init.body)) as Record<string, boolean>;
      writes.push({ path, body });
      current = { ...current, ...body };
      return jsonResponse(current);
    }
    return jsonResponse([current, ...extra]);
  });
  vi.stubGlobal('fetch', fetchMock);
  renderWithProviders(<PluginSwitches platform={platform} />, {
    session: makeSession(platform ? 'PLATFORM_ADMIN' : 'TENANT_ADMIN'),
  });
  return { writes, fetchMock };
}

describe('Plugin permission switches', () => {
  it('Esc and Cancel keep a disabled proposal checked without writing; confirm writes once', async () => {
    const { writes } = fixture();
    const user = userEvent.setup();
    const input = await screen.findByRole('checkbox', { name: 'billing' });
    await user.click(input);
    expect(input).toBeChecked();
    expect(screen.getByRole('dialog')).toHaveTextContent('Its data is kept.');
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(input).toHaveFocus();
    expect(writes).toEqual([]);
    await user.click(input);
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(writes).toEqual([]);
    await user.click(input);
    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    await waitFor(() => expect(input).not.toBeChecked());
    expect(writes).toEqual([{ path: '/api/admin/plugins/billing', body: { enabled: false } }]);
    await user.click(input);
    await waitFor(() => expect(input).toBeChecked());
    expect(writes.at(-1)?.body).toEqual({ enabled: true });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('operator withdrawal confirms switch-off; installation plugins have no checkbox', async () => {
    const { writes } = fixture(true, [
      { ...billing, name: 'monitor', scope: 'installation' },
      { ...billing, name: 'private-code', private: true, health: 'unavailable' },
    ]);
    const user = userEvent.setup();
    expect(await screen.findByText('monitor')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: 'monitor' })).not.toBeInTheDocument();
    expect(screen.getByText('Private')).toBeInTheDocument();
    expect(screen.getByText('Unavailable right now.')).toBeInTheDocument();
    await user.click(screen.getByRole('checkbox', { name: 'billing' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('also switched off');
    expect(writes).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    await waitFor(() =>
      expect(writes).toEqual([{ path: '/api/platform/plugins/billing', body: { allowed: false } }]),
    );
  });

  it('a failed write leaves the previous checkbox and exposes the error', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_path: string, init?: RequestInit) =>
        jsonResponse(
          init?.method === 'PUT' ? { error: 'Not allowed' } : [{ ...billing, enabled: false }],
          init?.method === 'PUT' ? 403 : 200,
        ),
      ),
    );
    renderWithProviders(<PluginSwitches platform={false} />, {
      session: makeSession('TENANT_ADMIN'),
    });
    const input = await screen.findByRole('checkbox', { name: 'billing' });
    fireEvent.click(input);
    expect(await screen.findByRole('alert')).toHaveTextContent('Access denied');
    expect(input).not.toBeChecked();
  });
});
