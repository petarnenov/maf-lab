/** Files a unified diff touches, with the lines each adds and removes. */
export interface DiffFile {
  path: string;
  added: number;
  removed: number;
}

export function summarizeDiff(diff: string): DiffFile[] {
  const files: DiffFile[] = [];
  let current: DiffFile | null = null;
  for (const line of diff.split('\n')) {
    if (line.startsWith('+++ ')) {
      const name = line.slice(4);
      current = { path: name.startsWith('b/') ? name.slice(2) : name, added: 0, removed: 0 };
      files.push(current);
    } else if (current && line.startsWith('+') && !line.startsWith('+++')) {
      current.added++;
    } else if (current && line.startsWith('-') && !line.startsWith('---')) {
      current.removed++;
    }
  }
  return files;
}
