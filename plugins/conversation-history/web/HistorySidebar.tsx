import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, type KeyboardEvent } from 'react';
import type { ChatContext } from '@maf/plugin-api';
import {
  CONVERSATIONS,
  titleError,
  useConversations,
  useDeleteConversation,
  useRenameConversation,
} from './conversationsApi';
import styles from './HistorySidebar.module.css';
import { relativeTime } from './relativeTime';
import { runsFinished as defaultRunsFinished, type RunsFinished } from './runsFinished';
import type { ConversationSummary } from './types';
import { useDebouncedValue } from './useDebouncedValue';

export const SEARCH_DEBOUNCE_MS = 250;

/**
 * The user's own conversations: search, open, rename, delete. Content only: the chat draws the sidebar around it (its
 * landmark, heading, collapse and drawer) and starts a new conversation from its own header.
 */
export function HistorySidebar({
  context,
  runsFinished = defaultRunsFinished,
}: {
  context: ChatContext;
  runsFinished?: RunsFinished;
}) {
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search.trim(), SEARCH_DEBOUNCE_MS);
  const list = useConversations(debounced);
  const [confirming, setConfirming] = useState<ConversationSummary | null>(null);
  const remove = useDeleteConversation();
  const client = useQueryClient();
  const activeId = context.conversationId;

  // A finished run changes the list (a new conversation, its last activity, its turn count).
  useEffect(
    () =>
      runsFinished.subscribe(() => void client.invalidateQueries({ queryKey: [CONVERSATIONS] })),
    [runsFinished, client],
  );

  const conversations = list.data?.pages.flatMap((p) => p.conversations) ?? [];

  return (
    <div className={styles.sidebar}>
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
            onSelect={() => context.openConversation?.(c.conversationId)}
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
                      // The conversation on screen is gone: the chat starts a new one.
                      if (id === activeId) context.startNew?.();
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
    </div>
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
    // One rename at a time: a second Enter while the first is on its way sends nothing.
    if (rename.isPending) return;
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
