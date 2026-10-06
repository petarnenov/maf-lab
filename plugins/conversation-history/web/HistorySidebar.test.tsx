import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ChatContext } from '@maf/plugin-api';
import { jsonResponse, renderWithProviders } from '@maf/testing';
import { HistorySidebar } from './HistorySidebar';
import { createRunsFinished } from './runsFinished';
import type { ConversationSummary } from './types';

const item = (id: string, title = `Title ${id}`): ConversationSummary => ({
  conversationId: id,
  title,
  createdAt: new Date(Date.now() - 3600_000).toISOString(),
  lastActivityAt: new Date(Date.now() - 5 * 60_000).toISOString(),
  turnCount: id === 'c1' ? 1 : 3,
});

const listUrls = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls
    .map((c) => c[0] as string)
    .filter((u) => u.startsWith('/api/conversations?'));

/** The sidebar as the chat renders it, on conversation c2, with the chat's two actions recorded. */
function renderSidebar() {
  const handlers = { openConversation: vi.fn(), startNew: vi.fn() };
  const context: ChatContext = { conversationId: 'c2', openPane: () => {}, ...handlers };
  const runs = createRunsFinished();
  renderWithProviders(<HistorySidebar context={context} runsFinished={runs} />);
  return { ...handlers, runs };
}

describe('HistorySidebar', () => {
  it('lists conversations with relative time, turn count and the active one highlighted', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        jsonResponse({ conversations: [item('c1'), item('c2')], nextCursor: null }),
      ),
    );
    const { openConversation } = renderSidebar();

    const items = await screen.findAllByTestId('history-item');
    expect(items[0]).toHaveTextContent('5 min ago · 1 turn');
    expect(items[1]).toHaveTextContent('3 turns');
    expect(within(items[1]).getByRole('button', { name: /^Title c2/ })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(items[0]).getByRole('button', { name: /^Title c1/ })).not.toHaveAttribute(
      'aria-current',
    );

    await userEvent.click(within(items[0]).getByRole('button', { name: /^Title c1/ }));
    expect(openConversation).toHaveBeenCalledWith('c1');
    // There is one "New conversation", in the chat's header; the list has none of its own.
    expect(screen.queryByRole('button', { name: /New conversation/ })).not.toBeInTheDocument();
  });

  it('debounces search and loads more pages', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      const params = new URL(url, 'http://x').searchParams;
      if (params.get('search'))
        return jsonResponse({ conversations: [item('s1', 'Fee match')], nextCursor: null });
      if (params.get('before') === 'cur-1')
        return jsonResponse({ conversations: [item('c3')], nextCursor: null });
      return jsonResponse({ conversations: [item('c1'), item('c2')], nextCursor: 'cur-1' });
    });
    vi.stubGlobal('fetch', fetchMock);
    renderSidebar();

    await screen.findAllByTestId('history-item');
    await userEvent.click(screen.getByRole('button', { name: 'Load more' }));
    await waitFor(() => expect(screen.getAllByTestId('history-item')).toHaveLength(3));
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
    expect(listUrls(fetchMock)).toContain('/api/conversations?limit=30&before=cur-1');

    await userEvent.type(screen.getByLabelText('Search conversations'), 'fee');
    expect(await screen.findByText('Fee match')).toBeInTheDocument();
    // One request for the whole word, not one per keystroke.
    const searches = listUrls(fetchMock).filter((u) => u.includes('search='));
    expect(searches).toEqual(['/api/conversations?limit=30&search=fee']);
  });

  it('renames inline: Enter saves, Escape cancels, invalid titles are rejected', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'PATCH' && url.startsWith('/api/conversations/'))
        return jsonResponse(undefined, 204);
      return jsonResponse({ conversations: [item('c1')], nextCursor: null });
    });
    vi.stubGlobal('fetch', fetchMock);
    renderSidebar();

    const open = async () => {
      await userEvent.click(await screen.findByRole('button', { name: 'Actions for Title c1' }));
      await userEvent.click(screen.getByRole('menuitem', { name: 'Rename' }));
      return screen.getByLabelText('Conversation title');
    };

    let input = await open();
    await userEvent.clear(input);
    await userEvent.type(input, 'Draft{Escape}');
    expect(screen.queryByLabelText('Conversation title')).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, i]) => i?.method === 'PATCH')).toBe(false);

    input = await open();
    await userEvent.clear(input);
    await userEvent.type(input, '   {Enter}');
    expect(screen.getByRole('alert')).toBeInTheDocument();
    await userEvent.clear(input);
    await userEvent.type(input, `${'x'.repeat(121)}{Enter}`);
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, i]) => i?.method === 'PATCH')).toBe(false);

    await userEvent.clear(input);
    await userEvent.type(input, '  Fee questions {Enter}');
    await waitFor(() =>
      expect(screen.queryByLabelText('Conversation title')).not.toBeInTheDocument(),
    );
    const patch = fetchMock.mock.calls.find(([, i]) => i?.method === 'PATCH')!;
    expect(patch[0]).toBe('/api/conversations/c1');
    expect(JSON.parse(patch[1]!.body as string)).toEqual({ title: 'Fee questions' });
  });

  it('deletes after confirming and has the chat start anew when the one on screen went away', async () => {
    let deleted = false;
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'DELETE') {
        deleted = true;
        return jsonResponse(undefined, 204);
      }
      return jsonResponse({
        conversations: deleted ? [item('c1')] : [item('c1'), item('c2')],
        nextCursor: null,
      });
    });
    vi.stubGlobal('fetch', fetchMock);
    const { startNew } = renderSidebar();

    await userEvent.click(await screen.findByRole('button', { name: 'Actions for Title c2' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    let dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Delete “Title c2”?');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(deleted).toBe(false);

    await userEvent.click(screen.getByRole('button', { name: 'Actions for Title c2' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));
    dialog = screen.getByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(startNew).toHaveBeenCalledTimes(1));
    expect(fetchMock.mock.calls.find(([, i]) => i?.method === 'DELETE')![0]).toBe(
      '/api/conversations/c2',
    );
    await waitFor(() => expect(screen.getAllByTestId('history-item')).toHaveLength(1));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('reads itself again when a run finishes, and only then', async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ conversations: [item('c1')], nextCursor: null }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const { runs } = renderSidebar();
    await screen.findAllByTestId('history-item');
    const reads = () => listUrls(fetchMock).length;
    const before = reads();

    runs.observer.onRunEnd?.('r-1', 'stopped');
    runs.observer.onRunEnd?.('r-1', 'failed');
    await new Promise((r) => setTimeout(r, 0));
    expect(reads()).toBe(before);

    runs.observer.onRunEnd?.('r-2', 'finished');
    await waitFor(() => expect(reads()).toBe(before + 1));
  });
});
