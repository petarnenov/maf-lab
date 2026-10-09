/** React Query keys of the Coverage screen: a run's end invalidates the tree, the file and the runs together. */
export const coverageKeys = {
  all: ['coverage'] as const,
  tree: ['coverage', 'tree'] as const,
  file: (path: string) => ['coverage', 'file', path] as const,
  refresh: ['coverage', 'refresh'] as const,
  models: (path: string) => ['coverage', 'models', path] as const,
  runs: (path: string) => ['coverage', 'runs', path] as const,
};
