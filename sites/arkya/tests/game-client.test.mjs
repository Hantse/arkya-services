import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { createAuthClient } from '../dist/auth-client.mjs';
import { createGameController, validateBuildManifest } from '../dist/game-client.mjs';
import { gameReturnPath, gameSignInPath } from '../dist/game-navigation.mjs';

const API = 'https://api.arkya.online';
const SITE = 'https://arkya-atlas.easykrill33.chatgpt.site';
const TOKEN = 'T'.repeat(43);
const START = Date.parse('2026-10-18T12:00:00Z');
const USER = { accountId: '197d1fa3-83c2-45cb-bb26-f4b574e88ee8', displayName: 'Explorateur', expiresAt: new Date(START + 3600000).toISOString() };
const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; };
const settle = () => new Promise(resolve => setImmediate(resolve));

function fixture({ saved = true, responses = [], loadGame, charactersBaseUrl = API } = {}) {
  let time = START;
  const events = [];
  const states = [];
  const timers = new Map();
  let nextTimer = 0;
  const storageData = new Map(saved ? [['arkya-session-v1', JSON.stringify({ version: 1, apiOrigin: API, ...USER, sessionToken: TOKEN })]] : []);
  const storage = { getItem: key => storageData.get(key), setItem: (key, value) => storageData.set(key, value), removeItem: key => storageData.delete(key) };
  const auth = createAuthClient({ baseUrl: API, charactersBaseUrl, siteOrigin: SITE, storage, cryptoImpl: webcrypto, now: () => time,
    fetchImpl: async (url, init) => {
      events.push({ type: 'request', url, init });
      const next = responses.shift();
      if (next instanceof Error) throw next;
      if (typeof next === 'function') return next(url, init);
      return next || json(USER);
    } });
  const game = {
    SendMessage(object, method, payload) { events.push({ type: 'message', object, method, payload }); },
    async Quit() { events.push({ type: 'quit' }); },
    SetFullscreen(value) { events.push({ type: 'fullscreen', value }); },
  };
  const controller = createGameController({ auth, now: () => time,
    loadGame: async progress => { events.push({ type: 'load' }); return loadGame ? loadGame(progress, game) : game; },
    onState: state => states.push(state),
    setTimer: (callback, delay) => { const id = ++nextTimer; timers.set(id, { callback, due: time + delay }); return id; },
    clearTimer: id => timers.delete(id),
  });
  return { auth, controller, game, events, states, storageData,
    async advance(ms) {
      time += ms;
      for (const [id, timer] of [...timers]) if (timer.due <= time) { timers.delete(id); timer.callback(); }
      await settle();
    } };
}

test('signed-out visitors never request a build or receive a game session', async () => {
  const f = fixture({ saved: false });
  await f.controller.start();
  assert.deepEqual(f.events, []);
  assert.equal(f.states.at(-1).status, 'signed-out');
});

test('an unreachable Identity service fails closed before loading Unity', async () => {
  const f = fixture({ responses: [new Error('DNS unavailable')] });
  await f.controller.start();
  assert.deepEqual(f.events.map(e => e.type), ['request']);
  assert.equal(f.states.at(-1).status, 'network');
  assert.equal(f.auth.session(), null);
});

test('a rejected saved session never loads Unity and is removed', async () => {
  const f = fixture({ responses: [json({}, 401)] });
  await f.controller.start();
  assert.equal(f.events.some(e => e.type === 'load'), false);
  assert.equal(f.storageData.size, 0);
  assert.equal(f.states.at(-1).status, 'signed-out');
});

test('two real /me checks bracket loading, then a single narrow handoff starts the game', async () => {
  const f = fixture();
  await f.controller.start();
  await f.controller.start();
  assert.deepEqual(f.events.map(e => e.type), ['request', 'load', 'request', 'message']);
  for (const request of f.events.filter(e => e.type === 'request')) {
    assert.equal(request.url, `${API}/v1/auth/me`);
    assert.equal(request.init.headers.Authorization, `Bearer ${TOKEN}`);
    assert.equal(request.init.credentials, 'omit');
    assert.equal(request.url.includes(TOKEN), false);
  }
  const message = f.events.at(-1);
  assert.equal(message.object, 'ArkyaWebSession');
  assert.equal(message.method, 'AcceptSiteSession');
  assert.deepEqual(JSON.parse(message.payload), { sessionToken: TOKEN, ...USER, identityBaseUrl: API, charactersBaseUrl: API });
  assert.equal(f.states.at(-1).status, 'playing');
  assert.equal(JSON.stringify(f.states).includes(TOKEN), false);
  await f.controller.stop();
});

