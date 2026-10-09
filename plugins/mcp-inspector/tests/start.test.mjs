import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import test from 'node:test';
import { createContext, SourceTextModule, SyntheticModule } from 'node:vm';

const source = await readFile(new URL('../files/start.mjs', import.meta.url), 'utf8');
const plugin = (name, url, extra = {}) => ({
  manifest: { name, kind: 'mcp' },
  serverJson: { title: `${name} server`, remotes: [{ url }] },
  ...extra,
});
const initial = [plugin('billing', 'http://billing/mcp'), plugin('portfolio', 'http://portfolio/mcp')];
const hour = 60 * 60 * 1000;
const poll = 30 * 1000;

async function start(plugins = initial) {
  const state = {
    plugins, calls: [], events: [], errors: [], timers: new Map(), files: new Map(),
    signals: new Map(), childEvents: new Map(), kills: [], exits: [], failWrite: false, deadlines: [],
    reply: async (body, index) => ({ ok: true, json: async () => ({ token: `${body.audience}-${index}` }) }),
  };
  const catalogPath = '/catalog/mcp.json';
  const context = createContext({
    AbortSignal: {
      timeout: (milliseconds) => {
        const controller = new AbortController();
        state.deadlines.push({ milliseconds, expire: () => controller.abort(new Error('token deadline exceeded')) });
        return controller.signal;
      },
    },
    process: {
      env: { LAB_URL: 'http://lab', MCP_CATALOG_PATH: catalogPath,
        MAF_INSTALLED_PATH: '/installed', LAB_USER_ID: 'eve', LAB_TENANT_ID: 'firm-b', LAB_ROLE: 'ADMIN' },
      on: (signal, handler) => state.signals.set(signal, handler),
      exit: (code) => state.exits.push(code),
    },
    console: { error: (message) => state.errors.push(message) },
    fetch: async (url, options) => {
      const body = JSON.parse(options.body);
      state.calls.push({ url, ...options, headers: { ...options.headers }, body });
      return state.reply(body, state.calls.length, options.signal);
    },
    setInterval: (handler, delay) => {
      state.timers.set(delay, handler);
      return { unref: () => state.events.push(['unref', delay]) };
    },
  });
  const boundaries = {
    'node:path': { dirname },
    'node:child_process': {
      spawn: (...args) => {
        state.events.push(['spawn', ...args]);
        return { on: (event, handler) => state.childEvents.set(event, handler),
          kill: (signal) => state.kills.push(signal) };
      },
    },
    'node:fs': {
      readFileSync: (path) => { assert.equal(path, '/installed'); return JSON.stringify({ plugins: state.plugins }); },
      mkdirSync: (path) => state.events.push(['mkdir', path]),
      writeFileSync: (path, content) => {
        if (state.failWrite) throw new Error('disk unavailable');
        state.events.push(['write', path]);
        state.files.set(path, content);
      },
      renameSync: (from, to) => {
        assert.ok(state.files.has(from));
        state.events.push(['rename', from, to]);
        state.files.set(to, state.files.get(from));
        state.files.delete(from);
      },
    },
  };
  const module = new SourceTextModule(source, { context });
  await module.link((specifier) => {
    const exports = boundaries[specifier];
    assert.ok(exports, `Unexpected dependency: ${specifier}`);
    return new SyntheticModule(Object.keys(exports), function () {
      for (const [name, value] of Object.entries(exports)) this.setExport(name, value);
    }, { context });
  });
  await module.evaluate();
  state.catalog = () => JSON.parse(state.files.get(catalogPath)).mcpServers;
  return state;
}

test('startup requests each installed MCP audience with the selected persona and maps remote URLs', async () => {
  const state = await start([...initial,
    plugin('ui-only', 'http://ui', { manifest: { name: 'ui-only', kind: 'ui' } }),
    { manifest: { name: 'no-remote', kind: 'mcp' }, serverJson: {} },
    plugin('fallback', 'ignored', { serverJson: { name: 'registry-name', remotes: [{}, { url: 'http://fallback/mcp' }] } }),
  ]);
  assert.deepEqual(state.calls.map(({ url, method, headers, body }) => ({ url, method, headers, body })),
    ['billing', 'portfolio', 'fallback'].map((audience) => ({
      url: 'http://lab/dev/token', method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: { userId: 'eve', tenantId: 'firm-b', role: 'ADMIN', audience },
    })));
  assert.deepEqual(state.catalog(), {
    'billing server': { type: 'streamable-http', url: 'http://billing/mcp', protocolEra: 'auto', headers: { Authorization: 'Bearer billing-1' } },
    'portfolio server': { type: 'streamable-http', url: 'http://portfolio/mcp', protocolEra: 'auto', headers: { Authorization: 'Bearer portfolio-2' } },
    'registry-name': { type: 'streamable-http', url: 'http://fallback/mcp', protocolEra: 'auto', headers: { Authorization: 'Bearer fallback-3' } },
  });
  const operations = state.events.filter(([kind]) => ['write', 'rename', 'spawn'].includes(kind));
  assert.deepEqual(operations.map(([kind]) => kind), ['write', 'rename', 'spawn']);
  assert.deepEqual(operations[0], ['write', '/catalog/mcp.json.tmp']);
  assert.deepEqual(operations[1], ['rename', '/catalog/mcp.json.tmp', '/catalog/mcp.json']);
});

