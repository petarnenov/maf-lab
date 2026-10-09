import { act, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { jsonResponse, makeSession, renderWithProviders } from '@maf/testing';
import { TestAgentSection } from './TestAgentSection';
import { reachableAgent, unconfiguredAgent, unreachableAgent } from './testAgentFixtures';

const admin = { session: makeSession('TENANT_ADMIN') };

describe('TestAgentSection', () => {
  it('shows a reachable agent: its card, its defaults, its runs by state and the recent ones', async () => {
    const fetch = vi.fn<(url: string) => Promise<Response>>(async () =>
      jsonResponse(reachableAgent),
    );
    vi.stubGlobal('fetch', fetch);
    renderWithProviders(<TestAgentSection />, admin);

    expect(await screen.findByTestId('test-agent-status')).toHaveTextContent(/Reachable.*12 ms/);
    // Everything comes through the api; the browser never asks the agent.
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch.mock.calls[0][0]).toBe('/api/admin/coverage/test-agent');

    const card = screen.getByTestId('test-agent-card');
    expect(card).toHaveTextContent('maf-lab test agent');
    expect(card).toHaveTextContent('1.0.0');
    expect(card).toHaveTextContent('generate-tests');
    expect(card).toHaveTextContent('http://test-agent:8080/a2a');
    expect(card).toHaveTextContent('a2a.testgen.run');
    expect(card).toHaveTextContent('maf-lab-assistant');

    const defaults = screen.getByTestId('test-agent-defaults');
    expect(defaults).toHaveTextContent('GLM 5.3');
    expect(within(defaults).getByText('Attempts').closest('tr')).toHaveTextContent('10 (1–10)');
    expect(within(defaults).getByText('Tool rounds per attempt').closest('tr')).toHaveTextContent(
      '40 (1–40)',
    );
    expect(within(defaults).getByText('Test runs per attempt').closest('tr')).toHaveTextContent(
      '2 (0–2)',
    );
    expect(within(defaults).getByText('Suspected bugs').closest('tr')).toHaveTextContent('3 (0–3)');
    expect(within(defaults).getByText('Deadline').closest('tr')).toHaveTextContent(
      '2 h (10 min–2 h)',
    );
    expect(defaults).toHaveTextContent(/Budget\s*None/);

    const counts = screen.getByTestId('test-agent-counts');
    expect(within(counts).getByText('Running now').parentElement).toHaveTextContent('1');
    expect(within(counts).getByText('Accepted').parentElement).toHaveTextContent('2');
    expect(screen.getByRole('link', { name: /Open Coverage/ })).toHaveAttribute(
      'href',
      '/coverage',
    );

    const rows = within(screen.getByTestId('test-agent-runs')).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent('Working');
    expect(rows[0]).toHaveTextContent('3/10');
    expect(rows[0]).toHaveTextContent('72.4% → 85%');
    expect(rows[1]).toHaveTextContent('Failed');
    expect(rows[1]).toHaveTextContent('deadline');
    expect(rows[2]).toHaveTextContent('all attempts used');
    expect(within(rows[0]).getByRole('link')).toHaveAttribute(
      'href',
      '/coverage?file=src%2FMaf.Lab.Api%2FCoverage%2FCoverageTree.cs',
    );
  });

  it('shows how long each recent run took, counting on while one is running', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      vi.stubGlobal(
        'fetch',
        vi.fn(async () => jsonResponse(reachableAgent)),
      );
      renderWithProviders(<TestAgentSection />, admin);

      const table = await screen.findByTestId('test-agent-runs');
      expect(
        within(table)
          .getAllByRole('columnheader')
          .map((h) => h.textContent),
      ).toEqual([
        'File',
        'State',
        'Attempt',
        'Coverage',
        'Reason',
        'Model',
        'Duration',
        'Cost',
        'When',
      ]);
      const cells = () => screen.getAllByTestId('test-agent-run-duration');
      expect(cells()[0]).toHaveTextContent(/^42s so far$/);
      expect(cells()[1]).toHaveTextContent(/^3m 05s$/);
      expect(cells()[2]).toHaveTextContent(/^—$/);

      await act(async () => {
        vi.advanceTimersByTime(1000);
      });
      expect(cells()[0]).toHaveTextContent(/^43s so far$/);
      expect(cells()[1]).toHaveTextContent(/^3m 05s$/);
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows what each recent run cost, so far while running, and a dash for a run without one', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse(reachableAgent)),
    );
    renderWithProviders(<TestAgentSection />, admin);

    await screen.findByTestId('test-agent-runs');
    const cells = screen.getAllByTestId('test-agent-run-cost');
    expect(cells[0]).toHaveTextContent(/^≈\$0\.042 so far$/);
    expect(cells[0]).toHaveAttribute(
      'title',
      '300,000 tokens · estimated price · so far, as of the last refresh',
    );
    expect(cells[1]).toHaveTextContent(/^≈\$0\.291$/);
    expect(cells[1]).toHaveAttribute(
      'title',
      '2,760,003 tokens · estimated price · of $0.50 budget',
    );
    // A run listed by an api from before costs were shown.
    expect(cells[2]).toHaveTextContent(/^—$/);
    expect(cells[2]).not.toHaveAttribute('title');
  });

  it('shows a list-priced and a free run without the estimate mark', async () => {
    const [first, second] = reachableAgent.recent;
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        jsonResponse({
          ...reachableAgent,
          recent: [
            { ...second, costUsd: 1.2745, costIsEstimate: false, budget: null },
            { ...first, state: 'failed', finishedAt: first.updatedAt, costUsd: 0, tokens: 0 },
          ],
        }),
      ),
    );
    renderWithProviders(<TestAgentSection />, admin);

    await screen.findByTestId('test-agent-runs');
    const cells = screen.getAllByTestId('test-agent-run-cost');
    expect(cells[0]).toHaveTextContent(/^\$1\.27$/);
    expect(cells[0]).toHaveAttribute('title', '2,760,003 tokens · list price');
    expect(cells[1]).toHaveTextContent(/^\$0\.00$/);
  });

  it('says an unreachable agent is unreachable, and still shows its defaults and runs', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse(unreachableAgent)),
    );
    renderWithProviders(<TestAgentSection />, admin);

    expect(await screen.findByTestId('test-agent-status')).toHaveTextContent(
      /Unreachable.*No answer within 2 s\./,
    );
    expect(screen.getByTestId('test-agent-card')).toHaveTextContent('The card could not be read.');
    expect(screen.getByTestId('test-agent-defaults')).toHaveTextContent('GLM 5.3');
    expect(screen.getByTestId('test-agent-runs')).toBeInTheDocument();
  });

  it('says when no agent is configured and nothing has run', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse(unconfiguredAgent)),
    );
    renderWithProviders(<TestAgentSection />, admin);

    expect(await screen.findByTestId('test-agent-status')).toHaveTextContent(/Not configured/);
    expect(screen.getByTestId('test-agent-no-runs')).toBeInTheDocument();
    expect(screen.queryByText('The api signs in as')).not.toBeInTheDocument();
  });

  it('shows themed progress while the api has not answered', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {})),
    );
    renderWithProviders(<TestAgentSection />, admin);

    expect(
      await screen.findByRole('progressbar', { name: 'Checking the test agent…' }),
    ).toBeInTheDocument();
  });

  it('says so when the overview cannot be loaded', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => jsonResponse({ title: 'boom' }, 500)),
    );
    renderWithProviders(<TestAgentSection />, admin);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The test agent overview could not be loaded.',
    );
  });
});
