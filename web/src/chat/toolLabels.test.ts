import { describe, expect, it } from 'vitest';
import { toolCallLabel } from './toolLabels';

describe('toolCallLabel', () => {
  it('reads a tool no plugin labels with the generic label, running and then finished', () => {
    expect(
      toolCallLabel({ toolName: 'search_documents', argumentSummary: '', status: 'running' }),
    ).toBe('Calling search_documents…');
    expect(
      toolCallLabel({ toolName: 'search_documents', argumentSummary: '', status: 'finished' }),
    ).toBe('Called search_documents');
  });

  it('lets a plugin in use label its own tool', () => {
    const labels = {
      get_x: ({ running }: { running: boolean }) => (running ? 'Getting x…' : 'Got x'),
    };
    expect(
      toolCallLabel({ toolName: 'get_x', argumentSummary: '', status: 'running' }, labels),
    ).toBe('Getting x…');
  });
});