test('hourly refresh renews every audience; polling only renews when installed servers change', async () => {
  const state = await start();
  await state.timers.get(poll)();
  assert.equal(state.calls.length, 2);
  await state.timers.get(hour)();
  assert.deepEqual(state.calls.map(({ body }) => body.audience), ['billing', 'portfolio', 'billing', 'portfolio']);
  assert.equal(state.catalog()['billing server'].headers.Authorization, 'Bearer billing-3');
  state.plugins = [plugin('code', 'http://code/mcp')];
  await state.timers.get(poll)();
  assert.deepEqual(Object.keys(state.catalog()), ['code server']);
  assert.equal(state.calls.at(-1).body.audience, 'code');
});

test('refused or failed audience requests clear previous credentials without falling back', async () => {
  const state = await start();
  state.reply = async ({ audience }) => {
    if (audience === 'billing') return { ok: false, status: 403 };
    throw new Error('offline');
  };
  await state.timers.get(hour)();
  assert.deepEqual(Object.values(state.catalog()).map(({ headers }) => headers), [{}, {}]);
  assert.deepEqual(state.calls.slice(2).map(({ body }) => body.audience), ['billing', 'portfolio']);
  assert.equal(state.errors.length, 2);
});

test('successful responses with malformed tokens clear credentials instead of writing invalid bearer headers', async () => {
  const state = await start();
  for (const response of [{}, { token: null }, { token: {} }, { token: '' }, { token: ' \t\n' }, null]) {
    state.reply = async ({ audience }) => ({ ok: true, json: async () =>
      audience === 'billing' ? response : { token: 'valid-portfolio-token' } });
    await state.timers.get(hour)();
    assert.deepEqual(state.catalog()['billing server'].headers, {});
    assert.equal(state.catalog()['portfolio server'].headers.Authorization, 'Bearer valid-portfolio-token');
  }
  assert.equal(state.errors.length, 6);
});

test('overlapping refreshes serialize writes and reread the installed list after pending requests', async () => {
  const state = await start();
  let release;
  const gate = new Promise((resolve) => { release = resolve; });
  state.reply = async ({ audience }) => { await gate; return { ok: true, json: async () => ({ token: `new-${audience}` }) }; };
  const hourly = state.timers.get(hour)();
  await new Promise(setImmediate);
  state.plugins = [plugin('code', 'http://code/mcp')];
  const changed = state.timers.get(poll)();
  await new Promise(setImmediate);
  assert.equal(state.calls.length, 4, 'second refresh must wait for the first');
  assert.equal(state.events.filter(([kind]) => kind === 'rename').length, 1);
  release();
  await Promise.all([hourly, changed]);
  assert.deepEqual(Object.keys(state.catalog()), ['code server']);
  assert.equal(state.calls.at(-1).body.audience, 'code');
  assert.deepEqual(state.events.filter(([kind]) => ['write', 'rename'].includes(kind)).map(([kind]) => kind),
    ['write', 'rename', 'write', 'rename', 'write', 'rename']);
});

for (const phase of ['fetch', 'body']) {
  test(`a stalled token ${phase} is aborted after ten seconds and the queued installed-list refresh completes`, async () => {
    const state = await start([initial[0]]);
    state.reply = async ({ audience }, index, signal) => {
      if (audience === 'code') return { ok: true, json: async () => ({ token: 'code-token' }) };
      const stalled = () => new Promise((resolve, reject) => {
        signal.addEventListener('abort', () => reject(signal.reason), { once: true });
      });
      if (phase === 'fetch') return stalled();
      return { ok: true, json: stalled };
    };
    const hourly = state.timers.get(hour)();
    await new Promise(setImmediate);
    state.plugins = [plugin('code', 'http://code/mcp')];
    const changed = state.timers.get(poll)();
    await new Promise(setImmediate);
    assert.equal(state.calls.length, 2, 'installed-list refresh waits for the stalled request');
    const deadline = state.deadlines.at(-1);
    assert.equal(deadline.milliseconds, 10_000);
    deadline.expire();
    await Promise.all([hourly, changed]);
    assert.deepEqual(Object.keys(state.catalog()), ['code server']);
    assert.equal(state.catalog()['code server'].headers.Authorization, 'Bearer code-token');
    assert.equal(state.errors.length, 1);
    assert.match(state.errors[0], /no token for billing server/);
  });
}

test('hourly write failures are reported and subsequent refreshes recover', async () => {
  const state = await start();
  state.failWrite = true;
  await state.timers.get(hour)();
  assert.match(state.errors.at(-1), /disk unavailable/);
  assert.equal(state.catalog()['billing server'].headers.Authorization, 'Bearer billing-1');
  state.failWrite = false;
  await state.timers.get(hour)();
  assert.equal(state.catalog()['billing server'].headers.Authorization, 'Bearer billing-5');
});

test('signals are forwarded and inspector exit determines launcher exit', async () => {
  const state = await start();
  state.signals.get('SIGINT')();
  state.signals.get('SIGTERM')();
  assert.deepEqual(state.kills, ['SIGINT', 'SIGTERM']);
  state.childEvents.get('exit')(7, null);
  state.childEvents.get('exit')(null, 'SIGTERM');
  assert.deepEqual(state.exits, [7, 1]);
});
