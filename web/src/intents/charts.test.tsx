import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Columns, Lines } from './charts';

describe('intent charts', () => {
  it('draws a lone measured bucket among empty ones, even on a long axis', () => {
    const values: (number | null)[] = Array.from({ length: 30 }, () => null);
    values[10] = 420;
    const { container } = render(
      <Lines
        ariaLabel="lone"
        lines={[{ label: 'p50', className: 'a', values }]}
        xLabels={values.map((_, i) => String(i))}
        xTicks={[]}
        yDomain={[0, 2000]}
        format={String}
      />,
    );
    expect(screen.getByRole('img', { name: 'lone' })).toBeInTheDocument();
    expect(container.querySelectorAll('circle')).toHaveLength(1);
    expect(container.querySelectorAll('polyline')).toHaveLength(0);
  });

  it('draws one path per non-zero segment and none for an empty column', () => {
    const { container } = render(
      <Columns
        ariaLabel="cols"
        series={[
          { label: 'used', className: 'u' },
          { label: 'gated', className: 'g' },
        ]}
        columns={[
          { label: 'a', values: [3, 1] },
          { label: 'b', values: [0, 0] },
          { label: 'c', values: [0, 2] },
        ]}
        xTicks={[]}
        thresholds={[{ at: 1, label: 'floor 0.5' }]}
      />,
    );
    expect(container.querySelectorAll('path')).toHaveLength(3);
    expect(screen.getByRole('img', { name: 'cols' })).toHaveTextContent('floor 0.5');
  });
});
