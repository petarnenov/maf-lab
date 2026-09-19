import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useAuth } from '../auth/useAuth';
import type { AssistantTurn } from './chatReducer';
import styles from './ChatPage.module.css';
import { SourcesPanel } from './SourcesPanel';
import { ToolCallCard } from './ToolCallCard';
import { TurnFeedback } from './TurnFeedback';
import { useChatStream } from './useChatStream';

export function ChatPage() {
  const { session } = useAuth();
  const { state, send, reset } = useChatStream();
  const [draft, setDraft] = useState('');
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'end' });
  }, [state.turns]);

  if (!session) {
    return <p className={styles.empty}>Pick a dev persona in the header to start chatting.</p>;
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (state.streaming || !draft.trim()) return;
    void send(draft);
    setDraft('');
  }

  return (
    <div className={styles.page}>
      <div className={styles.toolbar}>
        <h1 className={styles.heading}>Chat</h1>
        <button type="button" onClick={reset} disabled={state.turns.length === 0}>
          New conversation
        </button>
      </div>

      <div className={styles.transcript} aria-live="polite">
        {state.turns.length === 0 && (
          <p className={styles.empty}>
            Ask a procedural question, e.g. “What is the procedure when a fee schedule is missing?”
          </p>
        )}
        {state.turns.map((turn) =>
          turn.role === 'user' ? (
            <div key={turn.id} className={`${styles.bubble} ${styles.user}`}>
              {turn.text}
            </div>
          ) : (
            <AssistantBubble key={turn.id} turn={turn} conversationId={state.conversationId} />
          ),
        )}
        <div ref={endRef} />
      </div>

      <form className={styles.composer} onSubmit={submit}>
        <textarea
          aria-label="Message"
          value={draft}
          rows={2}
          placeholder="Ask about billing procedures, runs, or fee schedules…"
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) submit(e);
          }}
        />
        <button type="submit" disabled={state.streaming || !draft.trim()}>
          {state.streaming ? 'Answering…' : 'Send'}
        </button>
      </form>
    </div>
  );
}

function AssistantBubble({
  turn,
  conversationId,
}: {
  turn: AssistantTurn;
  conversationId?: string;
}) {
  return (
    <div className={`${styles.bubble} ${styles.assistant}`} data-testid="assistant-turn">
      {turn.toolCalls.map((call) => (
        <ToolCallCard key={call.callId} call={call} />
      ))}
      {turn.text ? (
        <div className={styles.text}>{turn.text}</div>
      ) : (
        turn.status === 'streaming' && <div className={styles.thinking}>Thinking…</div>
      )}
      {turn.error && (
        <div className={styles.error} role="alert">
          {turn.error}
        </div>
      )}
      <SourcesPanel sources={turn.sources} />
      {turn.status !== 'streaming' && turn.turnId && conversationId && (
        <TurnFeedback conversationId={conversationId} turnId={turn.turnId} />
      )}
    </div>
  );
}
