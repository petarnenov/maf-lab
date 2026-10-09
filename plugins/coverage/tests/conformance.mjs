export async function registerChecks({ check, run, wellFormed, base, headers }) {
  const runs = await (await fetch(`${base}/api/coverage/runs`, { headers })).json().catch(() => []);
  const followed = Array.isArray(runs) ? runs.find((r) => !r.active) : undefined;
  const routes = {
    direct: `${base}/api/coverage/runs/agent`,
    runtime: `${base}/copilotkit/agent/testgen/run`,
  };
  for (const via of ['direct', 'runtime']) {
    check(`testgen (${via}): a finished run replays as one well-formed run`, async () => {
      if (!followed) return 'skipped: no finished test run on this stack';
      const { events } = await run(routes[via], { threadId: `testgen:${followed.id}:conformance` });
      wellFormed(events);
      if (!events.some((e) => e.type === 'STATE_SNAPSHOT')) throw new Error('no state');
    });
  }
}
