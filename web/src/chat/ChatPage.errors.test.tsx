import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, renderWithProviders, run, streamResponse } from '../test/render';
import { ChatPage } from './ChatPage';

const emptyHistory = { conversations: [], nextCursor: null };

async function ask(chat: () => Response | Promise<Response>) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) =>
      url.startsWith('/api/conversations') ? jsonResponse(emptyHistory) : chat(),
    ),
  );
  renderWithProviders(<ChatPage />);
  await userEvent.type(screen.getByLabelText('Message'), 'why did run 4417 fail?');
  await userEvent.click(screen.getByRole('button', { name: 'Send' }));
}

describe('ChatPage when something goes wrong', () => {
  it('says nothing about a failure when the turn succeeded', async () => {
    await ask(() =>
      streamResponse([run.started(), ...run.text('It failed for a missing schedule.'), run.done()]),
    );

    expect(await screen.findByText(/missing schedule/)).toBeInTheDocument();
    expect(screen.queryByTestId('turn-error')).not.toBeInTheDocument();
  });

  it('says what is unavailable and that it is worth trying again', async () => {
    await ask(() => new Response('', { status: 503 }));

    const error = await screen.findByTestId('turn-error');
    expect(error).toHaveAttribute('data-kind', 'unavailable');
    expect(error).toHaveTextContent(/unavailable/i);
    expect(error).toHaveTextContent(/try again/i);
  });

  it('says only that a refused conversation is not available', async () => {
    await ask(() => new Response('', { status: 404 }));

    const error = await screen.findByTestId('turn-error');
    expect(error).toHaveAttribute('data-kind', 'refused');
    expect(error).toHaveTextContent(/not available/i);
    // Why is not the caller's business.
    expect(error.textContent).not.toMatch(/firm|owner|permission|principal/i);
  });

  it('treats a stream that stops part-way as worth retrying', async () => {
    await ask(() => streamResponse([run.started(), ...run.text('half an ans')]));

    const error = await screen.findByTestId('turn-error');
    expect(error).toHaveAttribute('data-kind', 'unavailable');
  });

  it('renders nothing internal, whatever happened', async () => {
    await ask(() => new Response('', { status: 500 }));
    await screen.findByTestId('turn-error');

    const rendered = document.body.textContent ?? '';
    for (const internal of [
      'Exception',
      'at Maf.Lab',
      'System.',
      'Qdrant',
      'SELECT ',
      'http://',
      'Stack',
    ]) {
      expect(rendered).not.toContain(internal);
    }
  });
});
