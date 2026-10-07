// Opens the MCP Inspector on the lab: a catalog naming the lab's MCP servers, each with a dev user's bearer token,
// written before the Inspector starts. The servers are the built-in domains' (below) and every installed MCP plugin's,
// read from plugins/.installed — which embeds each plugin's MCP Registry server.json — every 30 s, as the api re-reads
// it; the catalog is rewritten only when that list changed, and with a fresh token every hour so it never goes stale
// (dev tokens last eight hours). The catalog lives in the container only; nothing is kept on disk across restarts.
import { spawn } from 'node:child_process';
import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

const lab = process.env.LAB_URL ?? 'http://localhost:7171';
const catalog = process.env.MCP_CATALOG_PATH ?? '/home/node/.mcp-inspector/mcp.json';
const persona = {
  userId: process.env.LAB_USER_ID ?? 'adam',
  tenantId: process.env.LAB_TENANT_ID ?? 'firm-a',
  role: process.env.LAB_ROLE ?? 'USER',
};
const installed = process.env.MAF_INSTALLED_PATH ?? '/plugins/.installed';


/** The installed MCP plugins' servers, by name: the first remote of each plugin's server.json. */
function installedServers() {
  try {
    const document = JSON.parse(readFileSync(installed, 'utf8'));
    return Object.fromEntries(
      (document.plugins ?? []).flatMap(({ manifest, serverJson }) => {
        const url = serverJson?.remotes?.find((r) => r?.url)?.url;
        return manifest?.kind === 'mcp' && url ? [[serverJson.title ?? serverJson.name ?? manifest.name, url]] : [];
      }),
    );
  } catch (error) {
    // No installed set yet (or a half-written one): no server is listed until the next read retries.
    if (error.code !== 'ENOENT') console.error(`maf-lab: cannot read ${installed} (${error.message})`);
    return {};
  }
}

function servers() {
  return installedServers();
}

async function devToken() {
  const reply = await fetch(`${lab}/dev/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(persona),
  });
  if (!reply.ok) throw new Error(`dev token: HTTP ${reply.status}`);
  return (await reply.json()).token;
}

let written = '';

async function writeCatalog() {
  let headers = {};
  try {
    headers = { Authorization: `Bearer ${await devToken()}` };
  } catch (error) {
    // The servers are still listed; a token can be pasted by hand until the next refresh succeeds.
    console.error(`maf-lab: no dev token (${error.message}); servers listed without one`);
  }
  const list = servers();
  const mcpServers = Object.fromEntries(
    Object.entries(list).map(([name, url]) => [
      name,
      { type: 'streamable-http', url, protocolEra: 'auto', headers },
    ]),
  );
  mkdirSync(dirname(catalog), { recursive: true });
  writeFileSync(`${catalog}.tmp`, JSON.stringify({ mcpServers }, null, 2));
  renameSync(`${catalog}.tmp`, catalog);
  written = JSON.stringify(list);
}

/** Rewrites the catalog when the installed MCP servers changed since it was written. */
async function refreshServers() {
  if (JSON.stringify(servers()) !== written) await writeCatalog();
}

await writeCatalog();
setInterval(writeCatalog, 60 * 60 * 1000).unref();
setInterval(() => refreshServers().catch((error) => console.error(`maf-lab: ${error.message}`)), 30 * 1000).unref();
const inspector = spawn('mcp-inspector', ['--web', '--catalog', catalog], { stdio: 'inherit' });
inspector.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => inspector.kill(signal));
