// The audit screen's view of the record, as the core's audit trail serves it (/api/admin/compliance/*).

export type AuditKind =
  | 'tool'
  | 'conversation.delete'
  | 'compliance.export'
  | 'a2a.request'
  | 'a2a.consultation'
  | 'fee.adjustment';

export interface ChainReport {
  intact: boolean;
  checked: number;
  /** Rows written before chaining began: reported, never rewritten. */
  unchained: number;
  from: string | null;
  to: string | null;
  head: string | null;
  firstBrokenId: number | null;
  reason: string | null;
}

export interface AuditAction {
  id: number;
  at: string;
  principalId: string;
  kind: AuditKind | null;
  action: string;
  /** Identifiers only (key=value); empty when the action carried none. */
  arguments: string;
  outcome: string;
  durationMs: number;
  conversationId: string | null;
  turnId: string | null;
  hash: string | null;
}

export interface ActionPage {
  actions: AuditAction[];
  nextCursor: number | null;
}

export interface ExportManifest {
  tenantId: string;
  subjectUserId: string | null;
  from: string;
  to: string;
  generatedAt: string;
  by: string;
  counts: Record<string, number>;
  sha256: string;
  auditChainHead: string | null;
}
