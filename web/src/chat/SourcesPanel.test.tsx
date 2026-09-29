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

describe('SourcesPanel with code sources (add-codebase-domain)', () => {
  const code = (i: number, symbol = 'Symbol') => ({
    docId: `src/Deep/Folder/File${i}.cs`,
    sectionPath: `src/Deep/Folder/File${i}.cs:1-9 › ${symbol}`,
    sourcePath: `src/Deep/Folder/File${i}.cs`,
    snippet: 'code',
    kind: 'code',
    startLine: 1,
    endLine: 9,
    symbol,
  });

  it('shows a code source as its file and lines, with its folder and symbol beneath, and opens it', async () => {
    const opened: string[] = [];
    const long =
      'AgentCardTests.A_partner_exchanges_its_credentials_for_a_token_and_a_stranger_does_not';
    render(<SourcesPanel sources={[code(1, long)]} onOpenCode={(s) => opened.push(s.docId)} />);

    const button = screen.getByRole('button', { name: /File1\.cs:1–9/ });
    expect(button).toHaveTextContent('File1.cs:1–9');
    expect(button).toHaveTextContent(`src/Deep/Folder › ${long}`);
    // The full place stays readable on hover even where the row is cut short.
    expect(button).toHaveAttribute('title', expect.stringContaining(long));
    await userEvent.click(button);
    expect(opened).toEqual(['src/Deep/Folder/File1.cs']);
  });

  it('shows five sources and the rest on request', async () => {
    render(
      <SourcesPanel sources={[1, 2, 3, 4, 5, 6, 7].map((i) => code(i))} onOpenCode={() => {}} />,
    );

    expect(screen.getByText('Sources (7)')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(5);
    await userEvent.click(screen.getByRole('button', { name: 'Show all 7' }));
    expect(screen.getAllByRole('listitem')).toHaveLength(7);
  });
});
