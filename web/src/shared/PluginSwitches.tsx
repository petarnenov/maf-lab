import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useApi, useAuth, useUserKey } from '../plugins/api';
import styles from './Page.module.css';
import dialogStyles from './PluginSwitches.module.css';

export interface AdminPlugin {
  name: string;
  description: string;
  scope: string;
  health: string;
  environments: string[];
  private: boolean;
  allowed: boolean;
  enabled: boolean;
}

/** Permission checkboxes stay controlled by the server; opening a confirmation never changes one. */
export function PluginSwitches({ platform }: { platform: boolean }) {
  const api = useApi();
  const user = useUserKey();
  const { session } = useAuth();
  const client = useQueryClient();
  const path = platform ? '/api/platform/plugins' : '/api/admin/plugins';
  const key = ['plugin-permissions', path, user, session?.token];
  const [confirm, setConfirm] = useState<AdminPlugin | null>(null);
  const restoreFocus = useRef<HTMLElement | null>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  const dialog = useRef<HTMLDivElement>(null);
  const listed = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => api<AdminPlugin[]>(path, { signal }),
  });
  const write = useMutation({
    mutationFn: ({ plugin, checked }: { plugin: string; checked: boolean }) =>
      api(`${path}/${encodeURIComponent(plugin)}`, {
        method: 'PUT',
        body: platform ? { allowed: checked } : { enabled: checked },
      }),
    onSuccess: async () => {
      setConfirm(null);
      restoreFocus.current?.focus();
      await Promise.all([
        client.invalidateQueries({ queryKey: key }),
        client.invalidateQueries({ queryKey: ['plugins'] }),
        client.invalidateQueries({ queryKey: ['admin-overview'] }),
      ]);
    },
  });
  const close = () => {
    setConfirm(null);
    restoreFocus.current?.focus();
  };
  useEffect(() => {
    if (confirm) cancel.current?.focus();
  }, [confirm]);
  return (
    <section aria-label={platform ? 'Plugin allowances' : 'Enabled plugins'}>
      <h2 className={styles.subheading}>{platform ? 'Plugin allowances' : 'Enabled plugins'}</h2>
      {listed.isPending && <p role="status">Loading plugins…</p>}
      {listed.isError && (
        <p className={styles.error} role="alert">
          {listed.error.message}
        </p>
      )}
      {write.isError && (
        <p className={styles.error} role="alert">
          {write.error.message}
        </p>
      )}
      {write.isPending && <p role="status">Saving…</p>}
      {listed.data?.length === 0 && (
        <p className={styles.notice}>No plugins are allowed for this organization.</p>
      )}
      <ul className={dialogStyles.list}>
        {listed.data?.map((plugin) => (
          <li key={plugin.name}>
            <label>
              {plugin.scope === 'tenant' && (
                <input
                  type="checkbox"
                  aria-label={plugin.name}
                  checked={platform ? plugin.allowed : plugin.enabled}
                  disabled={write.isPending}
                  onChange={(event) => {
                    write.reset();
                    if (event.currentTarget.checked)
                      write.mutate({ plugin: plugin.name, checked: true });
                    else {
                      restoreFocus.current = event.currentTarget;
                      setConfirm(plugin);
                    }
                  }}
                />
              )}
              <strong>{plugin.name}</strong>
              {plugin.private && <span className={styles.tag}>Private</span>}
            </label>
            <p className={styles.notice}>{plugin.description}</p>
            {plugin.health === 'unavailable' && (
              <p className={styles.notice}>Unavailable right now.</p>
            )}
            {platform && (
              <p className={styles.notice}>
                {plugin.scope} · {plugin.environments.join(', ')} · {plugin.health}
              </p>
            )}
          </li>
        ))}
      </ul>
      {confirm && (
        <div className={dialogStyles.backdrop}>
          <div
            ref={dialog}
            role="dialog"
            aria-modal="true"
            aria-labelledby="disable-plugin-title"
            aria-describedby="disable-plugin-detail"
            className={dialogStyles.dialog}
            onKeyDown={(event) => {
              if (event.key === 'Escape' && !write.isPending) {
                event.preventDefault();
                close();
              }
              if (event.key === 'Tab') {
                const buttons = Array.from(
                  dialog.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ??
                    [],
                );
                const first = buttons[0];
                const last = buttons.at(-1);
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
            <h2 id="disable-plugin-title">
              {platform ? 'Withdraw allowance for' : 'Disable'} {confirm.name}?
            </h2>
            <p id="disable-plugin-detail">
              The assistant stops using {confirm.name} for the whole organization from its next
              turn. {platform && 'The plugin is also switched off. '}Its data is kept.
            </p>
            <div className={styles.actions}>
              <button ref={cancel} disabled={write.isPending} onClick={close}>
                Cancel
              </button>
              <button
                disabled={write.isPending}
                onClick={() => write.mutate({ plugin: confirm.name, checked: false })}
              >
                Confirm
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
