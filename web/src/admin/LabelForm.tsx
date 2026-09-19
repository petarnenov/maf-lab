import { useState, type FormEvent } from 'react';
import type { LabelDataset, LabelRequest, ReviewQueueItem } from '../api/types';
import styles from '../components/Page.module.css';
import { TOOL_NAMES } from './toolNames';

const splitLines = (value: string) =>
  value
    .split(/[\n,]/)
    .map((s) => s.trim())
    .filter(Boolean);

function defaultDataset(item: ReviewQueueItem): LabelDataset {
  if (item.feedbackKinds.includes('wrong_tool') || item.signals.includes('no_tool_on_how_why')) {
    return 'selection';
  }
  if (item.feedbackKinds.includes('wrong_answer')) return 'generation';
  return 'retrieval';
}

export function LabelForm({
  item,
  onSubmit,
  submitting,
}: {
  item: ReviewQueueItem;
  onSubmit: (label: LabelRequest) => void;
  submitting: boolean;
}) {
  const [dataset, setDataset] = useState<LabelDataset>(() => defaultDataset(item));
  const [tools, setTools] = useState<string[]>(() => [
    ...new Set(item.toolCalls.map((c) => c.toolName).filter((t) => TOOL_NAMES.includes(t))),
  ]);
  const turnChunkIds = [...new Set(item.toolCalls.flatMap((c) => c.chunkIds ?? []))];
  const [checkedChunks, setCheckedChunks] = useState<string[]>([]);
  const [extraChunkIds, setExtraChunkIds] = useState('');
  const [referenceAnswer, setReferenceAnswer] = useState('');
  const [docIds, setDocIds] = useState(() =>
    [...new Set(item.toolCalls.flatMap((c) => c.docIds))].join('\n'),
  );

  function relevantChunkIds() {
    return [...new Set([...checkedChunks, ...splitLines(extraChunkIds)])];
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const label: LabelRequest = { dataset };
    if (dataset === 'selection') label.expectedTools = tools;
    if (dataset === 'retrieval') label.relevantChunkIds = relevantChunkIds();
    if (dataset === 'generation') {
      label.referenceAnswer = referenceAnswer.trim();
      label.expectedDocIds = splitLines(docIds);
    }
    onSubmit(label);
  }

  const invalid =
    (dataset === 'retrieval' && relevantChunkIds().length === 0) ||
    (dataset === 'generation' && !referenceAnswer.trim());

  return (
    <form className={styles.form} onSubmit={submit} aria-label="Label turn">
      <div>
        <strong>Question:</strong> {item.question}
      </div>
      <label>
        Dataset
        <select value={dataset} onChange={(e) => setDataset(e.target.value as LabelDataset)}>
          <option value="selection">selection — which tools should have been called</option>
          <option value="retrieval">retrieval — which chunks are relevant</option>
          <option value="generation">generation — reference answer</option>
        </select>
      </label>

      {dataset === 'selection' && (
        <fieldset className={styles.checks}>
          <legend>Expected tools (none checked = no tool)</legend>
          {TOOL_NAMES.map((tool) => (
            <label key={tool}>
              <input
                type="checkbox"
                checked={tools.includes(tool)}
                onChange={(e) =>
                  setTools((prev) =>
                    e.target.checked ? [...prev, tool] : prev.filter((t) => t !== tool),
                  )
                }
              />
              {tool}
            </label>
          ))}
        </fieldset>
      )}

      {dataset === 'retrieval' && (
        <>
          {turnChunkIds.length > 0 ? (
            <fieldset className={styles.checks}>
              <legend>Relevant chunks returned in this turn</legend>
              {turnChunkIds.map((id) => (
                <label key={id} className={styles.mono}>
                  <input
                    type="checkbox"
                    checked={checkedChunks.includes(id)}
                    onChange={(e) =>
                      setCheckedChunks((prev) =>
                        e.target.checked ? [...prev, id] : prev.filter((c) => c !== id),
                      )
                    }
                  />
                  {id}
                </label>
              ))}
            </fieldset>
          ) : (
            <p className={styles.muted}>This turn returned no chunks.</p>
          )}
          <label>
            Other relevant chunk ids (one per line)
            <textarea
              rows={3}
              value={extraChunkIds}
              onChange={(e) => setExtraChunkIds(e.target.value)}
            />
          </label>
        </>
      )}

      {dataset === 'generation' && (
        <>
          <label>
            Reference answer
            <textarea
              rows={4}
              value={referenceAnswer}
              onChange={(e) => setReferenceAnswer(e.target.value)}
            />
          </label>
          <label>
            Expected source doc ids (one per line)
            <textarea rows={3} value={docIds} onChange={(e) => setDocIds(e.target.value)} />
          </label>
        </>
      )}

      <div>
        <button type="submit" disabled={submitting || invalid}>
          {submitting ? 'Saving…' : 'Append to dataset'}
        </button>
      </div>
    </form>
  );
}
