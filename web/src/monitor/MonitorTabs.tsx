import { Fragment, useState } from 'react';
import type { TraceEvent } from '../api/types';
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
  type HistoryData,
  type ModelRequestData,
  type ModelResponseData,
  type PromptData,
  type RetrievalData,
  type ToolCallData,
  type ToolResultData,
  type TraceMessage,
} from './traceData';

const text = (value: unknown) =>
  typeof value === 'string' ? value : JSON.stringify(value, null, 2);

// ---- Timeline -----------------------------------------------------------------------------------------------------

export function TimelineTab({ events }: { events: TraceEvent[] }) {
  const [open, setOpen] = useState<number | null>(null);
  const total = Math.max(totalDuration(events), 1);
  return (
    <div role="list" aria-label="Timeline">
      {events.map((e) => {
        const left = Math.min((e.atMs / total) * 100, 100);
        const width = e.durationMs ? Math.max((e.durationMs / total) * 100, 0.5) : 0.5;
        const isOpen = open === e.seq;
        return (
          <div
            key={e.seq}
            role="listitem"
            className={styles.row}
            data-kind={e.kind}
            onClick={() => setOpen(isOpen ? null : e.seq)}
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
              {res.text ? (
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
  if (searches.length === 0) return <p className={styles.empty}>No retrieval in this turn.</p>;
  return (
    <div>
      {searches.map((e) => {
        const d = dataOf<RetrievalData>(e);
        const s = d.settings ?? {};
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
            </div>
            <div className={styles.sub}>Query</div>
            <div>{d.query?.text}</div>
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
                {(d.query?.terms ?? []).map((t) => (
                  <tr key={t.term}>
                    <td className={styles.mono}>{t.term}</td>
                    <td className={styles.num}>{t.idf.toFixed(3)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <div className={styles.lists}>
              <RankedList title="Dense" items={d.dense} />
              <RankedList title="Sparse (BM25)" items={d.sparse} />
              <RankedList title="Fused" items={d.fused} />
            </div>
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
            </div>
          </section>
        );
      })}
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

function RankedList({ title, items }: { title: string; items?: Candidate[] }) {
  return (
    <div>
      <div className={styles.sub}>{title}</div>
      <table className={styles.table} aria-label={`${title} candidates`}>
        <tbody>
          {(items ?? []).map((c) => (
            <tr key={`${c.rank}-${c.chunkId}`}>
              <td className={styles.num}>{c.rank}</td>
              <td className={styles.candidate} title={`${c.chunkId}\n${c.sectionPath}`}>
                <span className={styles.candidateDoc}>{shortDoc(c.docId)}</span>
                <span className={styles.candidateSection}>{shortSection(c.sectionPath)}</span>
              </td>
              <td>{c.tenantId}</td>
              <td className={styles.num}>{c.score.toFixed(3)}</td>
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
