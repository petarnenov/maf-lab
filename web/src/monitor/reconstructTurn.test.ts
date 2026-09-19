import { describe, expect, it } from 'vitest';
import { fixtureTrace } from './fixtures';
import { reconstructTurn } from './reconstructTurn';

const indexOf = (kind: string, nth = 0) =>
  fixtureTrace.map((e, i) => (e.kind === kind ? i : -1)).filter((i) => i >= 0)[nth];

describe('reconstructTurn', () => {
  it('rewinds the answer text to the chunks streamed by the cursor', () => {
    const first = indexOf('answer.delta');
    expect(reconstructTurn(fixtureTrace, first).text).toBe('');
    expect(reconstructTurn(fixtureTrace, first + 1).text).toBe('Assign the missing fee schedule ');
    expect(reconstructTurn(fixtureTrace, fixtureTrace.length).text).toBe(
      'Assign the missing fee schedule and re-run the billing run.',
    );
  });

  it('shows a tool card running between its call and its result', () => {
    const call = indexOf('tool.call');
    const running = reconstructTurn(fixtureTrace, call + 1).toolCalls;
    expect(running).toHaveLength(1);
    expect(running[0]).toMatchObject({ toolName: 'search_documents', status: 'running' });
    const done = reconstructTurn(fixtureTrace, indexOf('tool.result') + 1).toolCalls[0];
    expect(done).toMatchObject({
      status: 'finished',
      resultSummary: '1 snippet(s)',
      sourceCount: 1,
    });
    expect(done.argumentSummary).not.toContain('fee schedule is missing');
  });

  it('shows sources only from the sources step and enriches them from the final turn', () => {
    const at = indexOf('sources');
    expect(reconstructTurn(fixtureTrace, at).sources).toEqual([]);
    const final = {
      text: 'x',
      sources: [
        {
          docId: 'shared/procedures/missing-fee-schedule.txt',
          sectionPath: 'Procedure: Missing fee schedule',
          sourcePath: 'procedures/missing-fee-schedule.txt',
          snippet: 'Assign the schedule…',
        },
      ],
    };
    expect(reconstructTurn(fixtureTrace, at + 1, final).sources).toEqual(final.sources);
  });

  it('marks unknown tools and labels the step', () => {
    const r = reconstructTurn(fixtureTrace, fixtureTrace.length);
    expect(r.toolCalls.find((c) => c.toolName === 'send_email')).toMatchObject({ isError: true });
    expect(r.stepLabel).toBe(`step ${fixtureTrace.length} of ${fixtureTrace.length}`);
    expect(r.textRecorded).toBe(true);
  });

  it('falls back to the final answer for traces without answer text', () => {
    const old = fixtureTrace.filter((e) => e.kind !== 'answer.delta');
    const r = reconstructTurn(old, 2, { text: 'Final answer.', sources: [] });
    expect(r.textRecorded).toBe(false);
    expect(r.text).toBe('Final answer.');
  });
});
