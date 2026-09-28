import type { TraceEvent } from '../api/types';
import { byKind, dataOf, firstOf } from './traceData';

// Typed views over the domain-boundary events (add-portfolio-domain): Jev's verdict, the tools' domains, the crossings.

export interface DomainData {
  probabilities?: Record<string, number>;
  scopeFloor?: number;
  inScope?: string[];
  primary?: string | null;
  crossing?: boolean;
  forcedSearches?: string[];
  offered?: string[] | null;
  unavailable?: string[] | null;
}

export interface BoundaryData {
  from?: string;
  to?: string;
  tool?: string;
  callId?: string;
  server?: string | null;
  hop?: number;
}

export interface DomainToolCallData {
  callId?: string;
  tool?: string;
  domain?: string;
  server?: string;
}

export interface DomainToolResultData extends DomainToolCallData {
  isError?: boolean;
  latencyMs?: number;
  mcpInstance?: string | null;
}

export interface TurnEndDomainData {
  domainPath?: string[];
  domainsTouched?: string[];
  domainsPredicted?: string[];
  crossings?: number;
}

/** The domains the turn's calls went to, in order, consecutive repeats collapsed — while it runs and after it ends. */
export function domainPath(events: TraceEvent[]): string[] {
  const path: string[] = [];
  for (const e of byKind(events, 'tool.call')) {
    const domain = dataOf<DomainToolCallData>(e).domain;
    if (domain && path[path.length - 1] !== domain) path.push(domain);
  }
  if (path.length > 0) return path;
  return dataOf<TurnEndDomainData>(firstOf(events, 'turn.end')).domainPath ?? [];
}

/** Colour per domain; one that is not known yet gets the neutral tone. */
export function domainColor(domain: string | undefined): string {
  switch (domain) {
    case 'billing':
      return '#2f5bd3';
    case 'portfolio':
      return '#0f7b5f';
    default:
      return '#5d6673';
  }
}
