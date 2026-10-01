/**
 * What a run is doing, as its open step says (agui-protocol-only), in words for the progress line. A step this screen
 * does not know is shown as the agent named it, so any agent's steps read here.
 */
export function stepLabel(step: string | undefined): string {
  if (!step) return 'Thinking…';
  const tool = /^tool: (.+)$/.exec(step);
  if (tool) return `Calling ${tool[1]}…`;
  return `${step.charAt(0).toUpperCase()}${step.slice(1)}…`;
}
