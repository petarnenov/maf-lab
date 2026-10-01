import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { act, render, screen } from '@testing-library/react';
import { useRef } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { HEADER_OFFSET_VAR, useStickyHeader } from './useStickyHeader';

let headerHeight = 0;
let observerCallback: (() => void) | null = null;
const disconnect = vi.fn();

class FakeResizeObserver {
  constructor(callback: () => void) {
    observerCallback = callback;
  }
  observe() {}
  unobserve() {}
  disconnect() {
    disconnect();
  }
}

function Header() {
  const ref = useRef<HTMLElement>(null);
  const sticky = useStickyHeader(ref);
  return (
    <header ref={ref} data-sticky={sticky}>
      maf-lab
    </header>
  );
}

function offset() {
  return document.documentElement.style.getPropertyValue(HEADER_OFFSET_VAR);
}

beforeEach(() => {
  observerCallback = null;
  vi.stubGlobal('ResizeObserver', FakeResizeObserver);
  vi.stubGlobal('innerHeight', 900);
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(
    () => ({ height: headerHeight }) as DOMRect,
  );
});

afterEach(() => {
  document.documentElement.style.removeProperty(HEADER_OFFSET_VAR);
});

describe('useStickyHeader', () => {
  it('pins a header that takes at most a third of the window and publishes its height', () => {
    headerHeight = 188.5;
    render(<Header />);

    expect(screen.getByRole('banner')).toHaveAttribute('data-sticky', 'true');
    expect(offset()).toBe('189px');
  });

  it('follows the header as it wraps to more rows', () => {
    headerHeight = 56;
    render(<Header />);
    expect(offset()).toBe('56px');

    headerHeight = 281;
    act(() => observerCallback?.());

    expect(offset()).toBe('281px');
    expect(screen.getByRole('banner')).toHaveAttribute('data-sticky', 'true');
  });

  it('lets a header taller than a third of the window scroll away, with no offset', () => {
    headerHeight = 433;
    vi.stubGlobal('innerHeight', 667);
    render(<Header />);

    expect(screen.getByRole('banner')).toHaveAttribute('data-sticky', 'false');
    expect(offset()).toBe('0px');
  });

  it('re-decides when the window gets shorter', () => {
    headerHeight = 189;
    render(<Header />);
    expect(screen.getByRole('banner')).toHaveAttribute('data-sticky', 'true');

    vi.stubGlobal('innerHeight', 500);
    act(() => {
      window.dispatchEvent(new Event('resize'));
    });

    expect(screen.getByRole('banner')).toHaveAttribute('data-sticky', 'false');
    expect(offset()).toBe('0px');
  });

  it('removes the offset and stops observing on unmount', () => {
    headerHeight = 60;
    const { unmount } = render(<Header />);
    expect(offset()).toBe('60px');

    unmount();

    expect(offset()).toBe('');
    expect(disconnect).toHaveBeenCalled();
  });

  it('still measures where ResizeObserver is missing', () => {
    vi.stubGlobal('ResizeObserver', undefined);
    headerHeight = 70;
    render(<Header />);

    expect(offset()).toBe('70px');
  });
});

describe('the pinned header in CSS', () => {
  const read = (path: string) => readFileSync(resolve(import.meta.dirname, path), 'utf8');

  it('sticks to the top above content and below overlays, and not in print', () => {
    const css = read('./Layout.module.css');
    const rule = css.match(/\.header\[data-sticky='true'\] \{([^}]*)\}/)?.[1] ?? '';
    expect(rule).toContain('position: sticky');
    expect(rule).toContain('top: 0');
    const z = Number(rule.match(/z-index: (\d+)/)?.[1]);
    expect(z).toBeGreaterThan(5);
    expect(z).toBeLessThan(10);
    expect(css).toMatch(/@media print \{\s*\.header\[data-sticky='true'\] \{\s*position: static;/);
  });

  it('makes anchor jumps stop below it, and the chat screen fit below it', () => {
    expect(read('../index.css')).toMatch(
      /html \{\s*scroll-padding-top: var\(--app-header-offset, 0px\);/,
    );
    const chat = read('../chat/ChatPage.module.css');
    expect(chat).toContain('height: calc(100vh - var(--app-header-offset, 80px) - 40px)');
    expect(chat).not.toMatch(/calc\(100vh - 1[24]0px\)/);
  });
});