test('the game callback cannot read an unverified session and its temporary payload is scrubbed', async () => {
  const f = fixture();
  assert.equal(f.auth.session(), null);
  let held;
  const result = await f.auth.withGameSession(payload => {
    held = payload;
    assert.equal(payload.sessionToken, TOKEN);
    return payload.sessionToken;
  });
  assert.equal(result, undefined);
  assert.equal(held.sessionToken, '');
  assert.equal(f.events.length, 1);
  assert.equal('sessionToken' in f.auth.session(), false);
  assert.equal('sessionToken' in f.auth, false);
});

test('a callback exception still scrubs the bearer', async () => {
  const f = fixture();
  let held;
  await assert.rejects(f.auth.withGameSession(payload => { held = payload; throw new Error('consumer failure'); }), /consumer failure/);
  assert.equal(held.sessionToken, '');
});

test('invalid Characters configuration cannot export the session', async () => {
  const f = fixture({ charactersBaseUrl: 'http://untrusted.example' });
  let called = false;
  await assert.rejects(f.auth.withGameSession(() => { called = true; }), { code: 'unavailable' });
  assert.equal(called, false);
  assert.deepEqual(f.events, []);
});

test('session rejection after loading clears and quits without importing a bearer', async () => {
  const f = fixture({ responses: [json(USER), json({}, 401)] });
  await f.controller.start();
  assert.deepEqual(f.events.filter(e => e.type === 'message').map(e => e.method), ['ClearSiteSession']);
  assert.equal(f.events.at(-1).type, 'quit');
  assert.equal(f.states.at(-1).status, 'signed-out');
});

test('logout during an outstanding /me cannot resurrect a session or load Unity', async () => {
  const response = deferred();
  const f = fixture({ responses: [() => response.promise, new Response(null, { status: 204 })] });
  const starting = f.controller.start();
  await f.controller.stop();
  await f.auth.logout();
  response.resolve(json(USER));
  await starting;
  assert.equal(f.events.some(e => e.type === 'load'), false);
  assert.equal(f.auth.session(), null);
});

test('expiry during loading disposes the late engine without importing the old session', async () => {
  const loading = deferred();
  const f = fixture({ loadGame: () => loading.promise });
  const starting = f.controller.start();
  await settle();
  await f.advance(3600001);
  assert.equal(f.states.at(-1).status, 'expired');
  loading.resolve(f.game);
  await starting;
  assert.deepEqual(f.events.filter(e => e.type === 'message').map(e => e.method), ['ClearSiteSession']);
  assert.equal(f.events.at(-1).type, 'quit');
});

test('playing expiry clears Unity before Quit and leaves fullscreen disabled', async () => {
  const f = fixture();
  await f.controller.start();
  await f.advance(3600001);
  assert.deepEqual(f.events.slice(-2).map(e => e.type === 'message' ? e.method : e.type), ['ClearSiteSession', 'quit']);
  f.controller.fullscreen();
  assert.equal(f.events.at(-1).type, 'quit');
  assert.equal(f.states.at(-1).status, 'expired');
});

test('session loss while playing hides the game and tears it down exactly once', async () => {
  const f = fixture({ responses: [json(USER), json(USER), new Response(null, { status: 204 })] });
  await f.controller.start();
  await f.auth.logout();
  f.controller.sessionChanged();
  f.controller.sessionChanged();
  await settle();
  assert.equal(f.events.filter(e => e.type === 'quit').length, 1);
  assert.equal(f.states.at(-1).status, 'signed-out');
});

test('periodic server revalidation stops a revoked game session', async () => {
  const f = fixture({ responses: [json(USER), json(USER), json({}, 401)] });
  await f.controller.start();
  await f.advance(60000);
  assert.equal(f.events.filter(e => e.type === 'request').length, 3);
  assert.equal(f.events.at(-1).type, 'quit');
  assert.equal(f.states.at(-1).status, 'signed-out');
});

