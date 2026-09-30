import type { CoverageTree, CoverageTreeFile } from '../api/types';

export interface FolderNode {
  kind: 'folder';
  path: string;
  name: string;
  pct: number;
  linesTotal: number;
  linesCovered: number;
  filesBelowThreshold: number;
  children: TreeNode[];
}

export interface FileNode {
  kind: 'file';
  path: string;
  name: string;
  pct: number;
  file: CoverageTreeFile;
}

export type TreeNode = FolderNode | FileNode;

export type SortKey = 'name' | 'coverage';
export type SortDir = 'asc' | 'desc';

export interface TreeView {
  sort: SortKey;
  dir: SortDir;
  /** Case-insensitive match on the path. */
  filter: string;
  belowOnly: boolean;
}

/** One visible row of the flattened tree. */
export interface TreeRow {
  node: TreeNode;
  depth: number;
}

/**
 * Nests the server's flat lists by path. Folder numbers come from the server (over every file under the folder),
 * so a filtered view still shows each folder's real coverage.
 */
export function buildTree(tree: CoverageTree, view: TreeView): TreeNode[] {
  const needle = view.filter.trim().toLowerCase();
  const folderStats = new Map(tree.folders.map((f) => [f.path, f]));
  const roots: TreeNode[] = [];
  const folders = new Map<string, FolderNode>();

  const folderFor = (path: string): TreeNode[] => {
    if (path === '') return roots;
    const existing = folders.get(path);
    if (existing) return existing.children;
    const slash = path.lastIndexOf('/');
    const stats = folderStats.get(path);
    const node: FolderNode = {
      kind: 'folder',
      path,
      name: path.slice(slash + 1),
      pct: stats?.pct ?? 100,
      linesTotal: stats?.linesTotal ?? 0,
      linesCovered: stats?.linesCovered ?? 0,
      filesBelowThreshold: stats?.filesBelowThreshold ?? 0,
      children: [],
    };
    folders.set(path, node);
    folderFor(slash < 0 ? '' : path.slice(0, slash)).push(node);
    return node.children;
  };

  for (const file of tree.files) {
    if (view.belowOnly && !file.belowThreshold) continue;
    if (needle && !file.path.toLowerCase().includes(needle)) continue;
    const slash = file.path.lastIndexOf('/');
    folderFor(slash < 0 ? '' : file.path.slice(0, slash)).push({
      kind: 'file',
      path: file.path,
      name: file.path.slice(slash + 1),
      pct: file.pct,
      file,
    });
  }

  sortNodes(roots, view);
  return roots;
}

function sortNodes(nodes: TreeNode[], view: TreeView) {
  const sign = view.dir === 'asc' ? 1 : -1;
  nodes.sort((a, b) => {
    if (view.sort === 'coverage') {
      return sign * (a.pct - b.pct) || a.path.localeCompare(b.path);
    }
    // By name, folders come first, as in any file browser.
    if (a.kind !== b.kind) return a.kind === 'folder' ? -1 : 1;
    return sign * a.name.localeCompare(b.name);
  });
  for (const node of nodes) if (node.kind === 'folder') sortNodes(node.children, view);
}

/**
 * The rows on screen: every node whose ancestors are all open. `collapsed` holds folders the user closed, so a
 * new folder starts open and a filter shows its matches without anyone having to expand anything.
 */
export function flatten(nodes: TreeNode[], collapsed: ReadonlySet<string>, depth = 0, rows: TreeRow[] = []): TreeRow[] {
  for (const node of nodes) {
    rows.push({ node, depth });
    if (node.kind === 'folder' && !collapsed.has(node.path)) flatten(node.children, collapsed, depth + 1, rows);
  }
  return rows;
}
