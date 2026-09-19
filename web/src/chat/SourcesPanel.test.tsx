import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { SourcesPanel } from './SourcesPanel';

const sources = [
  {
    docId: 'd1',
    sectionPath: 'Billing > Fee schedules > Missing',
    sourcePath: 'shared/docs/fees.md',
    snippet: 'Open a ticket.',
  },
  {
    docId: 'd2',
    sectionPath: 'Step 3',
    sourcePath: 'firm-a/procedures/close.txt',
    snippet: 'Re-run the batch.',
  },
];

describe('SourcesPanel', () => {
  it('renders nothing without sources', () => {
    const { container } = render(<SourcesPanel sources={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('lists sections and expands a snippet when clicked', async () => {
    render(<SourcesPanel sources={sources} />);
    expect(screen.getByText('Sources (2)')).toBeInTheDocument();
    expect(screen.queryByText('Open a ticket.')).not.toBeInTheDocument();

    const section = screen.getByRole('button', { name: /Missing/ });
    await userEvent.click(section);
    expect(section).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('Open a ticket.')).toBeInTheDocument();

    await userEvent.click(section);
    expect(screen.queryByText('Open a ticket.')).not.toBeInTheDocument();
  });
});
