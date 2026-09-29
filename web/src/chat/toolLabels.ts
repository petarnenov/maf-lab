import type { ToolCallView } from './chatReducer';

/** Human label for a tool call, e.g. "Searching documentation…" or "Checking run 4417". */
export function toolCallLabel(call: Pick<ToolCallView, 'toolName' | 'argumentSummary' | 'status'>) {
  const running = call.status === 'running';
  switch (call.toolName) {
    case 'search_documents':
      return running ? 'Searching documentation…' : 'Searched documentation';
    case 'get_billing_run_status': {
      const runId = extractRunId(call.argumentSummary);
      if (running) return runId ? `Checking run ${runId}` : 'Checking billing run…';
      return runId ? `Checked run ${runId}` : 'Checked billing run';
    }
    case 'search_billing_runs':
      return running ? 'Searching billing runs…' : 'Searched billing runs';
    case 'search_codebase':
      return running ? 'Searching the codebase…' : 'Searched the codebase';
    case 'search_portfolio_documents':
      return running ? 'Searching portfolio documentation…' : 'Searched portfolio documentation';
    case 'get_household_portfolio':
      return running ? 'Reading the portfolio…' : 'Read the portfolio';
    case 'get_aum_history':
      return running ? 'Reading quarter-end AUM…' : 'Read quarter-end AUM';
    case 'list_my_accounts':
      return running ? 'Listing your accounts…' : 'Listed your accounts';
    // The reviewer is another system and takes its time; saying so beats looking stuck for half a minute.
    case 'propose_fee_adjustment':
      return running
        ? 'Checking the adjustment with compliance (about 30s)…'
        : 'Prepared the adjustment';
    default:
      return running ? `Calling ${call.toolName}…` : `Called ${call.toolName}`;
  }
}

/** Pulls a run id out of an argument summary such as `runId=4417` or `run 4417`. */
export function extractRunId(summary: string): string | null {
  const keyed = /run_?id\s*[:=]\s*"?([A-Za-z0-9-]+)"?/i.exec(summary);
  if (keyed) return keyed[1];
  const bare = /\b(\d{3,})\b/.exec(summary);
  return bare ? bare[1] : null;
}
