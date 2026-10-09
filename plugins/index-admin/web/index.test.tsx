import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderPluginApp, renderWithProviders } from '@maf/testing';
import indexAdmin from './index';

describe('the index administration contribution', () => {
  it('contributes its screen to the platform dashboard', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    const [section] = indexAdmin.platformAdminSections ?? [];
    expect(section.id).toBe('index-admin');
    renderWithProviders(<>{section.render()}</>, { session: makeSession('PLATFORM_ADMIN') });
    expect(
      await screen.findByRole('heading', { name: 'Index administration' }),
    ).toBeInTheDocument();
  });

  it.each(['USER', 'TENANT_ADMIN'] as const)('guards its standalone screen from %s', (role) => {
    renderPluginApp([indexAdmin], { route: '/admin/index', session: makeSession(role) });
    expect(screen.getByRole('alert')).toHaveTextContent('PLATFORM_ADMIN');
    expect(screen.queryByRole('link', { name: 'Index admin' })).toBeNull();
    expect(screen.queryByRole('heading', { name: 'Index administration' })).toBeNull();
  });

  it('offers its standalone screen to a platform operator when the shell is absent', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    renderPluginApp([indexAdmin], {
      route: '/admin/index',
      session: makeSession('PLATFORM_ADMIN'),
    });
    expect(
      await screen.findByRole('heading', { name: 'Index administration' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Index admin' })).toHaveAttribute(
      'href',
      '/admin/index',
    );
  });
});
