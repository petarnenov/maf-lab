import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fixtureTrace } from './fixtures';
import { MonitorPanel } from './MonitorPanel';

const tab = (name: string) => screen.getByRole('tab', { name });

describe('MonitorPanel', () => {
  it('shows an empty state without events', () => {
    render(<MonitorPanel events={[]} />);
    expect(
      screen.getByText(/Ask a question to see what happens behind the scenes/),
    ).toBeInTheDocument();
  });

  it('summarises the turn in the header', () => {
    render(<MonitorPanel events={fixtureTrace} live />);
    const stats = screen.getByTestId('monitor-stats');
    expect(stats).toHaveTextContent(`${fixtureTrace.length} events`);
    expect(stats).toHaveTextContent('total 945 ms');
    expect(stats).toHaveTextContent('1 model calls');
    expect(stats).toHaveTextContent('api: api-replica-1');
    expect(stats).toHaveTextContent('mcp: mcp-replica-2');
    expect(screen.getByText('● live')).toBeInTheDocument();
  });

  it('timeline lists every event and expands raw data on click', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    const rows = within(screen.getByRole('list', { name: 'Timeline' })).getAllByRole('listitem');
    expect(rows).toHaveLength(fixtureTrace.length);
    expect(rows[0]).toHaveAttribute('data-kind', 'turn.start');

    const timeline = screen.getByRole('list', { name: 'Timeline' });
    await userEvent.click(within(timeline).getByText('Intent: Procedural (forced retrieval)'));
    expect(within(timeline).getByText('"search_documents"')).toBeInTheDocument();
  });

  it('model tab shows forced call, request messages, response and tokens', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    await userEvent.click(tab('Model'));
    expect(screen.getByRole('region', { name: 'Forced tool call' })).toHaveTextContent(
      'search_documents',
    );
    const call = screen.getByRole('region', { name: 'Model call 1' });
    expect(call).toHaveTextContent('gpt-oss:120b');
    expect(call).toHaveTextContent('tokens 812 in / 64 out');
    expect(call).toHaveTextContent('Request messages (4)');
    expect(call).toHaveTextContent('Assign the missing fee schedule and re-run the billing run.');
    expect(within(call).getByText('function result (forced_1)')).toBeInTheDocument();
  });

  it('model tab shows n/a when usage is not reported', async () => {
    const events = fixtureTrace.map((e) =>
      e.kind === 'model.response' ? { ...e, data: { ...(e.data as object), usage: null } } : e,
    );
    render(<MonitorPanel events={events} />);
    await userEvent.click(tab('Model'));
    expect(screen.getByRole('region', { name: 'Model call 1' })).toHaveTextContent('tokens n/a');
  });

  it('retrieval tab shows scope, terms and the three ranked lists', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    await userEvent.click(tab('Retrieval'));
    const search = screen.getByRole('region', { name: 'Search' });
    expect(search).toHaveTextContent('scope: firm-a + shared');
    expect(search).toHaveTextContent('fusion: rrf');
    expect(within(search).getByRole('table', { name: 'Query terms' })).toHaveTextContent('missing');
    for (const list of ['Dense', 'Sparse (BM25)', 'Fused']) {
      const table = within(search).getByRole('table', { name: `${list} candidates` });
      // Short, readable label in the cell; the full chunk id stays available as the tooltip.
      expect(table).toHaveTextContent('missing-fee-schedule.txt');
      expect(
        table.querySelector(
          '[title^="shared/procedures/missing-fee-schedule.txt#procedure-missing-fee-schedule"]',
        ),
      ).not.toBeNull();
    }
    expect(search).toHaveTextContent('qdrant 12 ms');
  });

  it('retrieval tab shows what was searched when the query was translated', async () => {
    const events = fixtureTrace.map((e) =>
      e.kind === 'retrieval'
        ? {
            ...e,
            data: {
              ...(e.data as object),
              query: {
                ...((e.data as { query: object }).query as object),
                original: 'каква е процедурата когато липсва фий схема',
                translated: true,
                translationMs: 812,
              },
            },
          }
        : e,
    );
    render(<MonitorPanel events={events} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    // Both texts: what the user asked, and the English text the BM25 terms below actually come from.
    expect(search).toHaveTextContent('каква е процедурата когато липсва фий схема');
    expect(search).toHaveTextContent('translated in 812 ms');
    expect(search).toHaveTextContent('What is the procedure when a fee schedule is missing?');
  });

  it('retrieval tab shows the query alone when nothing was translated', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    expect(search).toHaveTextContent('What is the procedure when a fee schedule is missing?');
    expect(search).not.toHaveTextContent('translated in');
  });

  it('mcp tab shows arguments, raw result, replicas and the unknown tool', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    await userEvent.click(tab('MCP'));
    const tool = screen.getByRole('region', { name: 'Tool search_documents' });
    expect(tool).toHaveTextContent('api: api-replica-1');
    expect(tool).toHaveTextContent('mcp: mcp-replica-2');
    expect(tool).toHaveTextContent('114 ms');
    expect(tool).toHaveTextContent('ok');
    expect(within(tool).getByText('Envelope sent to the model')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Unknown tool' })).toHaveTextContent('send_email');
  });

  it('prompt tab shows the system prompt, tools and history window', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    await userEvent.click(tab('Prompt & memory'));
    expect(screen.getByRole('region', { name: 'System prompt' })).toHaveTextContent('system.v1');
    expect(screen.getByText('get_billing_run_status')).toBeInTheDocument();
    const history = screen.getByRole('region', { name: 'History window' });
    expect(history).toHaveTextContent('42 / 3000 tokens');
    expect(history).toHaveTextContent('2 older messages excluded');
    expect(screen.getByRole('region', { name: 'Memory' })).toHaveTextContent(
      'assistant: 14 tokens',
    );
  });

  it('shows an error instead of the views', () => {
    render(<MonitorPanel events={[]} error="Could not load the trace." />);
    expect(screen.getByRole('alert')).toHaveTextContent('Could not load the trace.');
  });
});
