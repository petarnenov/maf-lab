import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { ApiError, useAuth, useUserKey } from '../plugins/api';
import { CONTENT_ACCESS_PATH, useContentAccess, type ContentAccessState } from './contentAccess';
import styles from './Page.module.css';
import dialogStyles from './PluginSwitches.module.css';
import contentStyles from './ContentAccess.module.css';

function Control() {
  const { query, key, api } = useContentAccess();
  const client = useQueryClient();
  const [confirm, setConfirm] = useState(false);
  const [reason, setReason] = useState('');
  const [duration, setDuration] = useState('60');
  const restoreFocus = useRef<HTMLButtonElement>(null);
  const endButton = useRef<HTMLButtonElement>(null);
  const reasonInput = useRef<HTMLInputElement>(null);
  const dialog = useRef<HTMLDivElement>(null);
  const validReason = /^[A-Za-z0-9._:/#-]{2,128}$/.test(reason);
  const minutes = Number(duration);
  const validDuration = /^\d+$/.test(duration) && minutes >= 1 && minutes <= 60;
  const grant = query.data?.grant;
  const unresolved = grant != null && grant.endedAt == null;
  const write = useMutation({
    mutationFn: ({ endId }: { endId?: number }) =>
      api<ContentAccessState>(
        endId == null ? CONTENT_ACCESS_PATH : `${CONTENT_ACCESS_PATH}/${endId}/end`,
        {
          method: 'POST',
          body: endId == null ? { reason, durationMinutes: minutes } : undefined,
        },
      ),
    onMutate: () => client.cancelQueries({ queryKey: key }),
    onSuccess: (state) => {
      client.setQueryData(key, state);
      setConfirm(false);
      restoreFocus.current?.focus();
    },
    onSettled: () => client.invalidateQueries({ queryKey: key }),
  });
  const handleOpen = () => {
    write.reset();
    setReason('');
    setDuration('60');
    setConfirm(true);
  };
  const handleClose = () => {
    setConfirm(false);
    restoreFocus.current?.focus();
  };
  const handleConfirm = () => {
    if (validReason && validDuration && !write.isPending && !unresolved) {
      write.mutate({});
    }
  };
  const handleEnd = () => {
    if (grant && unresolved && !write.isPending) {
      write.mutate({ endId: grant.id });
    }
  };
  useEffect(() => {
    if (confirm) {
      reasonInput.current?.focus();
    } else if (write.isSuccess) {
      (endButton.current ?? restoreFocus.current)?.focus();
    }
  }, [confirm, write.isSuccess]);
  return (
    <section aria-label="Temporary content access">
      <h2 className={styles.subheading}>Temporary content access</h2>
      <p className={styles.notice}>
        Request access only for the current organization. Each grant ends within 60 minutes and
        cannot be extended.
      </p>
      {query.isPending && <p role="status">Loading content access…</p>}
      {query.isError && (
        <p role="alert" className={styles.error}>
          Content access status could not be confirmed. Try again before requesting access.
        </p>
      )}
      {unresolved && (
        <p role="status">
          {query.data?.active && !grant.endRequestedAt
            ? 'Content access is active.'
            : 'Content access is ending; completion is not confirmed.'}{' '}
          Reason: {grant.reason}. Started{' '}
          <time dateTime={grant.startedAt}>{new Date(grant.startedAt).toLocaleString()}</time>;
          expires{' '}
          <time dateTime={grant.expiresAt}>{new Date(grant.expiresAt).toLocaleString()}</time>.
        </p>
      )}
      {grant?.endedAt && (
        <p className={styles.notice}>
          Content access ended{' '}
          <time dateTime={grant.endedAt}>{new Date(grant.endedAt).toLocaleString()}</time>.
        </p>
      )}
      {write.isError && (
        <p role="alert" className={styles.error}>
          {write.error instanceof ApiError && write.error.status === 409
            ? 'An existing grant must finish before another can be requested.'
            : unresolved
              ? 'Ending content access has not been confirmed. The status will be checked again.'
              : 'Content access could not be confirmed. Check the current status before retrying.'}
        </p>
      )}
      {write.isPending && <p role="status">Waiting for server confirmation…</p>}
      <div className={styles.actions}>
        {unresolved ? (
          <button ref={endButton} onClick={handleEnd} disabled={write.isPending}>
            End content access
          </button>
        ) : (
          <button
            ref={restoreFocus}
            onClick={handleOpen}
            disabled={query.isPending || query.isError || write.isPending}
          >
            Request content access
          </button>
        )}
      </div>
      {confirm && (
        <div className={dialogStyles.backdrop}>
          <div
            ref={dialog}
            role="dialog"
            aria-modal="true"
            aria-labelledby="content-access-title"
            aria-describedby="content-access-detail"
            className={dialogStyles.dialog}
            onKeyDown={(event) => {
              if (event.key === 'Escape' && !write.isPending) {
                event.preventDefault();
                handleClose();
              }
              if (event.key === 'Tab') {
                const fields = Array.from(
                  dialog.current?.querySelectorAll<HTMLInputElement | HTMLButtonElement>(
                    'input, button',
                  ) ?? [],
                ).filter((field) => !field.disabled);
                const first = fields[0];
                const last = fields.at(-1);
                if (event.shiftKey && document.activeElement === first) {
                  event.preventDefault();
                  last?.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                  event.preventDefault();
                  first?.focus();
                }
              }
            }}
          >
            <h2 id="content-access-title">Confirm temporary content access</h2>
            <p id="content-access-detail">
              The organization can see who requested access, the reason reference, and the start,
              expiry, and end times.
            </p>
            <div className={contentStyles.fields}>
              <label>
                Reason reference
                <input
                  ref={reasonInput}
                  type="text"
                  value={reason}
                  maxLength={128}
                  required
                  aria-describedby="content-access-reason-help"
                  disabled={write.isPending}
                  onChange={(event) => setReason(event.currentTarget.value)}
                />
              </label>
              <p id="content-access-reason-help" className={styles.notice}>
                Use a ticket or incident reference (2–128 characters). Letters, digits, and . _ : /
                # - are allowed; spaces and tenant content are not.
              </p>
              <label>
                Duration in minutes
                <input
                  type="number"
                  value={duration}
                  min={1}
                  max={60}
                  step={1}
                  required
                  disabled={write.isPending}
                  onChange={(event) => setDuration(event.currentTarget.value)}
                />
              </label>
            </div>
            <div className={styles.actions}>
              <button onClick={handleClose} disabled={write.isPending}>
                Cancel
              </button>
              <button
                onClick={handleConfirm}
                disabled={!validReason || !validDuration || write.isPending || unresolved}
              >
                Confirm content access
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}

export function PlatformContentAccess() {
  const { session } = useAuth();
  const user = useUserKey();
  return (
    <>
      {session?.user.role === 'PLATFORM_ADMIN' && (
        <Control key={JSON.stringify([user, session.token])} />
      )}
    </>
  );
}
