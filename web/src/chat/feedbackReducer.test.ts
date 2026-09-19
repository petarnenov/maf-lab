import { describe, expect, it } from 'vitest';
import { feedbackReducer, feedbackStatus, type FeedbackState } from './feedbackReducer';

describe('feedbackReducer', () => {
  it('moves idle → sending → sent per turn and kind', () => {
    let state: FeedbackState = {};
    state = feedbackReducer(state, { type: 'send_started', turnId: 't1', kind: 'wrong_document' });
    expect(feedbackStatus(state, 't1', 'wrong_document')).toBe('sending');
    expect(feedbackStatus(state, 't1', 'wrong_tool')).toBe('idle');
    state = feedbackReducer(state, {
      type: 'send_succeeded',
      turnId: 't1',
      kind: 'wrong_document',
    });
    expect(feedbackStatus(state, 't1', 'wrong_document')).toBe('sent');
    expect(feedbackStatus(state, 't2', 'wrong_document')).toBe('idle');
  });

  it('moves sending → error and allows a retry', () => {
    let state: FeedbackState = {};
    state = feedbackReducer(state, { type: 'send_started', turnId: 't1', kind: 'wrong_answer' });
    state = feedbackReducer(state, { type: 'send_failed', turnId: 't1', kind: 'wrong_answer' });
    expect(feedbackStatus(state, 't1', 'wrong_answer')).toBe('error');
    state = feedbackReducer(state, { type: 'send_started', turnId: 't1', kind: 'wrong_answer' });
    expect(feedbackStatus(state, 't1', 'wrong_answer')).toBe('sending');
  });

  it('ignores duplicate sends and out-of-order completions', () => {
    const sent: FeedbackState = { t1: { wrong_tool: 'sent' } };
    expect(feedbackReducer(sent, { type: 'send_started', turnId: 't1', kind: 'wrong_tool' })).toBe(
      sent,
    );
    const idle: FeedbackState = {};
    expect(
      feedbackReducer(idle, { type: 'send_succeeded', turnId: 't1', kind: 'wrong_tool' }),
    ).toBe(idle);
    expect(feedbackReducer(idle, { type: 'send_failed', turnId: 't1', kind: 'wrong_tool' })).toBe(
      idle,
    );
  });
});
