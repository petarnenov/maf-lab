import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ConversationSummary } from '../api/types';
import { jsonResponse, renderWithProviders } from '../test/render';
import { HistorySidebar } from './HistorySidebar';

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

function renderSidebar(props: Partial<Parameters<typeof HistorySidebar>[0]> = {}) {
  const handlers = { onSelect: vi.fn(), onNew: vi.fn(), onDeleted: vi.fn() };
  renderWithProviders(<HistorySidebar activeId="c2" {...handlers} {...props} />);
  return handlers;
}

describe('HistorySidebar', () => {
  it('lists conversations with relative time, turn count and the active one highlighted', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        jsonResponse({ conversations: [item('c1'), item('c2')], nextCursor: null }),
      ),
    );
    const { onSelect, onNew } = renderSidebar();

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
    expect(onSelect).toHaveBeenCalledWith('c1');
    await userEvent.click(screen.getByRole('button', { name: '＋ New conversation' }));
    expect(onNew).toHaveBeenCalled();
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

  it('deletes after confirming and tells the page when the active one went away', async () => {
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
    const { onDeleted } = renderSidebar();

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

    await waitFor(() => expect(onDeleted).toHaveBeenCalledWith('c2'));
    expect(fetchMock.mock.calls.find(([, i]) => i?.method === 'DELETE')![0]).toBe(
      '/api/conversations/c2',
    );
    await waitFor(() => expect(screen.getAllByTestId('history-item')).toHaveLength(1));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('collapses to a rail', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ conversations: [], nextCursor: null })),
    );
    const onToggleCollapsed = vi.fn();
    renderSidebar({ collapsed: true, onToggleCollapsed });
    expect(screen.queryByLabelText('Search conversations')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Expand history' }));
    expect(onToggleCollapsed).toHaveBeenCalled();
  });
});
