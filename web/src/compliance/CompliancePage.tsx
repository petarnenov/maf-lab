import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { ActionPage, AuditAction, ChainReport, ExportManifest } from '../api/types';
import { useApi, useAuth } from '../auth/useAuth';
import page from '../components/Page.module.css';
import { formatDate } from '../evals/format';
import styles from './Compliance.module.css';

// Every kind the record can hold, so nothing written is invisible to whoever is looking.
const KINDS = [
  'tool',
  'conversation.delete',
  'compliance.export',
  'a2a.request',
  'a2a.consultation',
  'fee.adjustment',
] as const;
const PAGE_SIZE = 25;

export function CompliancePage() {
  const api = useApi();
  const { session } = useAuth();

  return (
    <section className={page.page}>
      <h1 className={page.heading}>Compliance</h1>
      <p className={page.subheading}>
        Every audited action of {session?.user.firmId} — tool calls, deletions and exports — in one
        chained record.
      </p>
      <ChainPanel />
      <ActionLog />
      <ExportPanel api={api} />
    </section>
  );
}

function ChainPanel() {
  const api = useApi();
  const { session } = useAuth();
  const chain = useQuery({
    queryKey: ['compliance', 'chain', session?.token],
    queryFn: () => api<ChainReport>('/api/admin/compliance/verify'),
    enabled: !!session,
  });

  return (
    <section className={styles.panel} aria-label="Audit chain">
      <h2 className={styles.panelHeading}>Audit chain</h2>
      {chain.isLoading && <p className={page.muted}>Checking…</p>}
      {chain.isError && (
        <p className={page.error} role="alert">
          Could not check the chain.
        </p>
      )}
      {chain.data && (
        <>
          {/* Stated in words, not only in colour: an admin must not have to read a green dot. */}
          <p className={chain.data.intact ? styles.ok : styles.broken} role="status">
            <strong>{chain.data.intact ? '✔ The chain is intact' : '✕ The chain is broken'}</strong>{' '}
            — {chain.data.checked} record{chain.data.checked === 1 ? '' : 's'} checked
            {chain.data.unchained > 0 && `, ${chain.data.unchained} predating the chain`}.
          </p>
          {chain.data.intact ? (
            <p className={page.muted}>
              Head: <code className={styles.digest}>{chain.data.head ?? '—'}</code>
            </p>
          ) : (
            <p className={styles.brokenDetail}>
              First broken record: <strong>#{chain.data.firstBrokenId}</strong>
              {chain.data.reason && <> — {chain.data.reason}</>}. Records written before it are
              unaffected and remain evidence.
            </p>
          )}
          {chain.data.from && (
            <p className={page.muted}>
              Covering {formatDate(chain.data.from)} – {formatDate(chain.data.to!)}
            </p>
          )}
          <p className={page.muted}>
            A chain detects tampering; it does not prevent it. Anyone able to write the database can
            recompute it — keeping exports is what fixes a point in time.
          </p>
        </>
      )}
    </section>
  );
}

