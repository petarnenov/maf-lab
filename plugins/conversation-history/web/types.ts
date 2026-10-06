/** A conversation as the list shows it (GET /api/conversations). */
export interface ConversationSummary {
  conversationId: string;
  title: string;
  createdAt: string;
  lastActivityAt: string;
  turnCount: number;
}

export interface ConversationPage {
  conversations: ConversationSummary[];
  nextCursor: string | null;
}

export interface RenameConversationRequest {
  title: string;
}
