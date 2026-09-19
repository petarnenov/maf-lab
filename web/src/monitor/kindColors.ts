/** Colour per trace kind family, used by the timeline. */
export function kindColor(kind: string): string {
  if (kind.startsWith('turn.')) return '#5d6673';
  if (kind.startsWith('model.')) return '#2f5bd3';
  if (kind === 'tool.unknown') return '#b42318';
  if (kind.startsWith('tool.') || kind === 'envelope') return '#8e44ad';
  if (kind === 'retrieval') return '#1d7a3c';
  if (kind === 'intent' || kind === 'prompt' || kind === 'history' || kind === 'memory')
    return '#8a6d3b';
  return '#0e7490';
}
