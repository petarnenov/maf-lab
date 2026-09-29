import { useQuery } from '@tanstack/react-query';
import type { CodeSearchResult, CodeSnippet, SourceRef } from '../api/types';
import { useApi } from '../auth/useAuth';

/** The snippets of one file, in the order search ranked the file's best one. */
export interface FileGroup {
  path: string;
  snippets: CodeSnippet[];
}

/** Groups snippets per file: files in rank order of their best snippet, each file's snippets in line order. */
export function groupByFile(snippets: CodeSnippet[]): FileGroup[] {
  const groups = new Map<string, CodeSnippet[]>();
  for (const s of snippets) {
    const list = groups.get(s.path) ?? [];
    list.push(s);
    groups.set(s.path, list);
  }
  return [...groups].map(([path, list]) => ({
    path,
    snippets: [...list].sort((a, b) => (a.startLine ?? 0) - (b.startLine ?? 0)),
  }));
}

export function useCodeSnippets(question: string, enabled: boolean) {
  const api = useApi();
  return useQuery({
    queryKey: ['code-snippets', question],
    queryFn: () =>
      api<CodeSearchResult>('/api/code/snippets', { method: 'POST', body: { question } }),
    enabled: enabled && question.trim().length > 0,
    // The repository does not change under a conversation; a question asked again is the same answer.
    staleTime: 5 * 60_000,
  });
}

/** The key that names one snippet across Sources and the Code snippets tab. */
export function snippetKey(s: { path: string; startLine: number | null }): string {
  return `${s.path}:${s.startLine ?? 0}`;
}

/**
 * The code an answer used, as snippets (add-codebase-domain): the turn's sources that came from search_codebase, in the
 * order the answer received them.
 */
export function codeSnippetsOf(sources: SourceRef[]): CodeSnippet[] {
  return sources
    .filter((s) => s.kind === 'code')
    .map((s) => ({
      path: s.sourcePath || s.docId,
      startLine: s.startLine ?? null,
      endLine: s.endLine ?? null,
      symbol: s.symbol ?? null,
      section: s.sectionPath,
      kind: 'code' as const,
      language: s.language ?? '',
      score: 0,
      snippet: s.snippet,
    }));
}
