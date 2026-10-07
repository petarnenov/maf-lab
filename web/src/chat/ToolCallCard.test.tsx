import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ToolCallCard } from './ToolCallCard';

describe('ToolCallCard', () => {
  it('renders a running search_documents call', () => {
    render(
      <ToolCallCard
        call={{
          callId: 'c1',
          toolName: 'search_documents',
          argumentSummary: 'query="fee schedule"',
          status: 'running',
        }}
      />,
    );
    const card = screen.getByTestId('tool-call-card');
    expect(card).toHaveAttribute('data-state', 'running');
    expect(card).toHaveTextContent('Calling search_documents…');
    expect(card).toHaveTextContent('query="fee schedule"');
    expect(card).not.toHaveTextContent('sources');
  });

  it('renders a finished call with result summary and source count', () => {
    render(
      <ToolCallCard
        call={{
          callId: 'c1',
          toolName: 'search_documents',
          argumentSummary: 'query="fee schedule"',
          status: 'finished',
          resultSummary: '5 snippets from 3 documents',
          sourceCount: 5,
          isError: false,
        }}
      />,
    );
    const card = screen.getByTestId('tool-call-card');
    expect(card).toHaveAttribute('data-state', 'finished');
    expect(card).toHaveTextContent('Called search_documents');
    expect(card).toHaveTextContent('5 snippets from 3 documents');
    expect(card).toHaveTextContent('5 sources');
  });

  it('renders an errored call', () => {
    render(
      <ToolCallCard
        call={{
          callId: 'c3',
          toolName: 'search_billing_runs',
          argumentSummary: '',
          status: 'finished',
          resultSummary: 'Billing runs are temporarily unavailable',
          sourceCount: 0,
          isError: true,
        }}
      />,
    );
    expect(screen.getByTestId('tool-call-card')).toHaveAttribute('data-state', 'error');
  });
});
