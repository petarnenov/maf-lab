import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Progress } from './Progress';

describe('Progress', () => {
  it('is an indeterminate progress bar that names the work when there is no step count', () => {
    render(<Progress label="Checking the test agent…" />);

    const bar = screen.getByRole('progressbar', { name: 'Checking the test agent…' });
    expect(bar).not.toHaveAttribute('aria-valuenow');
    expect(bar).not.toHaveAttribute('aria-valuemax');
    expect(screen.getByText('Checking the test agent…')).toBeInTheDocument();
  });

  it('is determinate, with the step it is at, when it is given a count', () => {
    render(<Progress label="Indexing" value={3} max={8} />);

    const bar = screen.getByRole('progressbar', { name: 'Indexing' });
    expect(bar).toHaveAttribute('aria-valuenow', '3');
    expect(bar).toHaveAttribute('aria-valuemax', '8');
    expect(screen.getByText('Indexing (3/8)')).toBeInTheDocument();
  });
});
