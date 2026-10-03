import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { domainColor } from './domainData';
import { kindColor } from './kindColors';

// The tokens the theme defines; a colour helper may only hand these out, so every mark follows light and dark.
const css = readFileSync(resolve(import.meta.dirname, '../index.css'), 'utf8');
const defined = new Set([...css.matchAll(/(--kind-[a-z]+):\s*light-dark\(/g)].map((m) => m[1]));

const token = (value: string) => /^var\((--kind-[a-z]+)\)$/.exec(value)?.[1];

describe('category colours', () => {
  it.each([
    'turn.start',
    'model.call',
    'tool.unknown',
    'guardrail',
    'tool.call',
    'envelope',
    'retrieval',
    'relevance',
    'graph',
    'answer.check',
    'domain',
    'boundary',
    'intent',
    'answer.delta',
  ])('%s resolves to a theme token', (kind) => {
    expect(defined).toContain(token(kindColor(kind)));
  });

  it.each(['billing', 'portfolio', undefined])('domain %s resolves to a theme token', (domain) => {
    expect(defined).toContain(token(domainColor(domain)));
  });
});
