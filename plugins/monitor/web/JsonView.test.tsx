import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { JsonView } from './JsonView';

describe('JsonView', () => {
  it('expands and collapses nested values', async () => {
    render(
      <JsonView value={{ settings: { mode: 'hybrid', limit: 20 }, ok: true }} expandDepth={1} />,
    );

    expect(screen.getByText('true')).toBeInTheDocument();
    expect(screen.queryByText('"hybrid"')).not.toBeInTheDocument();

    const toggle = screen.getByRole('button', { name: 'Expand settings' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    expect(screen.getByText('"hybrid"')).toBeInTheDocument();
    expect(screen.getByText('20')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Collapse settings' }));
    expect(screen.queryByText('"hybrid"')).not.toBeInTheDocument();
  });

  it('renders scalars and arrays', () => {
    render(<JsonView value={['a', 1, null]} expandDepth={1} />);
    expect(screen.getByText('[3]')).toBeInTheDocument();
    expect(screen.getByText('"a"')).toBeInTheDocument();
    expect(screen.getByText('null')).toBeInTheDocument();
  });
});
