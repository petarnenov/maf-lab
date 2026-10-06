import { Fragment, useEffect, useRef, useState } from 'react';
import type { AguiFrame, TraceEvent } from './types';
import { JsonView } from './JsonView';
import styles from './MonitorPanel.module.css';
import { kindColor } from './kindColors';
import {
  byKind,
  dataOf,
  firstOf,
  formatMs,
  totalDuration,
  type AuditData,
  type Candidate,
  type EnvelopeData,
  type GraphData,
  type HistoryData,
  type ModelRequestData,
  type ModelResponseData,
  type PromptData,
  type RelevanceData,
  type RetrievalData,
  type ToolCallData,
  type ToolResultData,
  type TraceMessage,
} from './traceData';

const text = (value: unknown) =>
  typeof value === 'string' ? value : JSON.stringify(value, null, 2);

// ---- Timeline -----------------------------------------------------------------------------------------------------

export function TimelineTab({
  events,
  cursor = events.length,
  onSeek,
}: {
  events: TraceEvent[];
  /** Steps shown so far; rows after it are dimmed, the row at it is highlighted. */
  cursor?: number;
  onSeek?: (step: number) => void;
}) {
  const [open, setOpen] = useState<number | null>(null);
  const currentRef = useRef<HTMLDivElement>(null);
  const total = Math.max(totalDuration(events), 1);
  useEffect(() => {
    currentRef.current?.scrollIntoView?.({ block: 'nearest' });
  }, [cursor]);
  return (
    <div role="list" aria-label="Timeline">
      {events.map((e, index) => {
        const future = index >= cursor;
        const isCurrent = index === cursor - 1;
        const left = Math.min((e.atMs / total) * 100, 100);
        const width = e.durationMs ? Math.max((e.durationMs / total) * 100, 0.5) : 0.5;
        const isOpen = open === e.seq;
        return (
          <div
            key={e.seq}
            ref={isCurrent ? currentRef : undefined}
            role="listitem"
            className={`${styles.row} ${future ? styles.future : ''} ${isCurrent ? styles.current : ''}`}
            data-kind={e.kind}
            data-future={future}
            aria-current={isCurrent ? 'step' : undefined}
            onClick={() => {
              setOpen(isOpen ? null : e.seq);
              onSeek?.(index + 1);
            }}
          >
            <span className={styles.seq}>#{e.seq}</span>
            <span className={styles.at}>+{formatMs(e.atMs)}</span>
            <span className={styles.kind} style={{ background: kindColor(e.kind) }} title={e.kind}>
              {e.kind}
            </span>
            <span className={styles.track}>
              <span
                className={styles.bar}
                style={{
                  left: `${left}%`,
                  width: `${Math.min(width, 100 - left)}%`,
                  background: kindColor(e.kind),
                }}
              />
              <span className={styles.rowTitle}>
                {e.title}
                {e.durationMs ? ` · ${formatMs(e.durationMs)}` : ''}
              </span>
            </span>
            {isOpen && (
              <div className={styles.details} onClick={(ev) => ev.stopPropagation()}>
                {e.truncated && <div className={styles.truncated}>Truncated by the size cap.</div>}
                <JsonView value={e.data} expandDepth={2} />
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

// ---- Model calls --------------------------------------------------------------------------------------------------

export function ModelTab({ events }: { events: TraceEvent[] }) {
  const requests = byKind(events, 'model.request');
  const responses = byKind(events, 'model.response');
  const forced = byKind(events, 'tool.forced');
  if (requests.length === 0 && responses.length === 0 && forced.length === 0) {
    return <p className={styles.empty}>No model calls yet.</p>;
  }
  const iterations = new Set<number>();
  for (const e of [...requests, ...responses])
    iterations.add(dataOf<{ iteration?: number }>(e).iteration ?? 0);

  return (
    <div>
      {forced.map((e) => {
        const d = dataOf<ToolCallData>(e);
        return (
          <section key={e.seq} className={styles.card} aria-label="Forced tool call">
            <h3 className={styles.cardTitle}>
              Forced call (no model) <span className={styles.chip}>{d.tool}</span>
            </h3>
            <div className={styles.sub}>Reason</div>
            <div>{d.reason}</div>
            <pre className={styles.pre}>{text(d.arguments)}</pre>
          </section>
        );
      })}
      {[...iterations]
        .sort((a, b) => a - b)
        .map((iteration) => {
          const req = dataOf<ModelRequestData>(
            requests.find((e) => dataOf<ModelRequestData>(e).iteration === iteration),
          );
          const res = dataOf<ModelResponseData>(
            responses.find((e) => dataOf<ModelResponseData>(e).iteration === iteration),
          );
          const usage = res.usage;
          return (
            <section key={iteration} className={styles.card} aria-label={`Model call ${iteration}`}>
              <h3 className={styles.cardTitle}>
                Model call #{iteration}
                <span className={styles.chip}>{req.model ?? res.model}</span>
                {req.endpoint && <span className={styles.chip}>{req.endpoint}</span>}
                <span className={styles.chip}>tool mode: {req.toolMode ?? 'n/a'}</span>
                <span className={styles.chip}>latency {formatMs(res.latencyMs)}</span>
                <span className={styles.chip}>
                  tokens{' '}
                  {usage
                    ? `${usage.inputTokens ?? 'n/a'} in / ${usage.outputTokens ?? 'n/a'} out`
                    : 'n/a'}
                </span>
              </h3>
              {req.tools && (
                <div>
                  Tools offered: <span className={styles.mono}>{req.tools.join(', ')}</span>
                </div>
              )}
              <div className={styles.sub}>Request messages ({req.messages?.length ?? 0})</div>
              {(req.messages ?? []).map((m, i) => (
                <MessageView key={i} message={m} />
              ))}
              <div className={styles.sub}>Response</div>
              {responses.every((e) => dataOf<ModelResponseData>(e).iteration !== iteration) ? (
                <div className={styles.waiting}>waiting for response…</div>
              ) : res.text ? (
                <div className={styles.message}>{res.text}</div>
              ) : (
                <div className={styles.summary}>—</div>
              )}
              {(res.toolCalls ?? []).map((c) => (
                <div key={c.callId}>
                  → calls <span className={styles.mono}>{c.name}</span>
                  <pre className={styles.pre}>{text(c.arguments)}</pre>
                </div>
              ))}
              {res.finishReason && <div>finish: {res.finishReason}</div>}
            </section>
          );
        })}
    </div>
  );
}

function MessageView({ message }: { message: TraceMessage }) {
  return (
    <div
      className={`${styles.message} ${styles[`role-${message.role}`] ?? ''}`}
      data-role={message.role}
    >
      <span className={styles.roleName}>{message.role}</span>
      {message.contents.map((c, i) => {
        if (c.type === 'text') return <div key={i}>{c.text}</div>;
        if (c.type === 'functionCall')
          return (
            <div key={i}>
              call <span className={styles.mono}>{c.name}</span>{' '}
              <span className={styles.mono}>{text(c.arguments)}</span>
            </div>
          );
        if (c.type === 'functionResult')
          return (
            <details key={i}>
              <summary>function result ({c.callId})</summary>
              <pre className={styles.pre}>{text(c.result)}</pre>
            </details>
          );
        return <JsonView key={i} value={c} />;
      })}
    </div>
  );
}

// ---- Retrieval ----------------------------------------------------------------------------------------------------

export function RetrievalTab({ events }: { events: TraceEvent[] }) {
  const searches = byKind(events, 'retrieval');
  const judgments = byKind(events, 'relevance').map((e) => dataOf<RelevanceData>(e));
  const judgmentOf = (callId?: string) =>
    callId === undefined ? undefined : judgments.find((j) => j.callId === callId);
  // A search judged with diagnostics off has a judgment and no retrieval picture; it is still a search Jev saw.
  const diagnosed = new Set(searches.map((e) => dataOf<RetrievalData>(e).callId));
  const judgedOnly = judgments.filter((j) => !diagnosed.has(j.callId));
  // Neo4j reads sit beside the Qdrant searches: both are what the tools read from a store.
  const graphs = byKind(events, 'graph');
  if (searches.length === 0 && judgedOnly.length === 0 && graphs.length === 0)
    return <p className={styles.empty}>No retrieval in this turn.</p>;
  return (
    <div>
      {judgedOnly.map((j, i) => (
        <section key={`judged-${j.callId ?? i}`} className={styles.card} aria-label="Search">
          <h3 className={styles.cardTitle}>
            search_documents
            <span className={styles.chip}>diagnostics not recorded</span>
          </h3>
          <JevRelevance judgment={j} />
        </section>
      ))}
      {searches.map((e) => {
        const d = dataOf<RetrievalData>(e);
        // The search's own event says whether Jev ordered the results; the diagnostics add each chunk's probability.
        const judgment: RelevanceData | undefined = d.relevance
          ? { ...d.relevance, ...judgmentOf(d.callId) }
          : judgmentOf(d.callId);
        const s = d.settings ?? {};
        const terms = d.query?.terms ?? [];
        return (
          <section key={e.seq} className={styles.card} aria-label="Search">
            <h3 className={styles.cardTitle}>
              search_documents
              {d.instance && <span className={styles.chip}>mcp: {d.instance}</span>}
            </h3>
            <div className={styles.stats}>
              <span className={styles.chip}>scope: {(d.tenantScope ?? []).join(' + ')}</span>
              <span className={styles.chip}>mode: {s.mode}</span>
              <span className={styles.chip}>fusion: {s.fusion}</span>
              <span className={styles.chip}>vector: {s.denseVector}</span>
              <span className={styles.chip}>
                limit {s.limit} · prefetch {s.prefetchLimit}
              </span>
              <span className={styles.chip}>rerank: {s.rerank ? 'on' : 'off'}</span>
              {/* The floors are per branch: dense cosine and BM25 are different scales. */}
              <span className={styles.chip}>dense floor {floorLabel(s.denseFloor)}</span>
              <span className={styles.chip}>bm25 floor {floorLabel(s.sparseFloor)}</span>
            </div>
            <div className={styles.sub}>Query</div>
            {d.query?.translated && (
              // The query was brought into the corpus language; the terms below are the ones actually searched.
              <div className={styles.muted}>
                asked: {d.query.original} → translated in {Math.round(d.query.translationMs ?? 0)}{' '}
                ms
              </div>
            )}
            <div>{d.query?.text}</div>
            {d.query?.translationNote && (
              <div className={styles.muted}>searched as written: {d.query.translationNote}</div>
            )}
            <div>
              dense: {d.query?.denseModel ?? 'n/a'} ({d.query?.denseDims ?? '?'} dims)
            </div>
            <table className={styles.table} aria-label="Query terms">
              <thead>
                <tr>
                  <th>BM25 term</th>
                  <th className={styles.num}>IDF</th>
                </tr>
              </thead>
              <tbody>
                {terms.map((t) => (
                  <tr key={t.term}>
                    <td className={styles.mono}>{t.term}</td>
                    {/* A term the corpus has never seen has no weight to show — saying so is the diagnosis. */}
                    <td className={styles.num}>
                      {t.idf === null ? (
                        <span className={styles.unweighted}>
                          {t.inVocabulary ? 'no weight' : 'not in index'}
                        </span>
                      ) : (
                        t.idf.toFixed(3)
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {terms.some((t) => !t.inVocabulary) && (
              <div className={styles.muted}>
                “not in index”: the corpus has never seen this term, so it cannot match on BM25.
              </div>
            )}
            <div className={styles.lists}>
              <RankedList title="Dense" items={d.dense} />
              <RankedList title="Sparse (BM25)" items={d.sparse} />
              <RankedList title="Fused" items={d.fused} />
            </div>
            {(d.fused ?? []).length === 0 && <NothingReturned data={d} />}
            {judgment && <JevRelevance judgment={judgment} scores={d.relevance?.scores} />}
            {d.rerank && d.rerank.length > 0 && (
              <>
                <div className={styles.sub}>Rerank order</div>
                <ol>
                  {d.rerank.map((id) => (
                    <li key={id} className={styles.mono}>
                      {id}
                    </li>
                  ))}
                </ol>
              </>
            )}
            <div className={styles.sub}>Timings</div>
            <div className={styles.stats}>
              <span className={styles.chip}>embed {formatMs(d.timings?.embedMs)}</span>
              <span className={styles.chip}>bm25 {formatMs(d.timings?.sparseEncodeMs)}</span>
              <span className={styles.chip}>qdrant {formatMs(d.timings?.qdrantMs)}</span>
              <span className={styles.chip}>rerank {formatMs(d.timings?.rerankMs)}</span>
              {judgment && (
                <span className={styles.chip}>
                  jev {formatMs(d.timings?.relevanceMs ?? judgment.durationMs)}
                </span>
              )}
            </div>
          </section>
        );
      })}
      {graphs.map((e) => (
        <GraphCard key={e.seq} data={dataOf<GraphData>(e)} />
      ))}
    </div>
  );
}

const GRAPH_OUTCOMES: Record<string, string> = {
  unavailable: 'graph store unavailable',
  cancelled: 'cancelled',
  error: 'failed',
};

/**
 * One graph tool call's reads of Neo4j: the store's counterpart of a search card, with a `neo4j` timing chip styled
 * like a search's `qdrant` one. Structure only — the call's arguments are in the MCP view.
 */
function GraphCard({ data: d }: { data: GraphData }) {
  const reads = Array.isArray(d.reads) ? d.reads : [];
  const failed = d.outcome && d.outcome !== 'ok' ? d.outcome : null;
  return (
    <section className={styles.card} aria-label="Graph reads">
      <h3 className={styles.cardTitle}>
        {d.tool ?? 'graph tool'}
        {d.instance && <span className={styles.chip}>mcp: {d.instance}</span>}
      </h3>
      <div className={styles.stats}>
        <span className={styles.chip}>scope: {(d.tenantScope ?? []).join(' + ') || 'n/a'}</span>
        <span className={styles.chip}>
          {typeof d.rows === 'number' ? `${d.rows} ${d.rows === 1 ? 'row' : 'rows'}` : 'rows n/a'}
        </span>
        {d.truncated && <span className={styles.chip}>truncated</span>}
        {failed && (
          <span className={`${styles.chip} ${styles.chipWarn}`} role="status">
            {GRAPH_OUTCOMES[failed] ?? failed}
          </span>
        )}
      </div>
      <table className={styles.table} aria-label="Neo4j reads">
        <thead>
          <tr>
            <th>Template</th>
            <th className={styles.num}>Rows / limit</th>
            <th>Truncated</th>
            <th className={styles.num}>Time</th>
            <th>Outcome</th>
          </tr>
        </thead>
        <tbody>
          {reads.map((r, i) => (
            <tr key={`${r.query ?? 'read'}-${i}`}>
              <td className={styles.mono}>{r.query ?? 'n/a'}</td>
              <td className={styles.num}>
                {r.rows ?? 'n/a'} / {r.limit ?? 'n/a'}
              </td>
              <td>{r.truncated ? 'yes' : 'no'}</td>
              <td className={styles.num}>{formatMs(r.durationMs)}</td>
              <td>
                {r.outcome ?? 'n/a'}
                {r.errorType ? ` (${r.errorType})` : ''}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className={styles.sub}>Timings</div>
      <div className={styles.stats}>
        <span className={styles.chip}>neo4j {formatMs(d.durationMs)}</span>
      </div>
    </section>
  );
}

/** What the gate did with Jev's answer, in the words the trace title uses. */
function verdictOf(j: RelevanceData): string {
  if (j.reason) return `ungated — Jev unavailable: ${j.reason}`;
  if (j.silenced) return 'silenced';
  return j.gate ? 'kept' : 'kept (gate off)';
}

/**
 * Jev's relevance judgment of one search: the model, the floor, the highest probability and what the gate did with
 * it — and, when the diagnostics carry them, each judged chunk's probability, so a silenced search shows what it
 * withheld.
 */
function JevRelevance({
  judgment: j,
  scores,
}: {
  judgment: RelevanceData;
  scores?: { chunkId: string; p: number }[] | null;
}) {
  return (
    <div role="group" aria-label="Jev relevance">
      <div className={styles.sub}>Jev relevance</div>
      <div className={styles.stats}>
        <span className={styles.chip}>model: {j.model ?? 'n/a'}</span>
        {j.gate && <span className={styles.chip}>floor {j.floor?.toFixed(2)}</span>}
        <span className={styles.chip}>
          max {j.max === null || j.max === undefined ? 'n/a' : j.max.toFixed(2)}
        </span>
        <span className={`${styles.chip} ${j.silenced || j.reason ? styles.error : styles.ok}`}>
          {verdictOf(j)}
        </span>
        {j.rerankedByJev && <span className={styles.chip}>reranked by Jev</span>}
        <span className={styles.chip}>{formatMs(j.durationMs)}</span>
      </div>
      {scores && scores.length > 0 && (
        <table className={styles.table} aria-label="Jev relevance per candidate">
          <tbody>
            {scores.map((s) => (
              <tr
                key={s.chunkId}
                className={
                  j.gate && j.floor !== undefined && s.p < j.floor ? styles.dropped : undefined
                }
              >
                <td className={styles.mono} title={s.chunkId}>
                  {shortDoc(s.chunkId)}
                </td>
                <td className={styles.num}>{s.p.toFixed(3)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}

/** "shared/procedures/missing-fee-schedule.txt" → "missing-fee-schedule.txt" (full id stays in the tooltip). */
function shortDoc(docId: string): string {
  return docId.split('/').pop() ?? docId;
}

/** Last two levels of the section path, e.g. "Section 1: Preparation › Step 1". */
function shortSection(sectionPath: string): string {
  return sectionPath.split(' > ').slice(-2).join(' › ');
}

/** A floor as the settings chip shows it. Null is a real setting: that branch keeps every candidate. */
function floorLabel(floor: number | null | undefined): string {
  return floor === null || floor === undefined ? 'off' : floor.toFixed(2);
}

/**
 * Why the search came back empty. "Found candidates, none close enough" and "found nothing at all" have
 * different causes — a floor set too high, against a corpus missing the document — and the answer looks the
 * same either way, so the difference has to be said here or it is lost.
 */
function NothingReturned({ data }: { data: RetrievalData }) {
  const found = [...(data.dense ?? []), ...(data.sparse ?? [])];
  const dropped = found.filter((c) => c.belowFloor);
  return (
    <p className={styles.empty}>
      {found.length === 0
        ? 'This search returned nothing, and found no candidates at all.'
        : `This search returned nothing: ${dropped.length} of ${found.length} candidate(s) fell below their branch floor.`}
    </p>
  );
}

function RankedList({ title, items }: { title: string; items?: Candidate[] }) {
  // The extra column only exists when there is something to say in it; these lists sit three abreast.
  const anyDropped = (items ?? []).some((c) => c.belowFloor);
  return (
    <div>
      <div className={styles.sub}>{title}</div>
      <table className={styles.table} aria-label={`${title} candidates`}>
        <tbody>
          {(items ?? []).map((c) => (
            // A dropped candidate is shown, not hidden: the near misses are what say whether the floor is wrong.
            <tr
              key={`${c.rank}-${c.chunkId}`}
              className={c.belowFloor ? styles.dropped : undefined}
            >
              <td className={styles.num}>{c.rank}</td>
              <td className={styles.candidate} title={`${c.chunkId}\n${c.sectionPath}`}>
                <span className={styles.candidateDoc}>{shortDoc(c.docId)}</span>
                <span className={styles.candidateSection}>{shortSection(c.sectionPath)}</span>
              </td>
              <td>{c.tenantId}</td>
              <td className={styles.num}>{c.score.toFixed(3)}</td>
              {anyDropped && (
                <td className={styles.num}>
                  {c.belowFloor && <span className={styles.unweighted}>below floor</span>}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ---- MCP ----------------------------------------------------------------------------------------------------------

export function McpTab({ events, apiInstance }: { events: TraceEvent[]; apiInstance?: string }) {
  const calls = byKind(events, 'tool.call');
  const unknown = byKind(events, 'tool.unknown');
  if (calls.length === 0 && unknown.length === 0)
    return <p className={styles.empty}>No tool calls in this turn.</p>;
  return (
    <div>
      {calls.map((call) => {
        const c = dataOf<ToolCallData>(call);
        const result = dataOf<ToolResultData>(
          byKind(events, 'tool.result').find((e) => dataOf<ToolResultData>(e).callId === c.callId),
        );
        const envelope = dataOf<EnvelopeData>(
          byKind(events, 'envelope').find((e) => dataOf<EnvelopeData>(e).callId === c.callId),
        );
        const audit = dataOf<AuditData>(
          byKind(events, 'audit').find((e) => dataOf<AuditData>(e).tool === c.tool),
        );
        return (
          <section key={call.seq} className={styles.card} aria-label={`Tool ${c.tool}`}>
            <h3 className={styles.cardTitle}>
              {c.tool}
              {apiInstance && <span className={styles.chip}>api: {apiInstance}</span>}
              {result.mcpInstance && <span className={styles.chip}>mcp: {result.mcpInstance}</span>}
              <span className={styles.chip}>{formatMs(result.latencyMs)}</span>
              {result.isError ? (
                <span className={`${styles.chip} ${styles.error}`}>error</span>
              ) : (
                <span className={`${styles.chip} ${styles.ok}`}>ok</span>
              )}
            </h3>
            <div className={styles.sub}>Arguments</div>
            <JsonView value={c.arguments ?? {}} expandDepth={2} />
            <div className={styles.sub}>Raw MCP result</div>
            <JsonView value={result.result ?? null} expandDepth={1} />
            {envelope.text && (
              <details>
                <summary>Envelope sent to the model</summary>
                <pre className={styles.pre}>{envelope.text}</pre>
              </details>
            )}
            {audit.outcome && (
              <div>
                audit: {audit.outcome} · {formatMs(audit.durationMs)}
              </div>
            )}
          </section>
        );
      })}
      {unknown.map((e) => (
        <section key={e.seq} className={styles.card} aria-label="Unknown tool">
          <h3 className={`${styles.cardTitle} ${styles.error}`}>
            Unknown tool refused: {dataOf<ToolCallData>(e).tool}
          </h3>
        </section>
      ))}
    </div>
  );
}

// ---- Prompt & memory ----------------------------------------------------------------------------------------------

export function PromptTab({ events }: { events: TraceEvent[] }) {
  const prompt = dataOf<PromptData>(firstOf(events, 'prompt'));
  const history = dataOf<HistoryData>(firstOf(events, 'history'));
  const memory = dataOf<{ stored?: { role: string; tokens: number }[] }>(firstOf(events, 'memory'));
  const maxTokens = Math.max(1, ...(history.included ?? []).map((m) => m.tokens));
  return (
    <div>
      <section className={styles.card} aria-label="System prompt">
        <h3 className={styles.cardTitle}>
          System prompt <span className={styles.chip}>{prompt.version ?? 'n/a'}</span>
          {prompt.toolMode && <span className={styles.chip}>tool mode: {prompt.toolMode}</span>}
        </h3>
        <pre className={styles.pre}>{prompt.systemPrompt ?? '—'}</pre>
        <div className={styles.sub}>Tools ({prompt.tools?.length ?? 0})</div>
        {(prompt.tools ?? []).map((t) => (
          <details key={t.name}>
            <summary className={styles.mono}>{t.name}</summary>
            <div>{t.description}</div>
            <JsonView value={t.inputSchema ?? {}} expandDepth={2} />
          </details>
        ))}
      </section>
      <section className={styles.card} aria-label="History window">
        <h3 className={styles.cardTitle}>
          History window
          <span className={styles.chip}>
            {history.usedTokens ?? 0} / {history.budgetTokens ?? 'n/a'} tokens
          </span>
          <span className={styles.chip}>{history.excludedCount ?? 0} older messages excluded</span>
        </h3>
        {(history.included ?? []).length === 0 && (
          <div className={styles.summary}>No earlier messages.</div>
        )}
        <table className={styles.table}>
          <tbody>
            {(history.included ?? []).map((m, i) => (
              <Fragment key={i}>
                <tr>
                  <td className={styles.roleName}>{m.role}</td>
                  <td>{m.text}</td>
                  <td className={styles.num}>{m.tokens}</td>
                </tr>
                <tr>
                  <td colSpan={3}>
                    <div
                      className={styles.tokenBar}
                      style={{ width: `${(m.tokens / maxTokens) * 100}%` }}
                    />
                  </td>
                </tr>
              </Fragment>
            ))}
          </tbody>
        </table>
      </section>
      <section className={styles.card} aria-label="Memory">
        <h3 className={styles.cardTitle}>Memory stored</h3>
        {(memory.stored ?? []).length === 0 ? (
          <div className={styles.summary}>Nothing stored yet.</div>
        ) : (
          <ul>
            {(memory.stored ?? []).map((m, i) => (
              <li key={i}>
                {m.role}: {m.tokens} tokens
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

// ---- AG-UI ---------------------------------------------------------------------------------------------------------

/**
 * Every event of the run as it crossed the wire, one row each — the types the rest of the screen maps to nothing
 * included. `reached` is how far the time-travel cursor has got: the frame at it is the current one, later frames
 * are dimmed.
 */
export function AguiTab({
  frames,
  reached = frames.length > 0 ? frames[frames.length - 1].seq : 0,
  recorded = true,
}: {
  frames: AguiFrame[];
  /** Sequence number of the last frame the cursor has reached; 0 means none. */
  reached?: number;
  /** False when this turn ran before frames were kept, so "none" means "not recorded", not "an empty run". */
  recorded?: boolean;
}) {
  const [open, setOpen] = useState<number | null>(null);
  if (frames.length === 0) {
    return (
      <p className={styles.empty}>
        {recorded
          ? 'No frames yet.'
          : 'The AG-UI frames of this turn were not recorded. Only turns answered since they were kept have them.'}
      </p>
    );
  }
  return (
    <div role="list" aria-label="AG-UI frames">
      {frames.map((f) => {
        const future = f.seq > reached;
        const isCurrent = f.seq === reached;
        const isOpen = open === f.seq;
        return (
          <div
            key={f.seq}
            role="listitem"
            className={`${styles.row} ${styles.frameRow} ${future ? styles.future : ''} ${
              isCurrent ? styles.current : ''
            }`}
            data-type={f.type}
            data-future={future}
            aria-current={isCurrent ? 'step' : undefined}
            onClick={() => setOpen(isOpen ? null : f.seq)}
          >
            <span className={styles.seq}>#{f.seq}</span>
            <span className={styles.at}>+{formatMs(f.atMs)}</span>
            <span className={styles.kind} style={{ background: frameColor(f) }} title={f.type}>
              {f.type}
            </span>
            <span className={styles.frameName}>
              {f.name}
              {f.traceSeq !== undefined && (
                <span className={styles.muted}> → trace #{f.traceSeq}</span>
              )}
              {f.unparsed !== undefined && (
                <span className={styles.frameBad}> unreadable payload</span>
              )}
              {f.truncated && <span className={styles.frameBad}> payload dropped by the cap</span>}
            </span>
            <span className={styles.frameBytes}>{f.bytes} B</span>
            {isOpen && (
              <div className={styles.details} onClick={(ev) => ev.stopPropagation()}>
                {f.unparsed !== undefined ? (
                  <pre className={styles.pre}>{f.unparsed}</pre>
                ) : f.truncated ? (
                  <div className={styles.truncated}>Truncated by the size cap.</div>
                ) : f.traceSeq !== undefined ? (
                  <div className={styles.summary}>
                    Carried trace event #{f.traceSeq} — the other tabs show it.
                  </div>
                ) : f.payload === undefined || f.payload === null ? (
                  <div className={styles.summary}>No payload was kept for this frame.</div>
                ) : (
                  <JsonView value={f.payload} expandDepth={2} />
                )}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** The protocol's families, coloured the way the timeline colours trace kinds. */
function frameColor(frame: AguiFrame): string {
  if (frame.unparsed !== undefined) return 'var(--kind-danger)';
  if (frame.type.startsWith('RUN_')) return 'var(--kind-neutral)';
  if (frame.type.startsWith('TEXT_MESSAGE')) return 'var(--kind-model)';
  if (frame.type.startsWith('TOOL_CALL')) return 'var(--kind-tool)';
  if (frame.type.startsWith('STEP_')) return 'var(--kind-context)';
  return 'var(--kind-other)';
}
