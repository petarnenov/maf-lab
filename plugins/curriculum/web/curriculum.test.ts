import { existsSync, readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { CURRICULUM, NOT_COVERED } from './curriculum';

// This file sits at plugins/curriculum/web/, three levels below the repository root. It reads the core's routes as
// text, never as an import, so the plugin import boundary holds.
const repo = resolve(__dirname, '../../..');
const app = readFileSync(resolve(repo, 'web/src/App.tsx'), 'utf8');
// `chat/:conversationId?` counts as `/chat`: the optional segment and its slash are dropped.
const routes = new Set(
  [...app.matchAll(/path="([^"*:]+)/g)].map((m) => `/${m[1].replace(/\/$/, '')}`),
);

const entries = CURRICULUM.flatMap((section) =>
  section.entries.map((entry) => ({ section: section.id, ...entry })),
);

describe('curriculum content', () => {
  it('has sections with entries and unique ids', () => {
    expect(CURRICULUM.length).toBeGreaterThan(0);
    expect(new Set(CURRICULUM.map((s) => s.id)).size).toBe(CURRICULUM.length);
    for (const section of CURRICULUM) expect(section.entries.length).toBeGreaterThan(0);
  });

  it.each(entries.map((e) => [e.concept, e] as const))('%s names what exists', (_, entry) => {
    expect(entry.summary.trim().length).toBeGreaterThan(40);
    expect(entry.paths.length).toBeGreaterThan(0);
    for (const path of entry.paths) {
      // Only the core's own files: a path into a plugin folder would turn red when that folder is deleted.
      expect(path, `${path} is a core path`).not.toMatch(/^plugins\//);
      expect(existsSync(resolve(repo, path)), `${path} exists`).toBe(true);
    }
    const spec = resolve(repo, 'openspec/specs', entry.spec);
    expect(existsSync(spec) && statSync(spec).isDirectory(), `spec ${entry.spec}`).toBe(true);
    if (entry.screen && !entry.screen.optional) {
      const route = entry.screen.to.split('#')[0].replace(/\/$/, '');
      expect(routes.has(route), `route ${route}`).toBe(true);
    }
  });

  it('says what is not covered, with a reason for each', () => {
    expect(NOT_COVERED.length).toBeGreaterThan(0);
    for (const gap of NOT_COVERED) expect(gap.reason.trim().length).toBeGreaterThan(20);
  });
});