test('a revalidation network outage stops play rather than claiming a live session', async () => {
  const f = fixture({ responses: [json(USER), json(USER), new Error('offline')] });
  await f.controller.start();
  await f.controller.revalidate();
  assert.equal(f.states.at(-1).status, 'network');
  assert.equal(f.events.at(-1).type, 'quit');
});

test('language/session notifications during a recheck do not race the server decision', async () => {
  const response = deferred();
  const f = fixture({ responses: [json(USER), json(USER), () => response.promise] });
  await f.controller.start();
  const recheck = f.controller.revalidate();
  f.controller.sessionChanged();
  await f.controller.revalidate();
  assert.equal(f.events.filter(e => e.type === 'request').length, 3);
  assert.equal(f.events.some(e => e.type === 'quit'), false);
  response.resolve(json(USER));
  await recheck;
  assert.equal(f.states.at(-1).status, 'playing');
  await f.controller.stop();
});

test('missing build errors remain explicit with no simulation or fallback', async () => {
  const f = fixture({ loadGame: () => { throw Object.assign(new Error('missing'), { code: 'build' }); } });
  await f.controller.start();
  assert.equal(f.states.at(-1).status, 'build');
  assert.equal(f.events.some(e => e.type === 'message'), false);
});

const manifest = { schemaVersion: 1, buildId: 'arkya-web-beta-v1', loaderUrl: 'Build/Arkya.loader.js',
  dataUrl: 'Build/Arkya.data.unityweb', frameworkUrl: 'Build/Arkya.framework.js.unityweb', codeUrl: 'Build/Arkya.wasm.unityweb',
  streamingAssetsUrl: 'StreamingAssets', companyName: 'Arkya', productName: 'Arkya', productVersion: 'beta-v1' };

test('the manifest resolves only real relative Unity file paths below the game directory', () => {
  const result = validateBuildManifest(manifest, `${SITE}/game/build-config.json`, SITE);
  assert.equal(result.loaderUrl, `${SITE}/game/Build/Arkya.loader.js`);
  assert.equal(result.config.codeUrl, `${SITE}/game/Build/Arkya.wasm.unityweb`);
  assert.equal('loaderUrl' in result.config, false);
});

test('manifest validation rejects external scripts, traversal, URL parameters and another protocol', () => {
  const bad = ['https://evil.test/Build/x.loader.js', '//evil.test/x.loader.js', '/game/x.loader.js', '../x.loader.js',
    'Build/../../x.loader.js', 'Build/%2e%2e/x.loader.js', 'Build/x.loader.js?token=secret', 'Build/x.loader.js#x', 'Build\\x.loader.js'];
  for (const loaderUrl of bad) assert.throws(() => validateBuildManifest({ ...manifest, loaderUrl }, `${SITE}/game/build-config.json`, SITE), { code: 'build' });
  for (const patch of [{ buildId: 'other-build' }, { schemaVersion: 2 }, { codeUrl: 'Build/x.wasm.br' }, { companyName: '' }])
    assert.throws(() => validateBuildManifest({ ...manifest, ...patch }, `${SITE}/game/build-config.json`, SITE), { code: 'build' });
  assert.throws(() => validateBuildManifest(manifest, 'https://evil.test/game/build-config.json', SITE), { code: 'build' });
});

test('post-login return accepts only the exact local game page and preserves language', () => {
  assert.equal(gameReturnPath('?returnTo=play.html', 'en'), 'play.html?lang=en');
  assert.equal(gameReturnPath('?returnTo=play.html', 'fr'), 'play.html?lang=fr');
  assert.equal(gameSignInPath('en'), 'account.html?returnTo=play.html&lang=en');
  for (const input of ['', '?returnTo=https://evil.test', '?returnTo=//evil.test', '?returnTo=/play.html',
    '?returnTo=play.html?token=secret', '?returnTo=play.html&returnTo=play.html', '?returnTo=javascript:alert(1)', '?returnTo=../play.html'])
    assert.equal(gameReturnPath(input), null, input);
});
