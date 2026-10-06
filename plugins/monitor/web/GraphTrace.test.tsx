import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { TraceEvent } from './types';
import { fixtureGraphTrace, fixtureTrace } from './fixtures';
import { kindColor } from './kindColors';
import { MonitorPanel } from './MonitorPanel';
import { RetrievalTab } from './MonitorTabs';
import { graphReadCount, mcpInstances } from './traceData';

// The monitor's view of the `graph` event (add-graph-trace-event): a timeline tag of its own, a card with a neo4j
// timing chip beside the searches, and a header count — all as of the time-travel cursor.

const graphIndex = fixtureGraphTrace.findIndex((e) => e.kind === 'graph');

describe('graph trace event', () => {
  it('has a colour no other kind uses', () => {
    expect(kindColor('graph')).toBe('var(--kind-graph)');
    const others = [
      'turn.start',
      'model.request',
      'tool.call',
      'tool.unknown',
      'retrieval',
      'relevance',
      'answer.check',
      'domain',
      'intent',
      'answer.delta',
    ];
    for (const kind of others) expect(kindColor(kind)).not.toBe('var(--kind-graph)');
  });

  it('shows a graph row in the timeline with its Neo4j title and duration', () => {
    render(<MonitorPanel events={fixtureGraphTrace} />);
    const rows = within(screen.getByRole('list', { name: 'Timeline' })).getAllByRole('listitem');
    const row = rows.find((r) => r.getAttribute('data-kind') === 'graph')!;
    expect(row).toHaveTextContent('Neo4j billing_neighbourhood_2 + firm_runs · 9 rows · 12 ms');
    expect(within(row).getByTitle('graph')).toHaveStyle({ background: 'var(--kind-graph)' });
    // It follows its call's tool.result.
    expect(rows[rows.indexOf(row) - 1]).toHaveAttribute('data-kind', 'tool.result');
  });

  it('shows each call as a graph card with its reads and a neo4j timing chip', () => {
    render(<RetrievalTab events={fixtureGraphTrace} />);
    const [billing, code] = screen.getAllByRole('region', { name: 'Graph reads' });
    expect(within(billing).getByText('trace_billing_relationships')).toBeInTheDocument();
    expect(within(billing).getByText('mcp: mcp-retrieval-2')).toBeInTheDocument();
    expect(within(billing).getByText('scope: firm-a + shared')).toBeInTheDocument();
    expect(within(billing).getByText('9 rows')).toBeInTheDocument();
    expect(within(billing).getByText('neo4j 12 ms')).toBeInTheDocument();
    const reads = within(within(billing).getByRole('table', { name: 'Neo4j reads' })).getAllByRole(
      'row',
    );
    expect(reads[1]).toHaveTextContent('billing_neighbourhood_2');
    expect(reads[1]).toHaveTextContent('7 / 50');
    expect(reads[2]).toHaveTextContent('firm_runs');
    expect(reads[2]).toHaveTextContent('2 / 5');
    // Arguments stay in the MCP view: the card shows structure only.
    expect(billing).not.toHaveTextContent('A-1042');

    expect(within(code).getByRole('status')).toHaveTextContent('graph store unavailable');
    expect(
      within(code).getByText(/unavailable \(ServiceUnavailableException\)/),
    ).toBeInTheDocument();
  });

  it('renders an event missing its fields as not available', () => {
    const bare: TraceEvent = {
      seq: 1,
      atMs: 0,
      kind: 'graph',
      title: 'Neo4j',
      durationMs: null,
      data: { reads: [{}] },
      truncated: false,
    };
    render(<RetrievalTab events={[bare, { ...bare, seq: 2, data: null }]} />);
    const cards = screen.getAllByRole('region', { name: 'Graph reads' });
    expect(cards).toHaveLength(2);
    expect(within(cards[0]).getByText('neo4j n/a')).toBeInTheDocument();
    expect(within(cards[0]).getByText('rows n/a')).toBeInTheDocument();
  });

  it('counts graph reads and their instances', () => {
    expect(graphReadCount(fixtureGraphTrace)).toBe(3);
    expect(graphReadCount(fixtureTrace)).toBe(0);
    expect(mcpInstances(fixtureGraphTrace)).toEqual(
      expect.arrayContaining(['mcp-retrieval-2', 'mcp-code-1']),
    );
  });

  it('shows the card and the header chip only once the cursor reaches the graph step', async () => {
    render(<MonitorPanel events={fixtureGraphTrace} />);
    await userEvent.click(screen.getByRole('tab', { name: 'Retrieval' }));
    await userEvent.click(screen.getByRole('button', { name: 'Jump to start' }));
    for (let i = 0; i < graphIndex; i++)
      await userEvent.click(screen.getByRole('button', { name: 'Step forward' }));

    expect(screen.queryByTestId('graph-reads')).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Graph reads' })).not.toBeInTheDocument();
    expect(screen.getByText('No retrieval in this turn.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Step forward' }));
    expect(screen.getByTestId('graph-reads')).toHaveTextContent('2 graph reads');
    expect(screen.getAllByRole('region', { name: 'Graph reads' })).toHaveLength(1);
  });

  it('leaves a trace without graph events as it was', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    expect(screen.queryByTestId('graph-reads')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Retrieval' }));
    expect(screen.queryByRole('region', { name: 'Graph reads' })).not.toBeInTheDocument();
  });
});
