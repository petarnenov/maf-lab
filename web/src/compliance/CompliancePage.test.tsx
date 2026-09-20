import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { ActionPage, AuditAction, ChainReport, ExportManifest } from '../api/types';
import { jsonResponse, makeSession, renderWithProviders } from '../test/render';
import { CompliancePage } from './CompliancePage';

const intact: ChainReport = {
  intact: true,
  checked: 5,
  unchained: 65,
  from: '2026-09-20T07:26:48Z',
  to: '2026-09-20T07:30:50Z',
  head: 'd90b5e24ce6d7a975b9015b87b69cb8b1f3b11f6c53bbd1b1fff8bb20cfcc851',
  firstBrokenId: null,
  reason: '65 row(s) predate the chain',
};

const action = (id: number, overrides: Partial<AuditAction> = {}): AuditAction => ({
  id,
  at: '2026-09-20T07:26:48Z',
  principalId: 'adam',
  kind: 'tool',
  action: 'search_documents',
  arguments: '',
  outcome: 'ok',
  durationMs: 94,
  conversationId: 'c_1',
  turnId: 't_1',
  hash: 'abc',
  ...overrides,
});

const manifest: ExportManifest = {
  firmId: 'firm-a',
  subjectUserId: null,
  from: '2026-09-01T00:00:00Z',
  to: '2026-09-30T00:00:00Z',
  generatedAt: '2026-09-20T08:00:00Z',
  by: 'alice',
  counts: { conversations: 51, turns: 63, actions: 65 },
  sha256: 'a9e0782f5c2555f0b0e8c7485b77cf14acc735630f9ad66d059630839d75633a',
  auditChainHead: 'd90b5e24ce6d7a97',
};

