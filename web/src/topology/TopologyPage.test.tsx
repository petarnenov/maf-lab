import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it, vi } from 'vitest';
import type { TopologyNode, TopologyReport } from '../api/types';
import { jsonResponse, renderWithProviders } from '../test/render';
import { TopologyPage } from './TopologyPage';

const diagram = readFileSync(resolve(__dirname, '../../../docs/topology.drawio'), 'utf8');

const node = (id: string, overrides: Partial<TopologyNode> = {}): TopologyNode => ({
  id,
  name: id,
  health: 'Healthy',
  instances: [],
  facts: {},
  reason: null,
  ...overrides,
});

const report = (overrides: Partial<TopologyReport> = {}): TopologyReport => ({
  generatedAt: '2026-09-20T10:00:00Z',
  cacheSeconds: 5,
  discoveryAvailable: true,
  reportedBy: 'api-replica-1',
  nodes: [
    node('lb'),
    node('web'),
    node('api', {
      instances: [
        { name: 'api-replica-1', address: '10.0.0.1', health: 'Healthy', reason: null },
        { name: 'api-replica-2', address: '10.0.0.2', health: 'Healthy', reason: null },
      ],
      facts: { replicas: '2/2 healthy' },
    }),
    node('mcp', {
      instances: [{ name: 'mcp-replica-1', address: '10.0.1.1', health: 'Healthy', reason: null }],
      facts: { tools: '3: get_billing_run_status, search_billing_runs, search_documents' },
    }),
    node('qdrant', { facts: { collection: 'maf_chunks', chunks: '3330' } }),
    node('ollama-embeddings'),
    node('chat-provider', {
      health: 'NotProbed',
      reason: 'not probed: a paid remote endpoint is not pinged to refresh a page',
      facts: { chatModel: 'gpt-oss:120b', apiKey: 'set (OLLAMA_API_KEY)' },
    }),
  ],
  edges: [{ from: 'api', to: 'mcp', label: '/mcp via lb' }],
  ...overrides,
});

function stubFetch(state: () => Response) {
  const fetchMock = vi.fn(async (url: string) =>
    url === '/api/topology/diagram' ? new Response(diagram, { status: 200 }) : state(),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('TopologyPage', () => {
  it('draws every node with its live state and replicas', async () => {
    stubFetch(() => jsonResponse(report()));
    renderWithProviders(<TopologyPage />);

    const canvas = await screen.findByRole('group', { name: 'Topology diagram' });
    for (const id of ['lb', 'web', 'api', 'mcp-retrieval', 'qdrant', 'ollama', 'chat provider']) {
      expect(within(canvas).getAllByText(new RegExp(id, 'i')).length).toBeGreaterThan(0);
    }
    expect(within(canvas).getAllByRole('button', { name: /healthy/ }).length).toBeGreaterThan(0);
    expect(within(canvas).getByText('2/2 up')).toBeInTheDocument();
    expect(screen.getByText(/reported by api-replica-1/)).toBeInTheDocument();
  });

  it('marks an unreachable node without relying on colour', async () => {
    stubFetch(() =>
      jsonResponse(
        report({
          nodes: report().nodes.map((n) =>
            n.id === 'qdrant' ? { ...n, health: 'Unreachable', reason: 'no answer within 2s' } : n,
          ),
        }),
      ),
    );
    renderWithProviders(<TopologyPage />);

    const canvas = await screen.findByRole('group', { name: 'Topology diagram' });
    // The state is spelled out in the box and in its accessible name, not only in its stroke colour.
    expect(within(canvas).getByText('✕ unreachable')).toBeInTheDocument();
    expect(
      within(canvas).getByRole('button', { name: /qdrant.*unreachable/i }),
    ).toBeInTheDocument();
  });

  it('shows the full detail of a selected node', async () => {
    stubFetch(() => jsonResponse(report()));
    renderWithProviders(<TopologyPage />);

    const canvas = await screen.findByRole('group', { name: 'Topology diagram' });
    await userEvent.click(within(canvas).getByRole('button', { name: /api.*healthy/i }));

    const detail = screen.getByRole('complementary', { name: 'Node detail' });
    expect(within(detail).getByText('api-replica-1')).toBeInTheDocument();
    expect(within(detail).getByText('api-replica-2')).toBeInTheDocument();
    expect(within(detail).getByText('2/2 healthy')).toBeInTheDocument();
  });

  it('colours a reason by its state: "not probed" is an explanation, not a fault', async () => {
    stubFetch(() =>
      jsonResponse(
        report({
          nodes: report().nodes.map((n) =>
            n.id === 'qdrant' ? { ...n, health: 'Unreachable', reason: 'no answer within 2s' } : n,
          ),
        }),
      ),
    );
    renderWithProviders(<TopologyPage />);

    const canvas = await screen.findByRole('group', { name: 'Topology diagram' });
    const detail = () => screen.getByRole('complementary', { name: 'Node detail' });

    await userEvent.click(within(canvas).getByRole('button', { name: /chat provider/i }));
    const notProbed = within(detail()).getByText(/not probed: a paid remote endpoint/);
    expect(notProbed.className).not.toMatch(/unreachable|degraded/);

    await userEvent.click(within(canvas).getByRole('button', { name: /qdrant/i }));
    expect(within(detail()).getByText('no answer within 2s').className).toMatch(/unreachable/);
  });

  it('keeps the diagram when the live state cannot be loaded', async () => {
    stubFetch(() => jsonResponse({ error: 'nope' }, 500));
    renderWithProviders(<TopologyPage />);

    expect(await screen.findByRole('group', { name: 'Topology diagram' })).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/Could not load the live state/),
    );
    expect(screen.getAllByText('? state unknown').length).toBeGreaterThan(0);
  });

  it('asks for a persona when none is selected', () => {
    stubFetch(() => jsonResponse(report()));
    renderWithProviders(<TopologyPage />, { session: null });

    expect(screen.getByText(/Pick a dev persona/)).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Topology diagram' })).not.toBeInTheDocument();
  });
});
