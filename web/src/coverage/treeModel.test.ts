import { describe, expect, it } from 'vitest';
import type { CoverageTree, CoverageTreeFile } from '../api/types';
import { buildTree, flatten, type TreeView } from './treeModel';

export function file(path: string, pctValue: number, overrides: Partial<CoverageTreeFile> = {}): CoverageTreeFile {
  return {
    path,
    toolchain: path.endsWith('.cs') ? 'dotnet' : 'vitest',
    linesTotal: 100,
    linesCovered: pctValue,
    branchesTotal: 0,
    branchesCovered: 0,
    pct: pctValue,
    threshold: 80,
    thresholdIsOverride: false,
    belowThreshold: pctValue < 80,
    commit: 'abcdef1234567',
    measuredAt: '2026-09-30T10:00:00Z',
    candidate: null,
    run: null,
    ...overrides,
  };
}

export const sampleTree: CoverageTree = {
  hasSnapshot: true,
  defaultThresholdPct: 80,
  files: [
    file('src/Lab/Alpha.cs', 95),
    file('src/Lab/Beta.cs', 40),
    file('src/Lab/Deep/Gamma.cs', 70),
    file('web/src/App.tsx', 90),
  ],
  folders: [
    { path: 'src', linesTotal: 300, linesCovered: 205, pct: 68.3, files: 3, filesBelowThreshold: 2 },
    { path: 'src/Lab', linesTotal: 300, linesCovered: 205, pct: 68.3, files: 3, filesBelowThreshold: 2 },
    { path: 'src/Lab/Deep', linesTotal: 100, linesCovered: 70, pct: 70, files: 1, filesBelowThreshold: 1 },
    { path: 'web', linesTotal: 100, linesCovered: 90, pct: 90, files: 1, filesBelowThreshold: 0 },
    { path: 'web/src', linesTotal: 100, linesCovered: 90, pct: 90, files: 1, filesBelowThreshold: 0 },
  ],
};

const view = (overrides: Partial<TreeView> = {}): TreeView => ({
  sort: 'name',
  dir: 'asc',
  filter: '',
  belowOnly: false,
  ...overrides,
});

const paths = (v: TreeView, collapsed = new Set<string>()) =>
  flatten(buildTree(sampleTree, v), collapsed).map((r) => `${'  '.repeat(r.depth)}${r.node.name}`);

describe('coverage tree', () => {
  it('nests files under their folders, folders first', () => {
    expect(paths(view())).toEqual([
      'src',
      '  Lab',
      '    Deep',
      '      Gamma.cs',
      '    Alpha.cs',
      '    Beta.cs',
      'web',
      '  src',
      '    App.tsx',
    ]);
  });

  it('sorts siblings by coverage, least covered first', () => {
    const rows = paths(view({ sort: 'coverage' }));
    expect(rows.slice(2, 6)).toEqual(['    Beta.cs', '    Deep', '      Gamma.cs', '    Alpha.cs']);
  });

  it('shows only files below threshold, with their folders', () => {
    expect(paths(view({ belowOnly: true }))).toEqual([
      'src',
      '  Lab',
      '    Deep',
      '      Gamma.cs',
      '    Beta.cs',
    ]);
  });

  it('filters by path, case-insensitively', () => {
    expect(paths(view({ filter: 'app' }))).toEqual(['web', '  src', '    App.tsx']);
  });

  it('hides the children of a collapsed folder', () => {
    expect(paths(view(), new Set(['src/Lab']))).toEqual(['src', '  Lab', 'web', '  src', '    App.tsx']);
  });

  it('keeps each folder at its own coverage when filtered', () => {
    const [src] = buildTree(sampleTree, view({ filter: 'Alpha' }));
    expect(src.pct).toBe(68.3);
  });
});
