import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { definePlugin } from '../plugins/api';
import { renderPluginApp } from '../test/pluginApp';
const adminPage = definePlugin({
  name: 'fixture',
  routes: [{ path: 'admin/fixture', element: <h1>Fixture admin page</h1>, admin: true }],
});
import { jsonResponse, makeSession } from '../test/render';

describe('admin routes', () => {
  it('denies a USER opening /admin/fixture', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderPluginApp([adminPage], { session: makeSession('USER'), route: '/admin/fixture' });
    expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
    expect(screen.queryByText('Fixture admin page')).not.toBeInTheDocument();
  });

  it('denies a READ_ONLY user opening /admin/fixture', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderPluginApp([adminPage], { session: makeSession('READ_ONLY'), route: '/admin/fixture' });
    expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
  });

  it('lets a TENANT_ADMIN open /admin/fixture', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderPluginApp([adminPage], {
      session: makeSession('TENANT_ADMIN'),
      route: '/admin/fixture',
    });
    expect(await screen.findByText('Fixture admin page')).toBeInTheDocument();
  });
});
