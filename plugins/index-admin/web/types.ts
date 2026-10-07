import type { AdminJob } from '@maf/plugin-api';

export interface StaleDocument {
  docId: string;
  sourcePath: string;
  sourceUpdatedAt: string;
  indexedUpdatedAt: string;
}

/**
 * The corpus's graph against the source (add-graph-drift); reason is "unreachable" when it could not be read and
 * "not-built" when the corpus is built into no graph.
 */
export interface GraphDrift {
  available: boolean;
  reason: string | null;
  outOfSync: number;
  outOfSyncPercent: number;
  missingFromGraph: string[];
  behind: string[];
  notInCorpus: string[];
}

export interface DriftReport {
  totalDocuments: number;
  staleDocuments: number;
  stalePercent: number;
  stale: StaleDocument[];
  missingFromIndex: string[];
  graph?: GraphDrift | null;
}

export interface ModelVersionCount {
  modelVersion: string;
  chunks: number;
}

export interface IndexStatus {
  modelVersions: ModelVersionCount[];
  activeDenseVector: string;
  currentJob?: AdminJob | null;
}

/** A corpus an installed plugin declares, by its plugin's name. */
export interface CorpusView {
  name: string;
  hasGraph: boolean;
}
