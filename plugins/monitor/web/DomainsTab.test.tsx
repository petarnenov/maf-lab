import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { TraceEvent } from './types';
import { DomainsTab } from './DomainsTab';
import { domainPath } from './domainData';
import { MonitorPanel } from './MonitorPanel';

let seq = 0;
const ev = (kind: string, title: string, data: unknown, atMs = seq * 10): TraceEvent =>
  ({ seq: ++seq, atMs, kind, title, durationMs: null, data, truncated: false }) as TraceEvent;

/** A turn that starts in billing and crosses into the portfolio domain, as the api traces it. */
function crossingTurn(): TraceEvent[] {
  seq = 0;
  return [
    ev('turn.start', 'Turn started', { question: 'Why did the fee on A-1042 go up?' }),
    ev('intent', 'Intent Procedural', {
      intent: 'Procedural',
      domains: { billing: 0.91, portfolio: 0.84 },
    }),
    ev('domain', 'Domains: billing 0.91 · portfolio 0.84 → crosses billing ↔ portfolio', {
      probabilities: { billing: 0.91, portfolio: 0.84 },
      scopeFloor: 0.5,
      inScope: ['billing', 'portfolio'],
      primary: 'billing',
      crossing: true,
      forcedSearches: ['search_documents', 'search_portfolio_documents'],
      offered: ['billing', 'portfolio'],
      unavailable: [],
    }),
    ev('tool.call', 'Calling search_documents over MCP (billing)', {
      callId: 'c1',
      tool: 'search_documents',
      domain: 'billing',
      server: 'maf-lab-retrieval',
    }),
    ev('tool.result', 'search_documents returned', {
      callId: 'c1',
      tool: 'search_documents',
      domain: 'billing',
      server: 'maf-lab-retrieval',
      isError: false,
      latencyMs: 120,
      mcpInstance: 'mcp-1',
    }),
    ev('boundary', 'Crossed billing → portfolio with get_aum_history', {
      from: 'billing',
      to: 'portfolio',
      tool: 'get_aum_history',
      callId: 'c2',
      server: 'maf-lab-portfolio',
      hop: 1,
    }),
    ev('tool.call', 'Calling get_aum_history over MCP (portfolio)', {
      callId: 'c2',
      tool: 'get_aum_history',
      domain: 'portfolio',
      server: 'maf-lab-portfolio',
    }),
    ev('tool.result', 'get_aum_history returned', {
      callId: 'c2',
      tool: 'get_aum_history',
      domain: 'portfolio',
      server: 'maf-lab-portfolio',
      isError: false,
      latencyMs: 15,
      mcpInstance: 'pf-2',
    }),
    ev('turn.end', 'Turn finished across billing → portfolio', {
      durationMs: 900,
      domainPath: ['billing', 'portfolio'],
      domainsTouched: ['billing', 'portfolio'],
      domainsPredicted: ['billing', 'portfolio'],
      crossings: 1,
    }),
  ];
}

describe('DomainsTab', () => {
  it("shows Jev's probability per domain against the scope floor and says the question crosses", () => {
    render(<DomainsTab events={crossingTurn()} />);
    const verdict = screen.getByRole('region', { name: "Jev's domain verdict" });
    expect(within(verdict).getByRole('meter', { name: 'billing probability' })).toHaveAttribute(
      'aria-valuenow',
      '0.91',
    );
    expect(within(verdict).getByRole('meter', { name: 'portfolio probability' })).toHaveAttribute(
      'aria-valuenow',
      '0.84',
    );
    expect(
      within(verdict).getByText('Crosses the boundary: billing ↔ portfolio'),
    ).toBeInTheDocument();
    expect(
      within(verdict).getByText(/forced: search_documents \+ search_portfolio_documents/),
    ).toBeInTheDocument();
  });

  it('lists the calls in order with their server and replica, and the crossing between them', () => {
    render(<DomainsTab events={crossingTurn()} />);
    const path = screen.getByRole('region', { name: 'Path across servers' });
    expect(within(path).getByText('billing → portfolio')).toBeInTheDocument();
    const hops = within(path).getAllByRole('listitem');
    expect(hops).toHaveLength(2);
    expect(within(hops[0]).getByText('search_documents')).toBeInTheDocument();
    expect(within(hops[0]).getByText('replica mcp-1')).toBeInTheDocument();
    expect(within(hops[0]).queryByRole('note')).toBeNull();
    expect(within(hops[1]).getByRole('note', { name: 'Boundary crossing' })).toHaveTextContent(
      'crossed billing → portfolio',
    );
    expect(within(hops[1]).getByText('maf-lab-portfolio')).toBeInTheDocument();
  });

  it('says when the calls did what Jev predicted, and when they did not', () => {
    render(<DomainsTab events={crossingTurn()} />);
    expect(screen.getByText('They agree.')).toBeInTheDocument();

    const events = crossingTurn();
    const end = events[events.length - 1];
    end.data = {
      ...(end.data as object),
      domainsTouched: ['billing'],
      domainPath: ['billing'],
    } as never;
    render(
      <DomainsTab
        events={events.filter(
          (e) => !(e.kind === 'tool.call' && (e.data as { domain: string }).domain === 'portfolio'),
        )}
      />,
    );
    expect(screen.getByText(/Predicted but never called: portfolio/)).toBeInTheDocument();
  });

  it('has something to say for a turn without a verdict or calls', () => {
    seq = 0;
    render(<DomainsTab events={[ev('turn.start', 'Turn started', {})]} />);
    expect(screen.getByText(/No domain verdict and no tool calls/)).toBeInTheDocument();
  });
});

describe('domain path', () => {
  it('collapses consecutive calls in one domain', () => {
    seq = 0;
    const call = (domain: string) => ev('tool.call', 'call', { domain });
    expect(
      domainPath([call('billing'), call('billing'), call('portfolio'), call('billing')]),
    ).toEqual(['billing', 'portfolio', 'billing']);
  });

  it("puts a crossing turn's path in the monitor header", () => {
    render(<MonitorPanel events={crossingTurn()} />);
    expect(screen.getByTestId('domain-path')).toHaveTextContent('domains: billing → portfolio');
    expect(screen.getByRole('tab', { name: 'Domains' })).toBeInTheDocument();
  });
});
