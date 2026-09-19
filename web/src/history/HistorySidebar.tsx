import { useState, type KeyboardEvent } from 'react';
import type { ConversationSummary } from '../api/types';
import styles from './HistorySidebar.module.css';
import {
  titleError,
  useConversations,
  useDeleteConversation,
  useRenameConversation,
} from './historyApi';
import { relativeTime } from './relativeTime';
import { useDebouncedValue } from './useDebouncedValue';

export const SEARCH_DEBOUNCE_MS = 250;

/** The user's own conversations: search, open, start new, rename, delete. */
export function HistorySidebar({
  activeId,
  onSelect,
  onNew,
  onDeleted,
  collapsed = false,
  onToggleCollapsed,
}: {
  activeId?: string;
  onSelect: (conversationId: string) => void;
  onNew: () => void;
  /** Called after a conversation was deleted (e.g. to leave it if it was open). */
  onDeleted?: (conversationId: string) => void;
  collapsed?: boolean;
  onToggleCollapsed?: () => void;
}) {
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search.trim(), SEARCH_DEBOUNCE_MS);
  const list = useConversations(debounced);
  const [confirming, setConfirming] = useState<ConversationSummary | null>(null);
  const remove = useDeleteConversation();

  if (collapsed) {
    return (
      <nav className={`${styles.sidebar} ${styles.rail}`} aria-label="Conversation history">
        <button
          type="button"
          className={styles.iconButton}
          aria-label="Expand history"
          onClick={onToggleCollapsed}
        >
          ☰
        </button>
        <button
          type="button"
          className={styles.iconButton}
          aria-label="New conversation"
          onClick={onNew}
        >
          ＋
        </button>
      </nav>
    );
  }

  const conversations = list.data?.pages.flatMap((p) => p.conversations) ?? [];

  return (
    <nav className={styles.sidebar} aria-label="Conversation history">
      <div className={styles.header}>
        <h2 className={styles.heading}>History</h2>
        {onToggleCollapsed && (
          <button
            type="button"
            className={styles.iconButton}
            aria-label="Collapse history"
            onClick={onToggleCollapsed}
          >
            ⟨
          </button>
        )}
      </div>
      <button type="button" className={styles.newButton} onClick={onNew}>
        ＋ New conversation
      </button>
      <input
        type="search"
        className={styles.search}
        aria-label="Search conversations"
        placeholder="Search…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />

      {list.isLoading && <p className={styles.muted}>Loading…</p>}
      {list.isError && (
        <p className={styles.error} role="alert">
          Could not load conversations.
        </p>
      )}
      {!list.isLoading && !list.isError && conversations.length === 0 && (
        <p className={styles.muted}>
          {debounced ? 'No matching conversations.' : 'No conversations yet.'}
        </p>
      )}

      <ul className={styles.list} aria-label="Conversations">
        {conversations.map((c) => (
          <HistoryItem
            key={c.conversationId}
            conversation={c}
            active={c.conversationId === activeId}
            onSelect={() => onSelect(c.conversationId)}
            onDelete={() => setConfirming(c)}
          />
        ))}
      </ul>

      {list.hasNextPage && (
        <button
          type="button"
          className={styles.loadMore}
          disabled={list.isFetchingNextPage}
          onClick={() => void list.fetchNextPage()}
        >
          {list.isFetchingNextPage ? 'Loading…' : 'Load more'}
        </button>
      )}

      {confirming && (
        <div className={styles.backdrop}>
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-title"
            className={styles.dialog}
          >
            <h3 id="delete-title" className={styles.dialogTitle}>
              Delete “{confirming.title}”?
            </h3>
            <p className={styles.muted}>
              It disappears from your history and cannot be continued. Flagged turns stay available
              for review.
            </p>
            {remove.isError && (
              <p className={styles.error} role="alert">
                Could not delete the conversation.
              </p>
            )}
            <div className={styles.dialogActions}>
              <button type="button" onClick={() => setConfirming(null)} disabled={remove.isPending}>
                Cancel
              </button>
              <button
                type="button"
                className={styles.danger}
                disabled={remove.isPending}
                onClick={() => {
                  const id = confirming.conversationId;
                  remove.mutate(id, {
                    onSuccess: () => {
                      setConfirming(null);
                      onDeleted?.(id);
                    },
                  });
                }}
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </nav>
  );
}

function HistoryItem({
  conversation,
  active,
  onSelect,
  onDelete,
}: {
  conversation: ConversationSummary;
  active: boolean;
  onSelect: () => void;
  onDelete: () => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(conversation.title);
  const [error, setError] = useState<string | null>(null);
  const rename = useRenameConversation();

  function save() {
    const problem = titleError(draft);
    if (problem) {
      setError(problem);
      return;
    }
    rename.mutate(
      { id: conversation.conversationId, title: draft },
      {
        onSuccess: () => {
          setEditing(false);
          setError(null);
        },
        onError: () => setError('Could not rename the conversation.'),
      },
    );
  }

  function onKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter') {
      e.preventDefault();
      save();
    } else if (e.key === 'Escape') {
      e.preventDefault();
      setEditing(false);
      setDraft(conversation.title);
      setError(null);
    }
  }

  const turns = conversation.turnCount === 1 ? '1 turn' : `${conversation.turnCount} turns`;

  return (
    <li className={`${styles.item} ${active ? styles.active : ''}`} data-testid="history-item">
      {editing ? (
        <div className={styles.renameRow}>
          <input
            aria-label="Conversation title"
            className={styles.renameInput}
            value={draft}
            autoFocus
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={onKeyDown}
          />
          {error && (
            <span className={styles.error} role="alert">
              {error}
            </span>
          )}
        </div>
      ) : (
        <button
          type="button"
          className={styles.itemButton}
          aria-current={active ? 'page' : undefined}
          onClick={onSelect}
          title={conversation.title}
        >
          <span className={styles.title}>{conversation.title}</span>
          <span className={styles.meta}>
            {relativeTime(conversation.lastActivityAt)} · {turns}
          </span>
        </button>
      )}
      {!editing && (
        <div className={styles.menuWrap}>
          <button
            type="button"
            className={styles.menuButton}
            aria-label={`Actions for ${conversation.title}`}
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen((o) => !o)}
          >
            ⋯
          </button>
          {menuOpen && (
            <div role="menu" className={styles.menu}>
              <button
                type="button"
                role="menuitem"
                onClick={() => {
                  setMenuOpen(false);
                  setDraft(conversation.title);
                  setEditing(true);
                }}
              >
                Rename
              </button>
              <button
                type="button"
                role="menuitem"
                onClick={() => {
                  setMenuOpen(false);
                  onDelete();
                }}
              >
                Delete
              </button>
            </div>
          )}
        </div>
      )}
    </li>
  );
}
