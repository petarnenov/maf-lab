import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { CodeSnippet } from '../api/types';
import { jsonResponse, renderWithProviders, run, streamResponse } from '../test/render';
import { agentFetch } from '../test/agentFetch';
import { ChatPage } from './ChatPage';
import { codeSnippetsOf, groupByFile } from './codeSnippets';
import { CodeSnippetsPanel } from './CodeSnippetsPanel';

const snippet = (
  path: string,
  startLine: number,
  symbol: string | null,
  text: string,
): CodeSnippet => ({
  path,
  startLine,
  endLine: startLine + text.split('\n').length - 1,
  symbol,
  section: symbol ? `${path} > ${symbol}` : path,
  kind: path.endsWith('.md') ? 'docs' : 'code',
  language: 'csharp',
  score: 0.5,
  snippet: text,
});

const filter = snippet(
  'src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs',
  124,
  'TenantFilter.For',
  'public static Filter For(Principal principal)\n{\n    return new Filter();\n}',
);
const query = snippet(
  'src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs',
  38,
  'TenantScopedSearch.QueryAsync',
  'public async Task QueryAsync()',
);
const decision = snippet('DECISIONS.md', 10, null, 'Tenant filter in one place.');

function Harness({ question }: { question: string }) {
  const [active, setActive] = useState(false);
  return (
    <>
      <button onClick={() => setActive(true)}>show</button>
      <CodeSnippetsPanel question={question} active={active} />
    </>
  );
}

const emptyHistory = { conversations: [], nextCursor: null };

describe('groupByFile', () => {
  it('groups per file in rank order, each file in line order', () => {
    const groups = groupByFile([filter, decision, query]);
    expect(groups.map((g) => g.path)).toEqual([filter.path, 'DECISIONS.md']);
    expect(groups[0].snippets.map((s) => s.startLine)).toEqual([38, 124]);
  });
});

describe('CodeSnippetsPanel', () => {
  it('fetches nothing while hidden, then shows the files with numbered lines', async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        results: [filter, decision],
        totalMatches: 2,
        truncated: false,
        refineHint: null,
      }),
    );
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderWithProviders(<Harness question="where is the tenant filter built?" />);
    expect(fetchMock).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'show' }));
    const files = await screen.findAllByTestId('code-file');
    expect(files).toHaveLength(2);
    expect(files[0]).toHaveTextContent('src/Maf.Lab.Retrieval/Store/TenantScopedSearch.cs');
    expect(within(files[0]).getByText('lines 124–127')).toBeInTheDocument();
    expect(within(files[0]).getByText('TenantFilter.For')).toBeInTheDocument();
    expect(within(files[0]).getByText('127')).toBeInTheDocument();

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/code/snippets');
    expect(JSON.parse(init.body as string)).toEqual({
      question: 'where is the tenant filter built?',
    });
  });

  it('says so when nothing matches, and when code search is down', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async () =>
          jsonResponse({ results: [], totalMatches: 0, truncated: false, refineHint: 'Rephrase.' }),
        ),
      ),
    );
    const { unmount } = renderWithProviders(
      <CodeSnippetsPanel question="capital of France?" active />,
    );
    expect(await screen.findByText(/No code matches this question/)).toBeInTheDocument();
    unmount();

    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async () => jsonResponse({ detail: 'Code search is unavailable right now.' }, 503)),
      ),
    );
    renderWithProviders(<CodeSnippetsPanel question="anything" active />);
    expect(await screen.findByRole('alert')).toHaveTextContent('Code search is unavailable');
  });
});

describe('ChatPage right pane', () => {
  it('opens on Behind the scenes and shows the code for the turn’s question on Code snippets', async () => {
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url.startsWith('/api/conversations')) return jsonResponse(emptyHistory);
      if (url === '/api/code/snippets') {
        const { question } = JSON.parse(init!.body as string) as { question: string };
        return jsonResponse({
          results: question.includes('tenant') ? [filter] : [decision],
          totalMatches: 1,
          truncated: false,
          refineHint: null,
        });
      }
      return streamResponse([run.delta('An answer.'), run.done('conv-1', `t-${Math.random()}`)]);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderWithProviders(<ChatPage />);
    const tabs = screen.getByRole('tablist', { name: 'Right pane' });
    expect(within(tabs).getByRole('tab', { name: 'Behind the scenes' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(screen.getByRole('region', { name: 'Behind the scenes' })).toBeVisible();

    await userEvent.type(screen.getByLabelText('Message'), 'where is the tenant filter built?');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));
    await screen.findByText('An answer.');
    expect(fetchMock.mock.calls.some(([u]) => u === '/api/code/snippets')).toBe(false);

    await userEvent.click(within(tabs).getByRole('tab', { name: 'Code snippets' }));
    expect(await screen.findByText('TenantFilter.For')).toBeInTheDocument();
    expect(
      screen.getByRole('region', { name: 'Behind the scenes', hidden: true }),
    ).not.toBeVisible();

    // Back to the monitor: it is still there, unchanged.
    await userEvent.click(within(tabs).getByRole('tab', { name: 'Behind the scenes' }));
    expect(screen.getByRole('region', { name: 'Behind the scenes' })).toBeVisible();
  });
});

