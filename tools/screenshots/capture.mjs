// Re-takes the README screenshots from the running stack: `make screenshots` (SHOTS=chat,topology for a subset).
// Each screen is captured twice from the same page state, light then dark (emulated OS colour scheme), into docs/screenshots/<name>-<theme>.png.
import { mkdir, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const BASE = process.env.BASE_URL ?? 'http://localhost:7171';
const OUT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../docs/screenshots');
const PERSONA = 'alice'; // firm-a TENANT_ADMIN: sees every admin screen
const VIEWPORT = { width: 1440, height: 900 };
const OUTPUT_WIDTH = 1600;
const WARN_BYTES = 400 * 1024;
const TURN_TIMEOUT = 180_000;

async function ask(page, question) {
  await page.getByRole('textbox', { name: 'Message' }).fill(question);
  await page.getByRole('button', { name: 'Send' }).click();
  // A finished turn is the one that offers feedback; streaming turns do not.
  await page.getByRole('group', { name: 'Feedback' }).last().waitFor({ timeout: TURN_TIMEOUT });
  // The history lists the persona's earlier (test) conversations: keep it out of the picture, and start the frame at
  // the question rather than the end of the answer.
  await page.getByRole('button', { name: 'Collapse history' }).click();
  await page.getByText(question, { exact: true }).last().evaluate((el) => {
    el.scrollIntoView({ block: 'start' });
    window.scrollTo(0, 0); // scrollIntoView also moves the window; keep the navigation in the frame
  });
}

const SHOTS = [
  {
    name: 'chat',
    url: '/chat',
    async prepare(page) {
      await ask(page, 'Why did run 4417 fail and how do I fix it?');
      await page.getByRole('tab', { name: 'Behind the scenes' }).click();
    },
  },
  {
    name: 'code-snippets',
    url: '/chat',
    async prepare(page) {
      await ask(page, 'How does the code make a tool call idempotent?');
      await page.getByRole('tab', { name: /Code snippets/ }).click();
      await page.getByRole('region', { name: 'Code snippets' }).waitFor();
    },
  },
  { name: 'topology', url: '/topology', ready: 'svg' },
  { name: 'jev', url: '/admin/jev', ready: 'table' },
  { name: 'evals', url: '/evals', ready: 'table' },
  { name: 'compliance', url: '/admin/compliance', ready: 'main' },
];

async function devSession() {
  const users = await (await fetch(`${BASE}/dev/users`)).json();
  const user = users.find((u) => u.userId === PERSONA);
  if (!user) throw new Error(`dev persona '${PERSONA}' not found at ${BASE}/dev/users`);
  const res = await fetch(`${BASE}/dev/token`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ userId: user.userId, tenantId: user.tenantId, role: user.role, domainRoles: user.domainRoles, advisorIds: user.advisorIds }),
  });
  if (!res.ok) throw new Error(`POST /dev/token answered ${res.status}`);
  const { token, expiresAt } = await res.json();
  return { token, expiresAt, user };
}

async function capture(page, shot) {
  for (const theme of ['light', 'dark']) {
    // The app stays in "system" mode, which follows the OS through `color-scheme`; emulating the OS scheme flips it
    // without touching the page state.
    await page.emulateMedia({ colorScheme: theme });
    await page.waitForTimeout(300); // let colour transitions settle
    const file = path.join(OUT, `${shot.name}-${theme}.png`);
    await page.screenshot({ path: file });
    const { size } = await stat(file);
    const kb = Math.round(size / 1024);
    console.log(`  ${path.relative(process.cwd(), file)}  ${kb} KB${size > WARN_BYTES ? '  (over 400 KB)' : ''}`);
  }
}

// Ctrl+C and SIGTERM (stop-anything): close the browser, keep what was written, and say so. Node's default exit on a
// signal would skip every `finally` below and leave the browser running.
let browser;
let stopping = false;
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.once(signal, async () => {
    stopping = true;
    console.error('Cancelled. The screenshots already written are kept; run `make screenshots` again for the rest.');
    await browser?.close().catch(() => {});
    process.exit(130);
  });
}

async function main() {
  const wanted = (process.env.SHOTS ?? '').split(',').map((s) => s.trim()).filter(Boolean);
  const unknown = wanted.filter((w) => !SHOTS.some((s) => s.name === w));
  if (unknown.length) throw new Error(`unknown SHOTS: ${unknown.join(', ')} (known: ${SHOTS.map((s) => s.name).join(', ')})`);
  const shots = wanted.length ? SHOTS.filter((s) => wanted.includes(s.name)) : SHOTS;

  await mkdir(OUT, { recursive: true });
  const session = await devSession();
  // This script closes the browser on a signal itself (above), so Playwright's own handlers stay out of the way.
  browser = await chromium.launch({ handleSIGINT: false, handleSIGTERM: false });
  const failed = [];
  try {
    for (const shot of shots) {
      console.log(shot.name);
      const context = await browser.newContext({
        viewport: VIEWPORT,
        deviceScaleFactor: OUTPUT_WIDTH / VIEWPORT.width,
        colorScheme: 'light',
      });
      // The web app reads its session from sessionStorage; the token never leaves this browser context.
      await context.addInitScript((s) => sessionStorage.setItem('maf-lab.session', JSON.stringify(s)), session);
      const page = await context.newPage();
      try {
        await page.goto(`${BASE}${shot.url}`, { waitUntil: 'networkidle' });
        if (shot.prepare) await shot.prepare(page);
        else await page.locator(shot.ready).first().waitFor({ timeout: 30_000 });
        await page.waitForLoadState('networkidle');
        await capture(page, shot);
      } catch (err) {
        // Stopped: the page went with the browser, which is not this shot failing.
        if (stopping) return;
        failed.push(shot.name);
        console.error(`  ${shot.name} failed: ${err.message.split('\n')[0]}`);
      } finally {
        await context.close();
      }
    }
  } finally {
    await browser.close();
  }
  if (failed.length) {
    console.error(`failed: ${failed.join(', ')} (files already written are kept)`);
    process.exit(1);
  }
}

main().catch((err) => {
  // A run being stopped fails its page calls; the signal handler says so and exits.
  if (stopping) return;
  console.error(err.message);
  process.exit(1);
});