/** Routes each call by URL; `actions` may answer differently per call for paging. */
function stubFetch(
  options: {
    chain?: ChainReport;
    actions?: (url: string) => ActionPage;
    exportPackage?: unknown;
  } = {},
) {
  const fetchMock = vi.fn(async (url: string) => {
    if (url.startsWith('/api/admin/compliance/verify'))
      return jsonResponse(options.chain ?? intact);
    if (url.startsWith('/api/admin/compliance/actions')) {
      return jsonResponse(options.actions?.(url) ?? { actions: [action(1)], nextCursor: null });
    }
    if (url.startsWith('/api/admin/compliance/export')) {
      return jsonResponse(
        options.exportPackage ?? { manifest, conversations: [], turns: [], actions: [] },
      );
    }
    return jsonResponse({}, 404);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const admin = { session: makeSession('FIRM_ADMIN') };

describe('CompliancePage', () => {
  it('says in words that the chain is intact, and what the chain does not promise', async () => {
    stubFetch();
    renderWithProviders(<CompliancePage />, admin);

    const panel = await screen.findByRole('region', { name: 'Audit chain' });
    expect(await within(panel).findByText(/The chain is intact/)).toBeInTheDocument();
    expect(panel).toHaveTextContent('5 records checked, 65 predating the chain');
    expect(panel).toHaveTextContent(intact.head!);
    expect(panel).toHaveTextContent(/detects tampering; it does not prevent it/);
  });

  it('names the record that broke the chain and says earlier records are unaffected', async () => {
    stubFetch({
      chain: {
        ...intact,
        intact: false,
        head: null,
        firstBrokenId: 66,
        reason: "the row's content does not match its digest",
      },
    });
    renderWithProviders(<CompliancePage />, admin);

    const panel = await screen.findByRole('region', { name: 'Audit chain' });
    expect(await within(panel).findByText(/The chain is broken/)).toBeInTheDocument();
    expect(panel).toHaveTextContent('#66');
    expect(panel).toHaveTextContent("the row's content does not match its digest");
    expect(panel).toHaveTextContent(/Records written before it are unaffected/);
  });

  it('lists actions newest first and says when an action carried no identifiers', async () => {
    stubFetch({
      actions: () => ({
        actions: [
          action(3, {
            kind: 'compliance.export',
            action: 'compliance.export',
            arguments: 'from=2026-09-01 to=2026-09-30',
          }),
          action(2, {
            kind: 'conversation.delete',
            action: 'conversation.delete',
            arguments: 'conversationId=c_1',
          }),
          action(1),
        ],
        nextCursor: null,
      }),
    });
    renderWithProviders(<CompliancePage />, admin);

    const table = await screen.findByRole('table', { name: 'Actions' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent('compliance.export');
    expect(rows[2]).toHaveTextContent('search_documents');
    // The search carried no identifiers on purpose; an empty cell would read as missing data.
    expect(within(rows[2]).getByText('none recorded')).toBeInTheDocument();
  });

  it('filters by person and kind', async () => {
    const fetchMock = stubFetch();
    renderWithProviders(<CompliancePage />, admin);
    await screen.findByRole('table', { name: 'Actions' });

    await userEvent.type(screen.getByRole('searchbox', { name: 'Person' }), 'adam');
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Kind' }),
      'conversation.delete',
    );

    await waitFor(() => {
      const urls = fetchMock.mock.calls.map((c) => String(c[0]));
      expect(
        urls.some((u) => u.includes('userId=adam') && u.includes('kind=conversation.delete')),
      ).toBe(true);
    });
  });

  it('appends older records when asked for more', async () => {
    stubFetch({
      actions: (url) =>
        url.includes('before=2')
          ? { actions: [action(1)], nextCursor: null }
          : { actions: [action(3), action(2)], nextCursor: 2 },
    });
    renderWithProviders(<CompliancePage />, admin);

    const table = await screen.findByRole('table', { name: 'Actions' });
    expect(within(table).getAllByRole('row')).toHaveLength(3); // header + 2

    await userEvent.click(screen.getByRole('button', { name: 'Load older' }));

    await waitFor(() => expect(within(table).getAllByRole('row')).toHaveLength(4));
    expect(screen.queryByRole('button', { name: 'Load older' })).not.toBeInTheDocument();
  });

  it('keeps "load older" after a filter is applied and cleared again', async () => {
    // Returning to a filter serves the first page from cache, so the cursor must come from the data, not from
    // state left behind by the previous filter.
    stubFetch({
      actions: (url) =>
        url.includes('userId=adam')
          ? { actions: [action(9)], nextCursor: null }
          : { actions: [action(3), action(2)], nextCursor: 2 },
    });
    renderWithProviders(<CompliancePage />, admin);
    await screen.findByRole('table', { name: 'Actions' });
    expect(screen.getByRole('button', { name: 'Load older' })).toBeInTheDocument();

    const person = screen.getByRole('searchbox', { name: 'Person' });
    await userEvent.type(person, 'adam');
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Load older' })).not.toBeInTheDocument(),
    );

    await userEvent.clear(person);

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Load older' })).toBeInTheDocument(),
    );
  });

  it('says when nothing was recorded for the chosen filters', async () => {
    stubFetch({ actions: () => ({ actions: [], nextCursor: null }) });
    renderWithProviders(<CompliancePage />, admin);

    expect(
      await screen.findByText(/No actions were recorded for these filters/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Actions' })).not.toBeInTheDocument();
  });

  it('is refused to a non-admin, as the other admin screens are', async () => {
    stubFetch();
    const { RequireAdmin } = await import('../components/RequireAdmin');
    renderWithProviders(
      <RequireAdmin>
        <CompliancePage />
      </RequireAdmin>,
      { session: makeSession('ADVISOR') },
    );

    expect(screen.getByText('Access denied')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Audit chain' })).not.toBeInTheDocument();
  });

  it('exports the chosen range and shows the manifest afterwards', async () => {
    const fetchMock = stubFetch();
    // jsdom has no object URLs or downloads; the click is what matters, not the file.
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:x', revokeObjectURL: () => {} });
    renderWithProviders(<CompliancePage />, admin);
    await screen.findByRole('region', { name: 'Export' });

    await userEvent.type(screen.getByLabelText('Export from'), '2026-09-01');
    await userEvent.type(screen.getByLabelText('Export to'), '2026-09-30');
    await userEvent.click(screen.getByRole('button', { name: 'Download package' }));

    const region = await screen.findByRole('region', { name: 'Export' });
    await waitFor(() => expect(region).toHaveTextContent('Produced by alice'));
    expect(region).toHaveTextContent(manifest.sha256);
    expect(region).toHaveTextContent(manifest.auditChainHead!);
    expect(region).toHaveTextContent('63'); // turns count
    const exportUrl = fetchMock.mock.calls
      .map((c) => String(c[0]))
      .find((u) => u.includes('/export'));
    expect(exportUrl).toContain('from=2026-09-01');
    expect(exportUrl).toContain('to=2026-09-30');
  });
});
