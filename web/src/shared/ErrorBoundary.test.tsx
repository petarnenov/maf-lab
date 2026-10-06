import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorBoundary } from './ErrorBoundary';

const SECRET = 'connection to qdrant-host:6333 refused';

function Boom(): ReactNode {
  throw new Error(SECRET);
}

describe('ErrorBoundary', () => {
  beforeEach(() => {
    // React logs a caught error itself; the boundary logs one too. Neither belongs in test output.
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders its children when nothing fails', () => {
    render(
      <ErrorBoundary area="Retrieval">
        <p>all well</p>
      </ErrorBoundary>,
    );
    expect(screen.getByText('all well')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('replaces a failing child with a fallback naming the area', () => {
    render(
      <ErrorBoundary area="Retrieval">
        <Boom />
      </ErrorBoundary>,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Retrieval could not be shown');
  });

  it('puts nothing from the error on the page', () => {
    const { container } = render(
      <ErrorBoundary area="Retrieval">
        <Boom />
      </ErrorBoundary>,
    );
    expect(container.textContent).not.toContain(SECRET);
    expect(container.textContent).not.toContain('qdrant-host');
    expect(container.textContent).not.toContain('Error');
  });

  it('keeps the detail for the console', () => {
    render(
      <ErrorBoundary area="Retrieval">
        <Boom />
      </ErrorBoundary>,
    );
    expect(console.error).toHaveBeenCalledWith(
      '[Retrieval] render failed',
      expect.objectContaining({ message: SECRET }),
      expect.anything(),
    );
  });
});
