import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderPluginApp, renderWithProviders } from '@maf/testing';
import feedbackReview from './index';

describe('the feedback review contribution', () => {
  it('contributes its review queue to the tenant dashboard', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse([])),
    );
    const [section] = feedbackReview.tenantAdminSections ?? [];
    expect(section.id).toBe('feedback-review');
    renderWithProviders(<>{section.render()}</>, { session: makeSession('TENANT_ADMIN') });
    expect(await screen.findByText('No flagged turns. 🎉')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Feedback review queue' })).toBeInTheDocument();
  });

  it.each(['USER', 'PLATFORM_ADMIN'] as const)('guards tenant review from %s', (role) => {
    renderPluginApp([feedbackReview], { route: '/admin/feedback', session: makeSession(role) });
    expect(screen.getByRole('alert')).toHaveTextContent('TENANT_ADMIN');
    expect(screen.queryByRole('link', { name: 'Feedback review' })).toBeNull();
    expect(screen.queryByRole('heading', { name: 'Feedback review queue' })).toBeNull();
  });
});
