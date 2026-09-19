import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { fixtureTrace } from './fixtures';
import { MonitorPanel } from './MonitorPanel';

const panel = () => screen.getByRole('region', { name: 'Behind the scenes' });
const step = () => screen.getByTestId('tt-step').textContent;

describe('time travel in the monitor', () => {
  it('opens a stored turn at the end and steps with the keyboard', async () => {
    render(<MonitorPanel events={fixtureTrace} resetKey="t1" />);
    expect(step()).toBe(`step ${fixtureTrace.length} / ${fixtureTrace.length}`);

    panel().focus();
    await userEvent.keyboard('{Home}{ArrowRight}{ArrowRight}{ArrowRight}');
    expect(step()).toBe(`step 3 / ${fixtureTrace.length}`);
    const rows = within(screen.getByRole('list', { name: 'Timeline' })).getAllByRole('listitem');
    expect(rows.filter((r) => r.getAttribute('data-future') === 'false')).toHaveLength(3);
    expect(rows[2]).toHaveAttribute('aria-current', 'step');
    expect(screen.getByRole('group', { name: 'This step' })).toHaveTextContent(
      fixtureTrace[2].title,
    );
    expect(screen.getByRole('slider', { name: 'Trace step' })).toHaveAttribute(
      'aria-valuetext',
      `step 3 of ${fixtureTrace.length}: ${fixtureTrace[2].kind} — ${fixtureTrace[2].title}`,
    );
    await userEvent.keyboard('{End}');
    expect(step()).toBe(`step ${fixtureTrace.length} / ${fixtureTrace.length}`);
  });

  it('buttons and clicking a timeline row move the cursor', async () => {
    render(<MonitorPanel events={fixtureTrace} resetKey="t1" />);
    await userEvent.click(screen.getByRole('button', { name: 'Jump to start' }));
    expect(step()).toBe(`step 0 / ${fixtureTrace.length}`);
    await userEvent.click(screen.getByRole('button', { name: 'Step forward' }));
    expect(step()).toBe(`step 1 / ${fixtureTrace.length}`);
    const rows = within(screen.getByRole('list', { name: 'Timeline' })).getAllByRole('listitem');
    await userEvent.click(within(rows[5]).getByText(fixtureTrace[5].title));
    expect(step()).toBe(`step 6 / ${fixtureTrace.length}`);
  });

  it('retrieval appears only once its step is reached', async () => {
    render(<MonitorPanel events={fixtureTrace} resetKey="t1" />);
    const retrievalIndex = fixtureTrace.findIndex((e) => e.kind === 'retrieval');
    await userEvent.click(screen.getByRole('tab', { name: 'Retrieval' }));
    await userEvent.click(screen.getByRole('button', { name: 'Jump to start' }));
    for (let i = 0; i < retrievalIndex; i++)
      await userEvent.click(screen.getByRole('button', { name: 'Step forward' }));
    expect(screen.getByText('No retrieval in this turn.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Step forward' }));
    const search = screen.getByRole('region', { name: 'Search' });
    for (const list of ['Dense', 'Sparse (BM25)', 'Fused']) {
      expect(within(search).getByRole('table', { name: `${list} candidates` })).toBeInTheDocument();
    }
  });

  it('a model request without its response shows it is waiting', async () => {
    const requestIndex = fixtureTrace.findIndex((e) => e.kind === 'model.request');
    render(<MonitorPanel events={fixtureTrace.slice(0, requestIndex + 1)} resetKey="t1" />);
    await userEvent.click(screen.getByRole('tab', { name: 'Model' }));
    expect(screen.getByRole('region', { name: 'Model call 1' })).toHaveTextContent(
      'waiting for response…',
    );
  });

  it('a live turn offers Back to live after scrubbing back and follows new events again', async () => {
    const { rerender } = render(
      <MonitorPanel events={fixtureTrace.slice(0, 5)} live resetKey="t1" />,
    );
    expect(screen.queryByRole('button', { name: 'Back to live' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Step back' }));
    rerender(<MonitorPanel events={fixtureTrace.slice(0, 8)} live resetKey="t1" />);
    expect(step()).toBe('step 4 / 8');
    await userEvent.click(screen.getByRole('button', { name: 'Back to live' }));
    expect(step()).toBe('step 8 / 8');
    rerender(<MonitorPanel events={fixtureTrace.slice(0, 10)} live resetKey="t1" />);
    expect(step()).toBe('step 10 / 10');
  });

  it('play and pause toggle and announce politely', async () => {
    render(<MonitorPanel events={fixtureTrace} resetKey="t1" />);
    await userEvent.click(screen.getByRole('button', { name: 'Play' }));
    expect(screen.getByRole('button', { name: 'Pause' })).toBeInTheDocument();
    expect(screen.getByText('Playing')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Pause' }));
    expect(screen.getByText('Paused')).toBeInTheDocument();
  });
});
