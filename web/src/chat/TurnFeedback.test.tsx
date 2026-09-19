import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { TurnFeedback } from './TurnFeedback';

describe('TurnFeedback', () => {
  it('posts a structured feedback event when "wrong document" is clicked', async () => {
    const fetchMock = vi.fn(async () => jsonResponse({ feedbackId: 'f1' }, 202));
    vi.stubGlobal('fetch', fetchMock);

    renderWithProviders(<TurnFeedback conversationId="conv-1" turnId="turn-9" />, {
      session: makeSession('ADVISOR'),
    });
    await userEvent.click(screen.getByRole('button', { name: 'Wrong document' }));

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Wrong document/ })).toHaveAttribute(
        'aria-pressed',
        'true',
      ),
    );
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/feedback');
    expect(init.method).toBe('POST');
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer token-ADVISOR');
    expect(JSON.parse(init.body as string)).toEqual({
      conversationId: 'conv-1',
      turnId: 'turn-9',
      kind: 'wrong_document',
    });
  });

  it('shows a retry state when the post fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({}, 500)),
    );
    renderWithProviders(<TurnFeedback conversationId="c" turnId="t" />);
    await userEvent.click(screen.getByRole('button', { name: 'Wrong tool' }));
    expect(await screen.findByText(/retry/)).toBeInTheDocument();
  });
});
