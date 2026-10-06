/** Colour per trace kind family, used by the timeline: a theme token, so it follows light and dark. */
export function kindColor(kind: string): string {
  if (kind.startsWith('turn.')) return 'var(--kind-neutral)';
  if (kind.startsWith('model.')) return 'var(--kind-model)';
  if (kind === 'tool.unknown' || kind === 'guardrail') return 'var(--kind-danger)';
  if (kind.startsWith('tool.') || kind === 'envelope') return 'var(--kind-tool)';
  if (kind === 'retrieval' || kind === 'relevance') return 'var(--kind-retrieval)';
  // A graph tool call's Neo4j reads: a store of its own beside Qdrant, so a colour of its own.
  if (kind === 'graph') return 'var(--kind-graph)';
  // Jev's check of the final answer: a judgment, like relevance, of what came out rather than what was found.
  if (kind === 'answer.check') return 'var(--kind-check)';
  // The domain boundary: where Jev put the question, and where a turn crossed from one domain into another.
  if (kind === 'domain' || kind === 'boundary') return 'var(--kind-domain)';
  if (kind === 'intent' || kind === 'prompt' || kind === 'history' || kind === 'memory')
    return 'var(--kind-context)';
  return 'var(--kind-other)';
}
