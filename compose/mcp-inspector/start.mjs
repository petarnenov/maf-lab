// Opens the MCP Inspector on the lab: a catalog naming the lab's three MCP servers, each with a dev user's bearer
// token, written before the Inspector starts, then rewritten with a fresh token every hour so it never goes stale
// (dev tokens last eight hours). The catalog lives in the container only; nothing is kept on disk across restarts.
import { spawn } from 'node:child_process';
import { mkdirSync, renameSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

const lab = process.env.LAB_URL ?? 'http://localhost:7171';
const catalog = process.env.MCP_CATALOG_PATH ?? '/home/node/.mcp-inspector/mcp.json';
const persona = {
  userId: process.env.LAB_USER_ID ?? 'adam',
  firmId: process.env.LAB_FIRM_ID ?? 'firm-a',
  role: process.env.LAB_ROLE ?? 'ADVISOR',
};
const servers = { 'maf-lab billing': '/mcp', 'maf-lab portfolio': '/portfolio/mcp', 'maf-lab code': '/code/mcp' };

async function devToken() {
  const reply = await fetch(`${lab}/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(persona),
  });
  if (!reply.ok) throw new Error(`dev token: HTTP ${reply.status}`);
  return (await reply.json()).token;
}

async function writeCatalog() {
  let headers = {};
  try {
    headers = { Authorization: `Bearer ${await devToken()}` };
  } catch (error) {
    // The servers are still listed; a token can be pasted by hand until the next refresh succeeds.
    console.error(`maf-lab: no dev token (${error.message}); servers listed without one`);
  }
  const mcpServers = Object.fromEntries(
    Object.entries(servers).map(([name, path]) => [
      name,
      { type: 'streamable-http', url: `${lab}${path}`, protocolEra: 'auto', headers },
    ]),
  );
  mkdirSync(dirname(catalog), { recursive: true });
  writeFileSync(`${catalog}.tmp`, JSON.stringify({ mcpServers }, null, 2));
  renameSync(`${catalog}.tmp`, catalog);
}

await writeCatalog();
setInterval(writeCatalog, 60 * 60 * 1000).unref();
const inspector = spawn('mcp-inspector', ['--web', '--catalog', catalog], { stdio: 'inherit' });
inspector.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => inspector.kill(signal));
