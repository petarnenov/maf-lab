import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import type { TopologyNode, TopologyReport } from '../api/types';
import { apiText } from '../api/client';
import { useApi, useAuth } from '../auth/useAuth';
import page from '../components/Page.module.css';
import { formatDate } from '../evals/format';
import { TopologyDiagram } from './TopologyDiagram';
import { HEALTH_MARK } from './health';
import { parseDiagram, type Diagram } from './parseDiagram';
import styles from './Topology.module.css';

const REFRESH_MS = 5000;

export function TopologyPage() {
  const api = useApi();
  const { session } = useAuth();
  const [selected, setSelected] = useState<string | null>(null);

  // The drawing changes only when someone edits it, so it is fetched once and kept.
  const diagram = useQuery({
    queryKey: ['topology', 'diagram'],
    queryFn: async () =>
      parseDiagram(await apiText(session?.token ?? null, '/api/topology/diagram')),
    enabled: !!session,
    staleTime: Infinity,
    retry: false,
  });

  const state = useQuery({
    queryKey: ['topology', 'state', session?.token],
    queryFn: () => api<TopologyReport>('/api/topology'),
    enabled: !!session,
    refetchInterval: REFRESH_MS,
  });

  const byId = useMemo(
    () => new Map((state.data?.nodes ?? []).map((node) => [node.id, node])),
    [state.data],
  );

  if (!session) {
    return (
      <section className={page.page}>
        <h1 className={page.heading}>Topology</h1>
        <p className={page.notice}>Pick a dev persona in the header to continue.</p>
      </section>
    );
  }

  return (
    <section className={page.page}>
      <h1 className={page.heading}>Topology</h1>
      <p className={page.subheading}>
        The stack as drawn in <code>docs/topology.drawio</code>, with the state of every service on
        top.
      </p>

      <div className={styles.toolbar}>
        <button type="button" onClick={() => state.refetch()} disabled={state.isFetching}>
          {state.isFetching ? 'Refreshing…' : 'Refresh'}
        </button>
        <span className={page.muted}>
          {state.data
            ? `State from ${formatDate(state.data.generatedAt)} · reported by ${state.data.reportedBy} · refreshes every ${REFRESH_MS / 1000}s`
            : 'State not loaded yet'}
        </span>
        {state.data && !state.data.discoveryAvailable && (
          <span className={page.tag}>replica discovery unavailable</span>
        )}
      </div>

      {state.isError && (
        <p className={page.error} role="alert">
          Could not load the live state — the diagram below is shown without it.
        </p>
      )}
      {diagram.isError && (
        <p className={page.error} role="alert">
          Could not load the topology diagram: {(diagram.error as Error).message}
        </p>
      )}

      <div className={styles.layout}>
        <div className={styles.canvasWrap}>
          {diagram.isLoading && <p className={page.muted}>Loading the diagram…</p>}
          {diagram.data && (
            <TopologyDiagram
              diagram={diagram.data as Diagram}
              state={byId}
              selected={selected}
              onSelect={setSelected}
            />
          )}
          <div className={styles.legend}>
            {Object.values(HEALTH_MARK).map((mark) => (
              <span key={mark.label}>
                {mark.symbol} {mark.label}
              </span>
            ))}
          </div>
        </div>

        <NodeDetail node={selected ? byId.get(selected) : undefined} selected={selected} />
      </div>
    </section>
  );
}

function NodeDetail({
  node,
  selected,
}: {
  node: TopologyNode | undefined;
  selected: string | null;
}) {
  if (!node) {
    return (
      <aside className={styles.side} aria-label="Node detail">
        <h2 className={styles.sideHeading}>Node detail</h2>
        <p className={page.muted}>
          {selected ? 'No live state for this node yet.' : 'Select a box in the diagram.'}
        </p>
      </aside>
    );
  }
  const mark = HEALTH_MARK[node.health];
  return (
    <aside className={styles.side} aria-label="Node detail">
      <h2 className={styles.sideHeading}>{node.name}</h2>
      <p>
        {mark.symbol} {mark.label}
        {node.latencyMs != null && (
          <span className={page.muted}> · {Math.round(node.latencyMs)} ms</span>
        )}
      </p>
      {node.reason && (
        // A reason is an explanation, not necessarily a fault: "not probed" is how the paid chat endpoint is meant
        // to look, so only a failing state is coloured as one.
        <p className={`${styles.reason} ${mark.className}`}>{node.reason}</p>
      )}

      <div className={styles.facts}>
        {Object.entries(node.facts).map(([key, value]) => (
          <div key={key} style={{ display: 'contents' }}>
            <span className={styles.factKey}>{key}</span>
            <span className={styles.factValue}>{value}</span>
          </div>
        ))}
      </div>

      {node.instances.length > 0 && (
        <>
          <h3 className={styles.sideHeading}>Instances</h3>
          <ul className={styles.instances}>
            {node.instances.map((instance) => (
              <li key={instance.name} className={styles.instance}>
                <span>{HEALTH_MARK[instance.health].symbol}</span>
                <span>{instance.name}</span>
                {instance.address && (
                  <span className={styles.instanceAddress}>{instance.address}</span>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </aside>
  );
}
