import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  fixtureFrames,
  fixtureTrace,
  fixtureTraceAllDropped,
  fixtureTraceAllOutOfVocabulary,
  fixtureTraceNothingFound,
  fixtureTraceOutOfVocabulary,
} from './fixtures';
import { MonitorPanel } from './MonitorPanel';

const FAILURE_DETAIL = 'connection to qdrant-host:6333 refused';

/**
 * Lets a test make one view throw during render, standing in for the next defect in a tab. Null for every other
 * test in this file, so they render the real views.
 */
const { failing } = vi.hoisted(() => ({ failing: { view: null as 'retrieval' | null } }));

vi.mock('./MonitorTabs', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./MonitorTabs')>();
  return {
    ...actual,
    RetrievalTab: (props: Parameters<typeof actual.RetrievalTab>[0]) => {
      if (failing.view === 'retrieval') throw new Error(FAILURE_DETAIL);
      return <actual.RetrievalTab {...props} />;
    },
  };
});

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

  it('retrieval tab shows a term the corpus has never seen instead of a weight', async () => {
    render(<MonitorPanel events={fixtureTraceOutOfVocabulary} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    const terms = within(search).getByRole('table', { name: 'Query terms' });
    const rows = within(terms).getAllByRole('row').slice(1); // drop the header row
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('what');
    expect(rows[0]).toHaveTextContent('0.300');
    expect(rows[1]).toHaveTextContent('jwe');
    expect(rows[1]).toHaveTextContent('not in index');
    // No weight was reported for it, and none is invented — 0.000 would read as a term in every document.
    expect(rows[1]).not.toHaveTextContent('0.000');
    expect(search).toHaveTextContent('it cannot match on BM25');

    // The rest of the search still renders.
    expect(within(search).getByRole('table', { name: 'Fused candidates' })).toHaveTextContent(
      'billing-overview.txt',
    );
    expect(search).toHaveTextContent('qdrant 12 ms');
  });

  it('retrieval tab renders a query whose every term is out of vocabulary', async () => {
    render(<MonitorPanel events={fixtureTraceAllOutOfVocabulary} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    const terms = within(search).getByRole('table', { name: 'Query terms' });
    const rows = within(terms).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    for (const row of rows) {
      expect(row).toHaveTextContent('not in index');
    }
    expect(search).toHaveTextContent('it cannot match on BM25');
    expect(search).toHaveTextContent('embed 29 ms');
  });

  it('retrieval tab says nothing about the vocabulary when every term has a weight', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    expect(search).not.toHaveTextContent('not in index');
    expect(search).not.toHaveTextContent('cannot match on BM25');
  });

  it('retrieval tab states the floor applied to each branch', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    expect(search).toHaveTextContent('dense floor 0.65');
    // Null is a real setting, not a missing one: that branch keeps every candidate.
    expect(search).toHaveTextContent('bm25 floor off');
  });

  it('retrieval tab shows the near misses a floor kept out of the answer', async () => {
    render(<MonitorPanel events={fixtureTraceAllDropped} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    const dense = within(search).getByRole('table', { name: 'Dense candidates' });
    // Shown, not hidden — and marked, so they cannot be mistaken for results.
    expect(dense).toHaveTextContent('billing-overview.txt');
    expect(dense).toHaveTextContent('0.471');
    expect(within(dense).getAllByText('below floor')).toHaveLength(2);
    expect(search).toHaveTextContent(
      'returned nothing: 2 of 2 candidate(s) fell below their branch floor',
    );
  });

  it('retrieval tab tells a search that found nothing from one whose candidates were dropped', async () => {
    render(<MonitorPanel events={fixtureTraceNothingFound} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    expect(search).toHaveTextContent('found no candidates at all');
    expect(search).not.toHaveTextContent('fell below their branch floor');
  });

  it('retrieval tab says nothing about dropping when a search returned results', async () => {
    render(<MonitorPanel events={fixtureTrace} />);
    await userEvent.click(tab('Retrieval'));

    const search = screen.getByRole('region', { name: 'Search' });
    expect(search).not.toHaveTextContent('below floor');
    expect(search).not.toHaveTextContent('returned nothing');
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
  it('agui tab lists every frame of the run, one row each, with no coalescing', async () => {
    render(<MonitorPanel events={fixtureTrace} frames={fixtureFrames} />);
    await userEvent.click(tab('AG-UI'));
    const rows = within(screen.getByRole('list', { name: 'AG-UI frames' })).getAllByRole(
      'listitem',
    );

    expect(rows).toHaveLength(fixtureFrames.length);
    expect(rows[0]).toHaveAttribute('data-type', 'RUN_STARTED');
    expect(rows[rows.length - 1]).toHaveAttribute('data-type', 'RUN_FINISHED');
    // The two text deltas stay two rows; nothing about the wire is summarised away.
    expect(rows.filter((r) => r.getAttribute('data-type') === 'TEXT_MESSAGE_CONTENT')).toHaveLength(
      2,
    );
    expect(rows[1]).toHaveTextContent('maf-lab/trace');
  });

  it('agui tab expands a frame and points a trace frame at the other views', async () => {
    render(<MonitorPanel events={fixtureTrace} frames={fixtureFrames} />);
    await userEvent.click(tab('AG-UI'));
    const list = screen.getByRole('list', { name: 'AG-UI frames' });
    const rows = within(list).getAllByRole('listitem');

    await userEvent.click(rows[0]);
    expect(within(rows[0]).getByTestId('json-view')).toHaveTextContent('threadId');

    await userEvent.click(rows[1]);
    expect(rows[1]).toHaveTextContent(`Carried trace event #${fixtureTrace[0].seq}`);
  });

  it('agui tab says when a turn has no frames recorded', async () => {
    render(<MonitorPanel events={fixtureTrace} frames={[]} framesRecorded={false} />);
    await userEvent.click(tab('AG-UI'));
    expect(screen.getByText(/were not recorded/)).toBeInTheDocument();
  });
  it("offers the turn's trace where the spans are kept", async () => {
    const withTrace = fixtureTrace.map((e) =>
      e.kind === 'turn.start'
        ? {
            ...e,
            data: {
              ...(e.data as object),
              traceId: 'abc123',
              traceUrl: 'http://localhost:7171/jaeger/trace/abc123',
            },
          }
        : e,
    );
    render(<MonitorPanel events={withTrace} />);

    expect(screen.getByRole('link', { name: 'open trace' })).toHaveAttribute(
      'href',
      'http://localhost:7171/jaeger/trace/abc123',
    );
  });

  it('offers no trace link for a turn recorded without one', () => {
    render(<MonitorPanel events={fixtureTrace} />);
    expect(screen.queryByRole('link', { name: 'open trace' })).not.toBeInTheDocument();
  });

  describe('when one view fails to render', () => {
    beforeEach(() => {
      failing.view = 'retrieval';
      // React logs the caught error, and so does the boundary. Neither belongs in test output.
      vi.spyOn(console, 'error').mockImplementation(() => {});
    });

    afterEach(() => {
      failing.view = null;
      vi.restoreAllMocks();
    });

    it('keeps the failure inside that view', async () => {
      render(<MonitorPanel events={fixtureTrace} />);
      await userEvent.click(tab('Retrieval'));

      expect(screen.getByRole('alert')).toHaveTextContent('The Retrieval view could not be shown');
      // Everything outside the view survives: the user can still read the turn and navigate away.
      expect(screen.getByTestId('monitor-stats')).toHaveTextContent('1 model calls');
      expect(screen.getByRole('heading', { name: /Behind the scenes/ })).toBeInTheDocument();
      for (const name of ['Timeline', 'Model', 'Retrieval', 'MCP', 'Prompt & memory', 'AG-UI']) {
        expect(tab(name)).toBeInTheDocument();
      }
    });

    it('renders nothing internal from the error', async () => {
      const { container } = render(<MonitorPanel events={fixtureTrace} />);
      await userEvent.click(tab('Retrieval'));

      expect(container.textContent).not.toContain(FAILURE_DETAIL);
      expect(container.textContent).not.toContain('qdrant-host');
      expect(container.textContent).not.toContain('Error');
    });

    it('leaves the other views working', async () => {
      render(<MonitorPanel events={fixtureTrace} />);
      await userEvent.click(tab('Retrieval'));
      expect(screen.getByRole('alert')).toBeInTheDocument();

      await userEvent.click(tab('Model'));
      expect(screen.getByRole('region', { name: 'Model call 1' })).toHaveTextContent(
        'gpt-oss:120b',
      );
      expect(screen.queryByRole('alert')).toBeNull();
    });

    it('attempts the view again on the way back, without a reload', async () => {
      render(<MonitorPanel events={fixtureTrace} />);
      await userEvent.click(tab('Retrieval'));
      expect(screen.getByRole('alert')).toBeInTheDocument();

      await userEvent.click(tab('Model'));
      // Whatever made it fail is gone; coming back must render it rather than stay failed.
      failing.view = null;
      await userEvent.click(tab('Retrieval'));

      expect(screen.queryByRole('alert')).toBeNull();
      expect(screen.getByRole('region', { name: 'Search' })).toHaveTextContent('fusion: rrf');
    });
  });
});
