import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Markdown } from './Markdown';
import { safeUrl } from './safeUrl';

const renderMd = (text: string) => render(<Markdown text={text} />).container;

describe('Markdown', () => {
  it('renders lists, emphasis, code and a table as elements', () => {
    const c = renderMd(
      [
        '**FS-REQUIRED** means *no schedule*.',
        '',
        '1. Open the failed run',
        '2. Assign the schedule',
        '',
        '- one',
        '- two',
        '',
        'Run `make index` or:',
        '',
        '```',
        'make rebuild-index FORCE=1',
        '```',
        '',
        '| Class | Trade |',
        '|---|---|',
        '| US equity | -8 000 $ |',
      ].join('\n'),
    );

    expect(c.querySelector('strong')).toHaveTextContent('FS-REQUIRED');
    expect(c.querySelector('em')).toHaveTextContent('no schedule');
    expect(c.querySelectorAll('ol li')).toHaveLength(2);
    expect(c.querySelectorAll('ul li')).toHaveLength(2);
    expect(c.querySelector('p code')).toHaveTextContent('make index');
    expect(c.querySelector('pre code')).toHaveTextContent('make rebuild-index FORCE=1');
    const table = screen.getByRole('table');
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toEqual(['Class', 'Trade']);
    // A number cell is aligned right, a text cell is not.
    const [name, trade] = within(table).getAllByRole('cell');
    expect(trade.className).not.toBe('');
    expect(name.className).toBe('');
    expect(c.textContent).not.toContain('**');
  });

  it('shows raw HTML as text and creates no element from it', () => {
    const c = renderMd('Hi <script>alert(1)</script> and <img src=x onerror=alert(1)>');

    expect(c.querySelector('script')).toBeNull();
    expect(c.querySelector('img')).toBeNull();
    expect(c.textContent).toContain('<script>');
    expect(c.textContent).toContain('<img src=x onerror=alert(1)>');
  });

  it('renders a link with an unsafe scheme as plain text', () => {
    const c = renderMd('[click](javascript:alert(1)) and [data](data:text/html,x)');

    expect(c.querySelector('a')).toBeNull();
    expect(c.textContent).toContain('click');
  });

  it('opens a safe link in a new tab without an opener', () => {
    renderMd('[docs](https://example.com/x) and [mail](mailto:ops@example.com)');

    const docs = screen.getByRole('link', { name: 'docs' });
    expect(docs).toHaveAttribute('href', 'https://example.com/x');
    expect(docs).toHaveAttribute('target', '_blank');
    expect(docs).toHaveAttribute('rel', 'noopener noreferrer');
    expect(screen.getByRole('link', { name: 'mail' })).toHaveAttribute(
      'href',
      'mailto:ops@example.com',
    );
  });

  it('shows an image as its alt text and loads nothing', () => {
    const c = renderMd('![a chart of fees](https://example.com/chart.png)');

    expect(c.querySelector('img')).toBeNull();
    expect(c.textContent).toContain('a chart of fees');
  });

  it('renders half-written markdown as far as it goes', () => {
    expect(() => renderMd('| Class | Trade |\n|---|')).not.toThrow();
    expect(() => renderMd('1. Open the run\n2. Assi')).not.toThrow();
    expect(() => renderMd('Some **bold')).not.toThrow();
  });

  it('keeps only http, https and mailto addresses', () => {
    expect(safeUrl('https://x.test')).toBe('https://x.test');
    expect(safeUrl('mailto:a@b.c')).toBe('mailto:a@b.c');
    expect(safeUrl(' JavaScript:alert(1)')).toBeNull();
    expect(safeUrl('/relative')).toBeNull();
  });
});
