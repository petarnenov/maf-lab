/** One place in the repository matching a question (search_codebase, via /api/code/snippets). */
export interface CodeSnippet {
  path: string;
  startLine: number | null;
  endLine: number | null;
  symbol: string | null;
  section: string;
  kind: 'code' | 'docs';
  language: string;
  score: number;
  snippet: string;
}

export interface CodeSearchResult {
  results: CodeSnippet[];
  totalMatches: number;
  truncated: boolean;
  refineHint: string | null;
}