const codeSource = {
  docId: 'src/Maf.Lab.Api/Agent/ToolSource.cs',
  sectionPath: 'src/Maf.Lab.Api/Agent/ToolSource.cs:17-27 › ConfirmedCall',
  sourcePath: 'src/Maf.Lab.Api/Agent/ToolSource.cs',
  snippet: '/// <param name="idempotencyKey">\npublic delegate Task ConfirmedCall();',
  kind: 'code',
  startLine: 17,
  endLine: 18,
  symbol: 'ConfirmedCall',
  language: 'csharp',
};

describe('Code snippets the answer used (add-codebase-domain)', () => {
  it('shows the answer’s own snippets, highlighted on request, and fetches nothing', async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ results: [], totalMatches: 0, truncated: false, refineHint: null }),
    );
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderWithProviders(
      <CodeSnippetsPanel
        question="how is a tool call made idempotent?"
        active
        used={codeSnippetsOf([codeSource])}
        highlight="src/Maf.Lab.Api/Agent/ToolSource.cs:17"
      />,
    );

    expect(screen.getByTestId('code-provenance')).toHaveTextContent('Used in this answer');
    expect(screen.getByTestId('code-file')).toHaveTextContent(
      'src/Maf.Lab.Api/Agent/ToolSource.cs',
    );
    expect(screen.getByTestId('code-snippet')).toHaveAttribute('data-highlighted', 'true');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('labels fetched code as related, not used by the answer', async () => {
    vi.stubGlobal(
      'fetch',
      agentFetch(
        vi.fn(async () =>
          jsonResponse({ results: [filter], totalMatches: 1, truncated: false, refineHint: null }),
        ),
      ),
    );
    renderWithProviders(<CodeSnippetsPanel question="where is the tenant filter built?" active />);
    expect(await screen.findByTestId('code-provenance')).toHaveTextContent(
      'Related code — not used by this answer',
    );
  });
});

describe('ChatPage with an answer from the codebase', () => {
  it('switches to Code snippets once, counts the snippets, and opens a source’s snippet', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url.startsWith('/api/conversations')) return jsonResponse(emptyHistory);
      if (url === '/api/code/snippets')
        throw new Error('the answer’s own code needs no second search');
      return streamResponse([
        ...run.toolCall('c1', 'search_codebase', 'query=idempotency'),
        run.toolResult('c1', '1 snippet(s)', 'search_codebase', 1),
        run.sources([codeSource]),
        run.delta(
          'Idempotency rides in ConfirmedCall (src/Maf.Lab.Api/Agent/ToolSource.cs:17-27).',
        ),
        run.done('conv-1', 't1'),
      ]);
    });
    vi.stubGlobal('fetch', agentFetch(fetchMock));

    renderWithProviders(<ChatPage />);
    await userEvent.type(
      screen.getByLabelText('Message'),
      'как в кода се прави идемпотентност на тул?',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    const turn = await screen.findByTestId('assistant-turn');
    expect(await within(turn).findByText(/Idempotency rides in ConfirmedCall/)).toBeInTheDocument();
    expect(within(turn).getByTestId('tool-call-card')).toHaveTextContent('Searched the codebase');

    const tabs = screen.getByRole('tablist', { name: 'Right pane' });
    const codeTab = within(tabs).getByRole('tab', { name: /Code snippets/ });
    expect(codeTab).toHaveAttribute('aria-selected', 'true');
    expect(codeTab).toHaveTextContent('1');
    expect(screen.getByTestId('code-provenance')).toHaveTextContent('Used in this answer');
    expect(fetchMock.mock.calls.some(([u]) => u === '/api/code/snippets')).toBe(false);

    // The person's choice stands after the one automatic switch.
    await userEvent.click(within(tabs).getByRole('tab', { name: 'Behind the scenes' }));
    expect(within(tabs).getByRole('tab', { name: 'Behind the scenes' })).toHaveAttribute(
      'aria-selected',
      'true',
    );

    // A code source in the answer opens its snippet.
    await userEvent.click(within(turn).getByRole('button', { name: /ToolSource\.cs:17–18/ }));
    expect(codeTab).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByTestId('code-snippet')).toHaveAttribute('data-highlighted', 'true');
  });
});
