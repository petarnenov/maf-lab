import { describe, expect, it } from 'vitest';
import { stepLabel } from './runStep';

describe('stepLabel', () => {
  it('says what the run is doing from its open step, and thinks when it has none', () => {
    expect(stepLabel(undefined)).toBe('Thinking…');
    expect(stepLabel('screening the question')).toBe('Screening the question…');
    expect(stepLabel('tool: search_documents')).toBe('Calling search_documents…');
    // Another agent's step reads as it named it.
    expect(stepLabel('attempt 2: building')).toBe('Attempt 2: building…');
  });
});