function ActionLog() {
  const api = useApi();
  const { session } = useAuth();
  const [userId, setUserId] = useState('');
  const [kind, setKind] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  /**
   * Older pages, kept per filter. A plain cursor in state breaks as soon as a filter is revisited: the query is
   * served from cache, the query function never runs, and the cursor stays at the previous filter's value.
   */
  const [olderByQuery, setOlderByQuery] = useState<
    Record<string, { rows: AuditAction[]; cursor: number | null }>
  >({});
  const [loadingOlder, setLoadingOlder] = useState(false);

  const filters = new URLSearchParams({ limit: String(PAGE_SIZE) });
  if (userId.trim()) filters.set('userId', userId.trim());
  if (kind) filters.set('kind', kind);
  if (from) filters.set('from', new Date(from).toISOString());
  if (to) filters.set('to', new Date(to).toISOString());
  const query = filters.toString();

  const first = useQuery({
    queryKey: ['compliance', 'actions', query, session?.token],
    queryFn: () => api<ActionPage>(`/api/admin/compliance/actions?${query}`),
    enabled: !!session,
  });

  const loaded = olderByQuery[query];
  const actions = [...(first.data?.actions ?? []), ...(loaded?.rows ?? [])];
  const cursor = loaded ? loaded.cursor : (first.data?.nextCursor ?? null);

  async function loadOlder() {
    if (cursor === null) return;
    setLoadingOlder(true);
    try {
      const next = await api<ActionPage>(`/api/admin/compliance/actions?${query}&before=${cursor}`);
      setOlderByQuery((pages) => ({
        ...pages,
        [query]: {
          rows: [...(pages[query]?.rows ?? []), ...next.actions],
          cursor: next.nextCursor,
        },
      }));
    } finally {
      setLoadingOlder(false);
    }
  }

  return (
    <section className={styles.panel} aria-label="Action log">
      <h2 className={styles.panelHeading}>Action log</h2>
      <div className={styles.filters}>
        <label>
          Person
          <input
            type="search"
            aria-label="Person"
            placeholder="all"
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
          />
        </label>
        <label>
          Kind
          <select aria-label="Kind" value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="">all</option>
            {KINDS.map((k) => (
              <option key={k} value={k}>
                {k}
              </option>
            ))}
          </select>
        </label>
        <label>
          From
          <input
            type="date"
            aria-label="From"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
        </label>
        <label>
          To
          <input type="date" aria-label="To" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
      </div>

      {first.isLoading && <p className={page.muted}>Loading…</p>}
      {first.isError && (
        <p className={page.error} role="alert">
          Could not load the record.
        </p>
      )}
      {first.data && actions.length === 0 && (
        <p className={page.notice}>No actions were recorded for these filters.</p>
      )}
      {actions.length > 0 && (
        <table className={page.table} aria-label="Actions">
          <thead>
            <tr>
              <th>Time</th>
              <th>Person</th>
              <th>Kind</th>
              <th>Action</th>
              <th>Identifiers</th>
              <th>Outcome</th>
              <th className={styles.num}>ms</th>
            </tr>
          </thead>
          <tbody>
            {actions.map((a) => (
              <tr key={a.id}>
                <td>{formatDate(a.at)}</td>
                <td>{a.principalId}</td>
                <td>{a.kind ?? <span className={page.muted}>before kinds</span>}</td>
                <td className={page.mono}>{a.action}</td>
                <td className={page.mono}>
                  {/* An empty cell would read as missing data; free text is withheld on purpose. */}
                  {a.arguments || <span className={page.muted}>none recorded</span>}
                </td>
                <td>{a.outcome}</td>
                <td className={styles.num}>{a.durationMs}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {cursor !== null && (
        <button type="button" onClick={loadOlder} disabled={loadingOlder}>
          {loadingOlder ? 'Loading…' : 'Load older'}
        </button>
      )}
    </section>
  );
}

function ExportPanel({ api }: { api: ReturnType<typeof useApi> }) {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [userId, setUserId] = useState('');
  const [manifest, setManifest] = useState<ExportManifest | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function exportPackage() {
    setBusy(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      if (from) params.set('from', new Date(from).toISOString());
      if (to) params.set('to', new Date(to).toISOString());
      if (userId.trim()) params.set('userId', userId.trim());
      const query = params.toString();
      const pkg = await api<{ manifest: ExportManifest }>(
        `/api/admin/compliance/export${query ? `?${query}` : ''}`,
      );
      download(
        pkg,
        `maf-lab-compliance-${pkg.manifest.firmId}-${pkg.manifest.generatedAt.slice(0, 10)}.json`,
      );
      setManifest(pkg.manifest);
    } catch {
      setError('Could not produce the export.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className={styles.panel} aria-label="Export">
      <h2 className={styles.panelHeading}>Export</h2>
      <div className={styles.filters}>
        <label>
          From
          <input
            type="date"
            aria-label="Export from"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
        </label>
        <label>
          To
          <input
            type="date"
            aria-label="Export to"
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
        </label>
        <label>
          Subject
          <input
            type="search"
            aria-label="Subject"
            placeholder="whole firm"
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
          />
        </label>
        <button type="button" onClick={exportPackage} disabled={busy}>
          {busy ? 'Producing…' : 'Download package'}
        </button>
      </div>
      {error && (
        <p className={page.error} role="alert">
          {error}
        </p>
      )}
      {manifest && (
        <div className={styles.manifest} role="status">
          <p>
            Downloaded. Produced by <strong>{manifest.by}</strong> at{' '}
            {formatDate(manifest.generatedAt)} for {formatDate(manifest.from)} –{' '}
            {formatDate(manifest.to)}
            {manifest.subjectUserId && <> · subject {manifest.subjectUserId}</>}.
          </p>
          <dl className={styles.facts}>
            {Object.entries(manifest.counts).map(([key, value]) => (
              <div key={key} className={styles.fact}>
                <dt>{key}</dt>
                <dd>{value}</dd>
              </div>
            ))}
            <div className={styles.fact}>
              <dt>sha256</dt>
              <dd className={styles.digest}>{manifest.sha256}</dd>
            </div>
            <div className={styles.fact}>
              <dt>chain head</dt>
              <dd className={styles.digest}>{manifest.auditChainHead ?? '—'}</dd>
            </div>
          </dl>
        </div>
      )}
    </section>
  );
}

/** The package is the file: what is shown on screen and what is handed over must be the same bytes. */
function download(content: unknown, filename: string) {
  const blob = new Blob([JSON.stringify(content, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}
