// Real-service integration harness. Never copy this file into dist/.
// Usage: node tests/play-real.mjs <private-fixture.json> <artifact-directory>
// Set ARKYA_PLAYWRIGHT_MODULE to an existing Playwright package if it is not installed locally.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, isAbsolute } from 'node:path';
import { createRequire } from 'node:module';
import { createInterface } from 'node:readline';

const require = createRequire(import.meta.url);
const site = 'http://127.0.0.1:51806';
const privateFixturePath = process.argv[2];
const artifactDirectory = process.argv[3] && resolve(process.argv[3]);
const secrets = [];
let browser;
let page;
let input;
let verifyRevocation;
let sequence = 0;
const report = { startedAt: new Date().toISOString(), scope: 'Real local services, MongoDB, Unity WebGL and dedicated',
  checks: [], http: [], sockets: [], browserErrors: [], consoleMessages: [], metrics: [], actions: [],
  movementAuthority: 'Awaiting independent dedicated/Mongo verification' };

function redact(value) {
  let text = String(value);
  for (const secret of secrets) if (secret) text = text.split(secret).join('[redacted]');
  return text.replace(/Bearer\s+[^\s"']+/gi, 'Bearer [redacted]').replace(/[A-Za-z0-9_-]{43,}/g, '[redacted]');
}

function emit(value) { process.stdout.write(redact(JSON.stringify(value)) + '\n'); }
function check(condition, name) {
  if (!condition) throw new Error(name);
  report.checks.push(name);
}

function localOrigin(value, protocols = ['http:', 'https:']) {
  const url = new URL(value);
  if (!protocols.includes(url.protocol) || !['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) ||
      url.username || url.password || url.search || url.hash || (protocols.includes('http:') && url.pathname !== '/'))
    throw new Error('The private fixture must use explicit loopback service addresses.');
  return url;
}

async function saveReport() {
  await writeFile(resolve(artifactDirectory, 'browser-report.json'), redact(JSON.stringify(report, null, 2)) + '\n');
}

async function capture(label) {
  const filename = `${String(++sequence).padStart(2, '0')}-${label}.png`;
  await page.screenshot({ path: resolve(artifactDirectory, filename), fullPage: true });
  return filename;
}

async function snapshot() {
  return {
    title: await page.title(), path: new URL(page.url()).pathname,
    gameVisible: await page.locator('.game-frame.is-playing').count() === 1,
    canvas: await page.locator('#unity-canvas').boundingBox(),
    status: await page.locator('#game-status').innerText(),
    requests: report.http.map(value => ({ ...value })), sockets: report.sockets,
    browserErrorCount: report.browserErrors.length,
    metrics: report.metrics.at(-1) || null,
  };
}

async function command(value) {
  const allowedKeys = new Set(['w', 'a', 's', 'd', 'z', 'q', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Shift', 'Space', 'Escape', 'm', 'j', 'i']);
  switch (value.action) {
    case 'click': {
      const viewport = page.viewportSize();
      if (![value.x, value.y].every(Number.isFinite) || value.x < 0 || value.y < 0 || value.x > viewport.width || value.y > viewport.height)
        throw new Error('Click coordinates must be inside the observed viewport.');
      await page.mouse.click(value.x, value.y);
      break;
    }
    case 'hold': {
      if (!Array.isArray(value.keys) || !value.keys.length || value.keys.some(key => !allowedKeys.has(key)) ||
          !Number.isInteger(value.ms) || value.ms < 1 || value.ms > 5000) throw new Error('Use known movement keys for at most 5000 ms.');
      await page.locator('#unity-canvas').focus();
      try {
        for (const key of value.keys) await page.keyboard.down(key);
        await page.waitForTimeout(value.ms);
        const screenshot = await capture(value.keys.includes('Shift') ? 'run-active' : 'walk-active');
        report.actions.push({ at: new Date().toISOString(), action: 'hold', keys: value.keys, millisecondsBeforeCapture: value.ms, screenshot });
      } finally { for (const key of [...value.keys].reverse()) await page.keyboard.up(key); }
      break;
    }
    case 'press':
      if (!allowedKeys.has(value.key)) throw new Error('Unsupported test key.');
      await page.locator('#unity-canvas').focus();
      await page.keyboard.press(value.key);
      break;
    case 'wait':
      if (!Number.isInteger(value.ms) || value.ms < 1 || value.ms > 5000) throw new Error('Wait is limited to 5000 ms.');
      await page.waitForTimeout(value.ms);
      break;
    case 'screenshot': break;
    case 'state': return snapshot();
    case 'verify-admission':
      check(report.http.some(item => item.method === 'GET' && /^\/v1\/characters\/[0-9a-f-]{36}$/i.test(item.path) && item.status === 200),
        'The real Unity interface reads a selected persistent character');
      check(report.http.some(item => item.method === 'POST' && /^\/v1\/characters\/[0-9a-f-]{36}\/admissions$/i.test(item.path) && item.status === 200),
        'Characters issues a real admission through the browser');
      check(report.sockets.some(socket => socket.expected && !socket.closed && socket.sentFrames > 0 && socket.receivedFrames > 0),
        'The real dedicated WebSocket carries bidirectional Netcode traffic');
      break;
    case 'logout': {
      const logoutResponse = page.waitForResponse(response => response.url().endsWith('/v1/auth/logout') && response.request().method() === 'POST', { timeout: 20000 });
      logoutResponse.catch(() => {});
      await page.locator('#game-logout').click();
      check([204, 401].includes((await logoutResponse).status()), 'The real Identity logout endpoint confirms revocation');
      await page.locator('#game-sign-in').waitFor({ state: 'visible', timeout: 20000 });
      await page.waitForFunction(() => sessionStorage.getItem('arkya-session-v1') === null, null, { timeout: 20000 });
      check(await page.locator('.game-frame.is-playing').count() === 0, 'Logout hides the real Unity canvas');
      check(await page.evaluate(() => sessionStorage.getItem('arkya-session-v1') === null), 'Logout removes the isolated tab credential');
      for (let attempt = 0; attempt < 30 && report.sockets.some(socket => !socket.closed); attempt++) await page.waitForTimeout(500);
      check(report.sockets.every(socket => socket.closed), 'Logout closes every observed dedicated WebSocket');
      check(await verifyRevocation(), 'Real Identity rejects the seeded bearer after logout');
      break;
    }
    case 'close': return { close: true };
    default: throw new Error('Unknown QA action.');
  }
  await page.waitForTimeout(250);
  const screenshot = await capture(value.action);
  await saveReport();
  return { screenshot, ...(await snapshot()) };
}

async function main() {
  if (!privateFixturePath || !isAbsolute(privateFixturePath) || !artifactDirectory)
    throw new Error('Provide an absolute private fixture path and an artifact directory.');
  const fixture = JSON.parse(await readFile(privateFixturePath, 'utf8'));
  // Redaction is defensive: request/response bodies, headers, traces and storage are never recorded.
  secrets.push(fixture.sessionToken, fixture.dedicatedKey, fixture.mongo);
  const identity = localOrigin(fixture.identityBaseUrl).origin;
  const characters = localOrigin(fixture.charactersBaseUrl).origin;
  const realms = localOrigin(fixture.realmsBaseUrl).origin;
  const endpoint = localOrigin(fixture.endpoint, ['ws:', 'wss:']).href;
  if (typeof fixture.sessionToken !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(fixture.sessionToken))
    throw new Error('The isolated fixture has no valid Arkya session credential.');
  await mkdir(artifactDirectory, { recursive: true });
  const { chromium } = require(process.env.ARKYA_PLAYWRIGHT_MODULE || 'playwright');
  browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1600, height: 1400 } });
  await context.exposeBinding('__arkyaQaMetrics', (_source, value) => {
    const numeric = Object.fromEntries(Object.entries(value).filter(([, item]) => typeof item === 'number' && Number.isFinite(item)));
    report.metrics.push({ at: new Date().toISOString(), ...numeric });
    if (report.metrics.length > 600) report.metrics.shift();
  });
  // Read-only test instrumentation: never export the Unity instance or inspect session payloads.
  await context.addInitScript(() => {
    document.addEventListener('load', event => {
      if (!(event.target instanceof HTMLScriptElement) || !event.target.src.endsWith('.loader.js')) return;
      const factory = window.createUnityInstance;
      if (typeof factory !== 'function') return;
      window.createUnityInstance = function (...args) {
        return Promise.resolve(Reflect.apply(factory, this, args)).then(instance => {
          if (typeof instance.GetMetricsInfo !== 'function') return instance;
          const sample = () => {
            if (instance.Module?.shouldQuit) return false;
            try {
              const metrics = instance.GetMetricsInfo();
              const numbers = {};
              for (const key of ['totalWASMHeapSize', 'usedWASMHeapSize', 'totalJSHeapSize', 'usedJSHeapSize',
                'pageLoadTime', 'pageLoadTimeToFrame1', 'fps', 'movingAverageFps', 'assetLoadTime',
                'webAssemblyStartupTime', 'codeDownloadTime', 'gameStartupTime', 'numJankedFrames']) {
                if (Number.isFinite(metrics[key])) numbers[key] = metrics[key];
              }
              void window.__arkyaQaMetrics(numbers).catch(() => {});
              return true;
            } catch { return false; }
          };
          sample();
          const timer = setInterval(() => { if (!sample()) clearInterval(timer); }, 1000);
          return instance;
        });
      };
    }, true);
  });
  page = await context.newPage();
  const privateOrigins = new Set([identity, characters, realms]);
  const requests = [];
  page.on('request', request => requests.push(new URL(request.url()).pathname));
  page.on('response', response => {
    const url = new URL(response.url());
    if (privateOrigins.has(url.origin)) report.http.push({ origin: url.origin, path: url.pathname, method: response.request().method(), status: response.status() });
  });
  page.on('pageerror', error => report.browserErrors.push(redact(error.message)));
  page.on('console', message => {
    if (['warning', 'error'].includes(message.type()) && report.consoleMessages.length < 100)
      report.consoleMessages.push({ type: message.type(), message: redact(message.text()) });
  });
  page.on('websocket', socket => {
    const url = new URL(socket.url());
    const item = { endpoint: url.origin + url.pathname, expected: socket.url() === endpoint, sentFrames: 0, receivedFrames: 0, closed: false };
    report.sockets.push(item);
    socket.on('framesent', () => { item.sentFrames++; });
    socket.on('framereceived', () => { item.receivedFrames++; });
    socket.on('close', () => { item.closed = true; });
  });

  // Only public configuration is replaced. API, asset and WebSocket responses are all real.
  await page.route(`${site}/auth-config.js`, route => route.fulfill({ contentType: 'text/javascript',
    body: `window.ArkyaAuthConfig=Object.freeze(${JSON.stringify({ identityBaseUrl: identity, charactersBaseUrl: characters })});` }));
  await page.goto(`${site}/play.html?lang=fr`);
  await page.locator('#game-sign-in').waitFor({ state: 'visible' });
  check(!requests.some(path => path.startsWith('/game/')), 'Real production shell requests no Unity asset before authentication');
  check(report.http.length === 0, 'An empty tab sends no fixture credential before authentication');
  await capture('auth-guard');
  emit({ phase: 'guard-passed', checks: report.checks.length });

  const me = await fetch(`${identity}/v1/auth/me`, { headers: { Authorization: `Bearer ${fixture.sessionToken}`, Accept: 'application/json' }, redirect: 'error' });
  if (!me.ok) throw new Error(`The real fixture Identity rejected the seeded session (HTTP ${me.status}).`);
  const user = await me.json();
  if (user.accountId !== fixture.accountId || !(Date.parse(user.expiresAt) > Date.now()) || typeof user.displayName !== 'string')
    throw new Error('The real Identity response does not match the isolated fixture.');
  const record = { version: 1, apiOrigin: identity, sessionToken: fixture.sessionToken,
    accountId: user.accountId, displayName: user.displayName, expiresAt: user.expiresAt };
  await page.evaluate(value => sessionStorage.setItem('arkya-session-v1', JSON.stringify(value)), record);
  record.sessionToken = '';
  verifyRevocation = async () => {
    const response = await fetch(`${identity}/v1/auth/me`, { headers: { Authorization: `Bearer ${fixture.sessionToken}` }, redirect: 'error' });
    return response.status === 401;
  };
  const servicesLoaded = Promise.all([
    page.waitForResponse(response => response.url() === `${characters}/v1/realms` && response.status() === 200, { timeout: 300000 }),
    page.waitForResponse(response => response.url() === `${characters}/v1/characters` && response.status() === 200, { timeout: 300000 }),
  ]);
  // If navigation fails first, closing the browser also rejects these pending waits.
  servicesLoaded.catch(() => {});
  await page.reload();
  emit({ phase: 'loading-real-unity', note: 'No API or game response fixtures are installed.' });
  await Promise.all([page.locator('.game-frame.is-playing').waitFor({ timeout: 300000 }), servicesLoaded]);
  check(report.http.filter(item => item.origin === identity && item.path === '/v1/auth/me' && item.status === 200).length >= 4,
    'Site restore, loading gate, handoff and real Unity each verify the seeded session with Identity');
  check(report.http.some(item => item.origin === characters && item.path === '/v1/characters' && item.status === 200), 'Real Unity loads persistent Characters');
  check(report.http.some(item => item.origin === characters && item.path === '/v1/realms' && item.status === 200), 'Real Unity loads the actual realm catalogue');
  const screenshot = await capture('character-selection');
  await saveReport();
  emit({ phase: 'awaiting-ui-input', screenshot, ...(await snapshot()) });

  // Coordinates are supplied only after inspecting this real render. No Unity state is injected.
  input = createInterface({ input: process.stdin, crlfDelay: Infinity });
  for await (const line of input) {
    try {
      const result = await command(JSON.parse(line));
      emit({ phase: 'command-result', ...result });
      if (result.close) break;
    } catch (error) { emit({ phase: 'command-error', error: redact(error.message) }); }
  }
}

try { await main(); }
catch (error) {
  report.failure = redact(error.message);
  if (page && !page.isClosed()) { try { report.failureScreenshot = await capture('failure'); } catch { /* Preserve the original failure. */ } }
  emit({ phase: 'failed', error: report.failure });
  process.exitCode = 1;
} finally {
  input?.close();
  if (page && !page.isClosed()) {
    try { await page.evaluate(() => sessionStorage.clear()); } catch { /* Closing a failed page must still dispose the browser. */ }
  }
  await browser?.close();
  if (artifactDirectory) { try { await saveReport(); } catch { /* No sensitive fallback output. */ } }
}
