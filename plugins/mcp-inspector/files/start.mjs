// Opens the MCP Inspector on the lab: a catalog naming the lab's MCP servers, each with its own audience-bound dev token,
// written before the Inspector starts. The servers are every installed MCP plugin's,
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
        return manifest?.kind === 'mcp' && url ? [[serverJson.title ?? serverJson.name ?? manifest.name, { url, audience: manifest.name }]] : [];
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

async function devToken(audience) {
  const reply = await fetch(`${lab}/dev/token`, {
    // Bounds both the request and response-body read so queued catalog refreshes can proceed.
    signal: AbortSignal.timeout(10_000),
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...persona, audience }),
  });
  if (!reply.ok) throw new Error(`dev token: HTTP ${reply.status}`);
  const token = (await reply.json())?.token;
  if (typeof token !== 'string' || !token.trim()) throw new Error('dev token: missing or invalid token');
  return token;
}

let written = '';

async function writeCatalog() {
  const list = servers();
  const mcpServers = Object.fromEntries(await Promise.all(
    Object.entries(list).map(async ([name, { url, audience }]) => {
      let headers = {};
      try { headers = { Authorization: `Bearer ${await devToken(audience)}` }; }
      catch { console.error(`maf-lab: no token for ${name}; server listed without credentials`); }
      return [name, { type: 'streamable-http', url, protocolEra: 'auto', headers }];
    }),
  ));
  mkdirSync(dirname(catalog), { recursive: true });
  writeFileSync(`${catalog}.tmp`, JSON.stringify({ mcpServers }, null, 2));
  renameSync(`${catalog}.tmp`, catalog);
  written = JSON.stringify(list);
}

// Serialize refreshes so a slow token request cannot let an older catalog overwrite a newer one.
let refresh = Promise.resolve();
function refreshServers(force = false) {
  const next = refresh.then(async () => {
    if (force || JSON.stringify(servers()) !== written) await writeCatalog();
  });
  // A failed write must not prevent subsequent refreshes from retrying.
  refresh = next.catch(() => {});
  return next;
}

await writeCatalog();
setInterval(() => refreshServers(true).catch((error) => console.error(`maf-lab: ${error.message}`)), 60 * 60 * 1000).unref();
setInterval(() => refreshServers().catch((error) => console.error(`maf-lab: ${error.message}`)), 30 * 1000).unref();
const inspector = spawn('mcp-inspector', ['--web', '--catalog', catalog], { stdio: 'inherit' });
inspector.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => inspector.kill(signal));
