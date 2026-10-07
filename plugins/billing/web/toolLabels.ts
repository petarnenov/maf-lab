import type { PluginToolLabels } from '@maf/plugin-api';

/** How billing's tools read in the chat (extract-portfolio moved them here from the core). */
export const billingToolLabels: PluginToolLabels = {
  search_documents: ({ running }) =>
    running ? 'Searching documentation…' : 'Searched documentation',
  get_billing_run_status: ({ running, argumentSummary }) => {
    const runId = extractRunId(argumentSummary);
    if (running) return runId ? `Checking run ${runId}` : 'Checking billing run…';
    return runId ? `Checked run ${runId}` : 'Checked billing run';
  },
  search_billing_runs: ({ running }) =>
    running ? 'Searching billing runs…' : 'Searched billing runs',
  // The reviewer is another system and takes its time; saying so beats looking stuck for half a minute.
  propose_fee_adjustment: ({ running }) =>
    running ? 'Checking the adjustment with compliance (about 30s)…' : 'Prepared the adjustment',
};

/** Pulls a run id out of an argument summary such as `runId=4417` or `run 4417`. */
export function extractRunId(summary: string): string | null {
  const keyed = /run_?id\s*[:=]\s*"?([A-Za-z0-9-]+)"?/i.exec(summary);
  if (keyed) return keyed[1];
  const bare = /\b(\d{3,})\b/.exec(summary);
  return bare ? bare[1] : null;
}
