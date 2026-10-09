import type { FeedbackKind, TurnSignal, ToolCallRecord } from '@maf/plugin-api';

export interface ReviewQueueItem {
  turnId: string;
  conversationId: string;
  userId: string;
  question: string;
  answer: string;
  signals: TurnSignal[];
  toolCalls: ToolCallRecord[];
  feedbackKinds: FeedbackKind[];
  createdAt: string;
  labeled: boolean;
}

export type LabelDataset = 'selection' | 'retrieval' | 'generation';

export interface LabelRequest {
  dataset: LabelDataset;
  expectedTools?: string[] | null;
  relevantChunkIds?: string[] | null;
  referenceAnswer?: string | null;
  expectedDocIds?: string[] | null;
}
