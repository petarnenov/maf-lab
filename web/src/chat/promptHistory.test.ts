import { describe, expect, it } from 'vitest';
import { idle, step, type RecallState } from './promptHistory';

const history = ['first', 'second', 'third'];

/** Presses the keys in order from `start`, returning the text after each press (null when the key was not taken). */
function press(keys: ('up' | 'down')[], draft = '', start: RecallState = idle) {
  let state = start;
  let text = draft;
  return keys.map((key) => {
    const next = step(history, state, key, text);
    if (next === null) return null;
    state = next.state;
    text = next.text;
    return text;
  });
}

describe('promptHistory.step', () => {
  it('steps back through the prompts, newest first', () => {
    expect(press(['up', 'up', 'up'])).toEqual(['third', 'second', 'first']);
  });

  it('stops at the oldest prompt and still takes the key', () => {
    expect(press(['up', 'up', 'up', 'up'])).toEqual(['third', 'second', 'first', 'first']);
  });

  it('steps forward and past the newest prompt restores the draft', () => {
    expect(press(['up', 'up', 'down', 'down'], 'half a question')).toEqual([
      'third',
      'second',
      'third',
      'half a question',
    ]);
  });

  it('restores an empty draft', () => {
    expect(press(['up', 'down'])).toEqual(['third', '']);
  });

  it('ends the recall after restoring the draft, so Down is the key again', () => {
    expect(press(['up', 'down', 'down'], 'draft')).toEqual(['third', 'draft', null]);
  });

  it('does nothing with no history', () => {
    expect(step([], idle, 'up', 'draft')).toBeNull();
  });

  it('does nothing on Down when no recall is in progress', () => {
    expect(step(history, idle, 'down', 'draft')).toBeNull();
  });
});
