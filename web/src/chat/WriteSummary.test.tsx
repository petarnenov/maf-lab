import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SummarySchema } from '../api/types';
import { WriteSummary } from './WriteSummary';

const schema: SummarySchema = {
  type: 'object',
  properties: {
    accountId: { title: 'Account', type: 'string' },
    amount: { title: 'Adjustment', type: 'number' },
    periodStart: { title: 'From', type: 'string', format: 'date' },
    note: { title: 'Note', type: 'string' },
  },
};

describe('WriteSummary', () => {
  it('shows each property by its title, in the schema order, and skips what the summary lacks', () => {
    render(
      <WriteSummary
        schema={schema}
        summary={{ amount: -200, accountId: 'A-1042', periodStart: '2026-07-01' }}
      />,
    );

    const terms = within(screen.getByTestId('write-summary'))
      .getAllByRole('term')
      .map((t) => t.textContent);
    expect(terms).toEqual(['Account', 'Adjustment', 'From']);
    expect(screen.getByText('A-1042')).toBeInTheDocument();
    expect(screen.getByText(new Intl.NumberFormat().format(-200))).toBeInTheDocument();
    expect(
      screen.getByText(new Date('2026-07-01T00:00:00').toLocaleDateString()),
    ).toBeInTheDocument();
  });

  it('shows nothing without a schema to name the fields by', () => {
    const { container } = render(<WriteSummary schema={null} summary={{ amount: -200 }} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('falls back to the property name when the schema gives no title', () => {
    render(
      <WriteSummary
        schema={{ properties: { ref: { type: 'string' } } }}
        summary={{ ref: 'x-1' }}
      />,
    );
    expect(screen.getByRole('term')).toHaveTextContent('ref');
  });
});
