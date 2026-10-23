import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto, createHash } from 'node:crypto';
import { createAuthClient } from '../dist/auth-client.mjs';

const API = 'https://identity.arkya.online';
const SITE = 'https://arkya.online';
const KEY = 'arkya-session-v1';
const PENDING_KEY = 'arkya-discord-pending-v1';
const RETURN_URL = `${SITE}/account.html?lang=en&returnTo=play.html`;
const START = Date.parse('2026-10-16T12:00:00Z');
const ID = '197d1fa3-83c2-45cb-bb26-f4b574e88ee8';
const CHALLENGE_ID = '5f91daa3df9047cb8a893f577bf50892';
const TOKEN = 'S'.repeat(43);
const NEXT_TOKEN = 'N'.repeat(43);

function memoryStorage(initial = {}) {
  const data = new Map(Object.entries(initial));
  return { getItem: (key) => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: (key) => data.delete(key), data };
}

function json(value, status = 200, headers = {}) {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json', ...headers } });
}

function challenge(overrides = {}) {
  return { challengeId: CHALLENGE_ID, authorizationUrl: `${API}/v1/auth/discord/authorize/${CHALLENGE_ID}`,
    expiresAt: new Date(START + 300000).toISOString(), pollIntervalSeconds: 5, ...overrides };
}

function account(overrides = {}) {
  return { accountId: ID, displayName: 'Explorateur', sessionToken: TOKEN,
    expiresAt: new Date(START + 3600000).toISOString(), ...overrides };
}

function stored(overrides = {}) {
  return JSON.stringify({ version: 1, apiOrigin: API, ...account(), ...overrides });
}

function fixture({ responses = [], storage = memoryStorage(), ...options } = {}) {
  let time = START;
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    const next = responses.shift();
    if (typeof next === 'function') return next(url, init);
    if (next instanceof Error) throw next;
    assert.ok(next, `Unexpected request: ${url}`);
    return next;
  };
  const client = createAuthClient({ baseUrl: API, siteOrigin: SITE, cryptoImpl: webcrypto,
    now: () => time, storage, fetchImpl, locks: null, ...options });
  return { client, calls, storage, advance: (ms) => { time += ms; } };
}

test('refresh restores a new tab with an HttpOnly cookie and never exposes a refresh secret', async () => {
  const { client, calls, storage } = fixture({ responses: [json(account({ sessionToken: NEXT_TOKEN, refreshExpiresAt: '2026-11-15T12:00:00Z' }))] });
  assert.equal((await client.restore()).accountId, ID);
  assert.equal(calls[0].url, `${API}/v1/auth/refresh`);
  assert.equal(calls[0].init.credentials, 'include');
  assert.equal(calls[0].init.headers['X-Arkya-Refresh'], '1');
  assert.equal(calls[0].init.headers.Authorization, undefined);
  assert.deepEqual(JSON.parse(calls[0].init.body), {});
  assert.equal('refreshToken' in JSON.parse(storage.getItem(KEY)), false);
  assert.equal('sessionToken' in client.session(), false);
});

test('access about to expire is rotated once for concurrent restore requests', async () => {
  let finish;
  const { client, calls, storage } = fixture({ storage: memoryStorage({ [KEY]: stored({ expiresAt: new Date(START + 30000).toISOString() }) }),
    responses: [() => new Promise(resolve => { finish = resolve; })] });
  const first = client.restore();
  const second = client.restore();
  assert.equal(calls.length, 1);
  finish(json(account({ sessionToken: NEXT_TOKEN })));
  const result = await Promise.all([first, second]);
  assert.deepEqual(result[0], result[1]);
  assert.equal(JSON.parse(storage.getItem(KEY)).sessionToken, NEXT_TOKEN);
  assert.equal(calls[0].url, `${API}/v1/auth/refresh`);
});

test('refresh serializes shared cookie mutations with a browser lock', async () => {
  const names = [];
  const locks = { async request(name, action) { names.push(name); return action(); } };
  const { client } = fixture({ locks, responses: [json(account()), new Response(null, { status: 204 })] });
  await client.restore();
  await client.logout();
  assert.deepEqual(names, [`arkya-refresh:${API}`, `arkya-refresh:${API}`]);
});

test('a rejected access token can recover through refresh, but a different account is refused', async () => {
  for (const accountId of [ID, '297d1fa3-83c2-45cb-bb26-f4b574e88ee8']) {
    const { client, calls } = fixture({ storage: memoryStorage({ [KEY]: stored() }),
      responses: [new Response(null, { status: 401 }), json(account({ sessionToken: NEXT_TOKEN, accountId }))] });
    if (accountId === ID) assert.equal((await client.restore()).accountId, ID);
    else { await assert.rejects(client.restore(), { code: 'invalid_response' }); assert.equal(client.session(), null); }
    assert.equal(calls.length, 2);
  }
});

test('logout revokes the cookie even after access storage has expired or disappeared', async () => {
  const { client, calls } = fixture({ responses: [new Response(null, { status: 204 })] });
  assert.deepEqual(await client.logout(), { revoked: true });
  assert.equal(calls[0].init.credentials, 'include');
  assert.equal(calls[0].init.headers['X-Arkya-Refresh'], '1');
  assert.equal(calls[0].init.headers.Authorization, undefined);
});

test('logout wins over a late refresh response', async () => {
  let finish;
  const { client } = fixture({ responses: [() => new Promise(resolve => { finish = resolve; }), new Response(null, { status: 204 })] });
  const refreshing = client.restore();
  await client.logout();
  finish(json(account()));
  await assert.rejects(refreshing, { code: 'cancelled' });
  assert.equal(client.session(), null);
});

test('logout aborts refresh before acquiring the shared cookie lock', { timeout: 5000 }, async () => {
  let queued = Promise.resolve();
  const serialLocks = { request(_name, action) {
    const result = queued.then(action);
    queued = result.catch(() => {});
    return result;
  } };
  for (const locks of [serialLocks, globalThis.navigator?.locks].filter(Boolean)) {
    let started;
    let refreshSignal;
    const requestStarted = new Promise(resolve => { started = resolve; });
    const { client, calls } = fixture({ locks, responses: [(_url, { signal }) => new Promise((_resolve, reject) => {
      refreshSignal = signal;
      signal.addEventListener('abort', () => reject(signal.reason), { once: true });
      started();
    }), new Response(null, { status: 204 })] });
    const refreshing = assert.rejects(client.restore(), { code: 'network' });
    await requestStarted;
    assert.deepEqual(await client.logout(), { revoked: true });
    await refreshing;
    assert.equal(refreshSignal.aborted, true);
    assert.deepEqual(calls.map(({ url }) => url), [`${API}/v1/auth/refresh`, `${API}/v1/auth/logout`]);
    assert.equal(client.session(), null);
  }
});

test('refresh outages do not claim authentication and can be retried', async () => {
  const { client } = fixture({ responses: [new Response(null, { status: 503 }), json(account())] });
  await assert.rejects(client.restore(), { code: 'network' });
  assert.equal(client.session(), null);
  assert.equal((await client.restore()).accountId, ID);
});

test('public configuration only accepts a safe HTTPS origin', () => {
  const bad = [undefined, '', 'no url', 'http://identity.arkya.online', 'https://user:pass@identity.arkya.online',
    `${API}/prefix`, `${API}?`, `${API}#`, `${API}/?x=y`, `${API}/#a`, ` ${API}`, `${API}\\foo`,
    'https://localhost:7141', 'https://localhost.:7141', 'https://login.localhost',
    'https://127.0.0.1', 'https://2130706433', 'https://0x7f000001', 'https://10.1.2.3', 'https://172.16.1.1',
    'https://172.31.255.255', 'https://192.168.1.1', 'https://169.254.169.254', 'https://100.64.0.1',
    'https://[::1]', 'https://[fd00::1]', 'https://[fe80::1]', 'https://[::ffff:127.0.0.1]'];
  for (const baseUrl of bad) assert.equal(fixture({ baseUrl }).client.available, false, String(baseUrl));
  for (const baseUrl of [API, `${API}/`, 'https://identity.arkya.online:8443', 'https://203.0.113.10']) {
    assert.equal(fixture({ baseUrl }).client.available, true, baseUrl);
  }
});

test('development permits local Identity only from a loopback site', () => {
  for (const baseUrl of ['https://localhost:7141', 'http://127.0.0.1:7141', 'http://[::1]:7141']) {
    assert.equal(fixture({ baseUrl, siteOrigin: 'http://127.0.0.1:51806' }).client.available, true);
  }
  assert.equal(fixture({ baseUrl: 'http://identity.arkya.online', siteOrigin: 'http://localhost:51806' }).client.available, false);
  assert.equal(fixture({ baseUrl: 'https://localhost:7141', siteOrigin: 'https://127.evil.test' }).client.available, false);
  assert.equal(fixture({ baseUrl: 'http://127.evil.test', siteOrigin: 'http://localhost:51806' }).client.available, false);
});

test('unconfigured client fails closed without a request', async () => {
  const { client, calls } = fixture({ baseUrl: '' });
  assert.equal(await client.restore(), null);
  assert.equal(client.session(), null);
  await assert.rejects(client.begin(), { code: 'unavailable' });
  await assert.rejects(client.poll(), { code: 'unavailable' });
  assert.deepEqual(await client.logout(), { revoked: false });
  assert.equal(calls.length, 0);
});

test('S256 proof is random, bound to its verifier and not in public return values or storage', async () => {
  const { client, calls, storage, advance } = fixture({ responses: [json(challenge()), json({ status: 'pending' }, 202)] });
  const started = await client.begin();
  assert.deepEqual(Object.keys(started).sort(), ['authorizationUrl', 'expiresAt', 'pollIntervalSeconds']);
  assert.deepEqual(await client.poll(), { status: 'pending', pollIntervalSeconds: 5 });
  assert.equal(calls.length, 1);
  advance(5000);
  await client.poll();
  const proof = JSON.parse(calls[0].init.body);
  const exchange = JSON.parse(calls[1].init.body);
  assert.equal(Object.keys(proof).join(), 'codeChallenge');
  assert.match(exchange.codeVerifier, /^[A-Za-z0-9_-]{43}$/);
  assert.equal(Buffer.from(exchange.codeVerifier, 'base64url').length, 32);
  assert.equal(proof.codeChallenge, createHash('sha256').update(exchange.codeVerifier).digest('base64url'));
  assert.equal(exchange.challengeId, CHALLENGE_ID);
  assert.equal(storage.data.size, 0);
  assert.ok(!JSON.stringify(started).includes(exchange.codeVerifier));
  assert.equal(client.session(), null);
});

test('requests omit cookies/referrer, disable redirects and caching, and include cancellation', async () => {
  const { client, calls } = fixture({ responses: [json(challenge())] });
  await client.begin();
  const { url, init } = calls[0];
  assert.equal(url, `${API}/v1/auth/discord/challenges`);
  assert.equal(init.method, 'POST');
  assert.equal(init.credentials, 'omit');
  assert.equal(init.redirect, 'error');
  assert.equal(init.cache, 'no-store');
  assert.equal(init.referrerPolicy, 'no-referrer');
  assert.equal(init.referrer, '');
  assert.ok(init.signal instanceof AbortSignal);
  assert.equal(init.headers.Authorization, undefined);
});

test('challenge validation rejects foreign and disguised authorization URLs', async () => {
  const urls = [`https://evil.test/v1/auth/discord/authorize/${CHALLENGE_ID}`,
    `${API}/v1/auth/discord/callback`, `${API}/v1/auth/discord/authorize/other`,
    `${API}/v1/auth/discord/authorize/${CHALLENGE_ID}?next=evil`, `${API}/v1/auth/discord/authorize/${CHALLENGE_ID}#evil`,
    `https://name@identity.arkya.online/v1/auth/discord/authorize/${CHALLENGE_ID}`,
    `${API}/v1/auth/discord/authorize/../authorize/${CHALLENGE_ID}`];
  for (const authorizationUrl of urls) {
    const { client } = fixture({ responses: [json(challenge({ authorizationUrl }))] });
    await assert.rejects(client.begin(), { code: 'invalid_response' });
    assert.equal((await client.poll()).status, 'expired');
  }
});

test('challenge validation rejects malformed IDs, interval and excessive or expired lifetime', async () => {
  for (const overrides of [{ challengeId: '../x' }, { challengeId: null }, { challengeId: [CHALLENGE_ID] }, { pollIntervalSeconds: 0 },
    { pollIntervalSeconds: 4 }, { pollIntervalSeconds: '5' }, { pollIntervalSeconds: 5.5 },
    { expiresAt: 'bad' }, { expiresAt: START + 1000 }, { expiresAt: new Date(START).toISOString() },
    { expiresAt: new Date(START + 331000).toISOString() }]) {
    const { client } = fixture({ responses: [json(challenge(overrides))] });
    await assert.rejects(client.begin(), { code: 'invalid_response' });
  }
});

test('polling respects intervals and Retry-After without sending early requests', async () => {
  const { client, calls, advance } = fixture({ responses: [json(challenge()), json({ status: 'pending' }, 202, { 'Retry-After': '10' }), json(account())] });
  await client.begin();
  advance(4999);
  assert.equal((await client.poll()).status, 'pending');
  assert.equal(calls.length, 1);
  advance(1);
  assert.deepEqual(await client.poll(), { status: 'pending', pollIntervalSeconds: 10 });
  advance(9999);
  assert.equal((await client.poll()).status, 'pending');
  assert.equal(calls.length, 2);
  advance(1);
  assert.equal((await client.poll()).status, 'authenticated');
  assert.equal(calls.length, 3);
});

test('a successful exchange stores only the Arkya session with origin and expiry', async () => {
  const { client, storage, advance } = fixture({ responses: [json(challenge()), json({ ...account(), accessToken: 'never-copy-this-discord-token' })] });
  await client.begin();
  advance(5000);
  const result = await client.poll();
  assert.equal(result.status, 'authenticated');
  assert.deepEqual(result.session, { accountId: ID, displayName: 'Explorateur', expiresAt: account().expiresAt });
  assert.deepEqual(client.session(), result.session);
  assert.ok(!JSON.stringify(result).includes(TOKEN));
  assert.ok(!JSON.stringify(client.session()).includes('Token'));
  const saved = JSON.parse(storage.getItem(KEY));
  assert.deepEqual(saved, JSON.parse(stored()));
  assert.ok(!storage.getItem(KEY).includes('never-copy-this'));
  result.session.displayName = 'modified externally';
  assert.equal(client.session().displayName, 'Explorateur');
});

test('expired challenges stop polling locally and server 410 becomes an expired result', async () => {
  const local = fixture({ responses: [json(challenge())] });
  await local.client.begin();
  local.advance(300000);
  assert.equal((await local.client.poll()).status, 'expired');
  assert.equal(local.calls.length, 1);
  const remote = fixture({ responses: [json(challenge()), json({ code: 'challenge_expired_or_consumed' }, 410)] });
  await remote.client.begin();
  remote.advance(5000);
  assert.equal((await remote.client.poll()).status, 'expired');
  assert.equal(remote.client.session(), null);
});

test('bad proof and nonpending 202 fail closed and expose no response details', async () => {
  for (const response of [json({ code: 'private token from server' }, 400), json({ status: 'completed' }, 202)]) {
    const { client, advance } = fixture({ responses: [json(challenge()), response] });
    await client.begin();
    advance(5000);
    await assert.rejects(client.poll(), (error) => {
      assert.ok(['rejected', 'invalid_response'].includes(error.code));
      assert.ok(!error.message.includes('private token'));
      return true;
    });
    assert.equal(client.session(), null);
    assert.equal((await client.poll()).status, 'expired');
  }
});

test('exchange rejects malformed sessions and lifetimes above eight hours', async () => {
  for (const overrides of [{ accountId: 'discord-user-id' }, { accountId: null }, { accountId: [ID] }, { displayName: '' },
    { displayName: '\nprivate' }, { displayName: 'X'.repeat(129) }, { sessionToken: 'short' }, { sessionToken: [TOKEN] },
    { expiresAt: new Date(START + 5000).toISOString() }, { expiresAt: new Date(START + 8 * 3600000 + 6000).toISOString() }]) {
    const { client, storage, advance } = fixture({ responses: [json(challenge()), json(account(overrides))] });
    await client.begin();
    advance(5000);
    await assert.rejects(client.poll(), { code: 'invalid_response' });
    assert.equal(client.session(), null);
    assert.equal(storage.getItem(KEY), null);
  }
});

test('restore verifies a stored bearer with /me before exposing the account', async () => {
  const storage = memoryStorage({ [KEY]: stored() });
  const { client, calls } = fixture({ storage, responses: [json(account({ displayName: 'Nouveau nom' }))] });
  assert.equal(client.session(), null);
  const result = await client.restore();
  assert.equal(result.displayName, 'Nouveau nom');
  assert.equal(calls[0].url, `${API}/v1/auth/me`);
  assert.equal(calls[0].init.headers.Authorization, `Bearer ${TOKEN}`);
  assert.equal(calls[0].init.method, 'GET');
  assert.ok(!JSON.stringify(result).includes(TOKEN));
});

test('restore discards unsafe access storage before trying the origin-bound refresh cookie', async () => {
  for (const value of ['{', 'null', stored({ apiOrigin: 'https://evil.test' }), stored({ version: 2 }),
    stored({ expiresAt: new Date(START).toISOString() }), stored({ sessionToken: 'wrong' }),
    stored({ expiresAt: new Date(START + 9 * 3600000).toISOString() })]) {
    const { client, calls, storage } = fixture({ storage: memoryStorage({ [KEY]: value }), responses: [new Response(null, { status: 401 })] });
    assert.equal(await client.restore(), null);
    assert.equal(client.session(), null);
    assert.equal(calls.length, 1);
    assert.equal(calls[0].url, `${API}/v1/auth/refresh`);
    assert.equal(calls[0].init.headers.Authorization, undefined);
    if (value !== 'null') assert.equal(storage.getItem(KEY), null);
  }
});

test('restore clears a rejected session and account mismatches', async () => {
  const rejected = fixture({ storage: memoryStorage({ [KEY]: stored() }), responses: [new Response(null, { status: 401 }), new Response(null, { status: 401 })] });
  assert.equal(await rejected.client.restore(), null);
  assert.equal(rejected.storage.getItem(KEY), null);
  const mismatch = fixture({ storage: memoryStorage({ [KEY]: stored() }), responses: [json(account({ accountId: '297d1fa3-83c2-45cb-bb26-f4b574e88ee8' }))] });
  await assert.rejects(mismatch.client.restore(), { code: 'invalid_response' });
  assert.equal(mismatch.client.session(), null);
  assert.equal(mismatch.storage.getItem(KEY), null);
});

test('restore does not allow the server response to extend a saved session', async () => {
  const { client, storage } = fixture({ storage: memoryStorage({ [KEY]: stored() }),
    responses: [json(account({ expiresAt: new Date(START + 7200000).toISOString() }))] });
  await assert.rejects(client.restore(), { code: 'invalid_response' });
  assert.equal(client.session(), null);
  assert.equal(storage.getItem(KEY), null);
});

test('a failed restore keeps the credential for retry but never claims authentication', async () => {
  const { client, storage } = fixture({ storage: memoryStorage({ [KEY]: stored() }), responses: [new Error('secret network data'), json(account())] });
  await assert.rejects(client.restore(), { code: 'network', message: 'network' });
  assert.equal(client.session(), null);
  assert.ok(storage.getItem(KEY));
  assert.equal((await client.restore()).accountId, ID);
});

test('blocked browser storage falls back to memory, including revocation', async () => {
  const blocked = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); }, removeItem() { throw new Error('blocked'); } };
  const { client, advance } = fixture({ storage: blocked, responses: [new Response(null, { status: 401 }), json(challenge()), json(account()), new Response(null, { status: 204 })] });
  assert.equal(await client.restore(), null);
  await client.begin();
  advance(5000);
  assert.equal((await client.poll()).status, 'authenticated');
  assert.equal(client.session().accountId, ID);
  assert.deepEqual(await client.logout(), { revoked: true });
  assert.equal(client.session(), null);
});

test('logout revokes the bearer and always clears the local session', async () => {
  for (const response of [new Response(null, { status: 204 }), new Response(null, { status: 401 }), new Error('offline')]) {
    const { client, calls, storage } = fixture({ storage: memoryStorage({ [KEY]: stored() }), responses: [json(account()), response] });
    await client.restore();
    const result = await client.logout();
    assert.equal(result.revoked, !(response instanceof Error));
    assert.equal(client.session(), null);
    assert.equal(storage.getItem(KEY), null);
    assert.equal(calls[1].url, `${API}/v1/auth/logout`);
    assert.equal(calls[1].init.method, 'POST');
    assert.equal(calls[1].init.headers.Authorization, `Bearer ${TOKEN}`);
    assert.equal(calls[1].init.body, undefined);
  }
});

test('session expiry is checked on every public read', async () => {
  const { client, storage, advance } = fixture({ storage: memoryStorage({ [KEY]: stored() }), responses: [json(account())] });
  await client.restore();
  advance(3600000);
  assert.equal(client.session(), null);
  assert.equal(storage.getItem(KEY), null);
});

test('cancel leaves an authenticated session intact and discards a pending proof', async () => {
  const { client, calls } = fixture({ storage: memoryStorage({ [KEY]: stored() }), responses: [json(account()), json(challenge())] });
  await client.restore();
  await client.begin();
  client.cancel();
  assert.equal(client.session().accountId, ID);
  assert.equal((await client.poll()).status, 'expired');
  assert.equal(calls.length, 2);
});

test('concurrent polls share one exchange and cancellation ignores a late reply', async () => {
  let finish;
  const { client, calls, advance, storage } = fixture({ responses: [json(challenge()), () => new Promise((resolve) => { finish = resolve; })] });
  await client.begin();
  advance(5000);
  const first = client.poll();
  const second = client.poll();
  assert.equal(calls.length, 2);
  client.cancel();
  assert.equal(calls[1].init.signal.aborted, true);
  finish(json(account()));
  await assert.rejects(first, { code: 'cancelled' });
  await assert.rejects(second, { code: 'cancelled' });
  assert.equal(client.session(), null);
  assert.equal(storage.getItem(KEY), null);
});

test('logout cannot be undone by an in-flight restore', async () => {
  let finish;
  const { client, storage } = fixture({ storage: memoryStorage({ [KEY]: stored() }),
    responses: [() => new Promise((resolve) => { finish = resolve; }), new Response(null, { status: 204 })] });
  const restoring = client.restore();
  assert.deepEqual(await client.logout(), { revoked: true });
  finish(json(account()));
  await assert.rejects(restoring, { code: 'cancelled' });
  assert.equal(client.session(), null);
  assert.equal(storage.getItem(KEY), null);
});

test('malformed JSON and response redirects are rejected without accepting a challenge', async () => {
  const redirected = json(challenge());
  Object.defineProperty(redirected, 'redirected', { value: true });
  const wrongEndpoint = json(challenge());
  Object.defineProperty(wrongEndpoint, 'url', { value: `${API}/unexpected` });
  for (const response of [redirected, wrongEndpoint, new Response('not JSON', { headers: { 'Content-Type': 'application/json' } }), new Response('<h1>login</h1>')]) {
    const { client } = fixture({ responses: [response] });
    await assert.rejects(client.begin(), { code: 'invalid_response' });
    assert.equal(client.session(), null);
  }
});

test('a transient exchange error can be retried after the minimum interval', async () => {
  const { client, advance, calls } = fixture({ responses: [json(challenge()), new Error('offline'), json(account())] });
  await client.begin();
  advance(5000);
  await assert.rejects(client.poll(), { code: 'network' });
  assert.equal((await client.poll()).status, 'pending');
  assert.equal(calls.length, 2);
  advance(5000);
  assert.equal((await client.poll()).status, 'authenticated');
});

test('same-tab login retains the proof through a reload, exchanges immediately and verifies /me', async () => {
  const storage = memoryStorage();
  const first = fixture({ storage, responses: [json(challenge({ returnUrl: RETURN_URL }))] });
  const result = await first.client.begin({ returnUrl: RETURN_URL });
  const saved = JSON.parse(storage.getItem(PENDING_KEY));
  assert.equal(saved.apiOrigin, API);
  assert.equal(saved.returnUrl, RETURN_URL);
  assert.equal(saved.expires - saved.createdAt, 300000);
  assert.equal(JSON.parse(first.calls[0].init.body).returnUrl, RETURN_URL);
  assert.equal(JSON.parse(first.calls[0].init.body).codeChallenge, createHash('sha256').update(saved.codeVerifier).digest('base64url'));
  assert.ok(!JSON.stringify(result).includes(saved.codeVerifier));
  assert.ok(!RETURN_URL.includes(saved.challengeId));
  assert.ok(!result.authorizationUrl.includes(saved.codeVerifier));

  let finishIdentity;
  const returned = fixture({ storage, responses: [json(account()), () => new Promise(resolve => { finishIdentity = resolve; })] });
  assert.deepEqual(returned.client.resume({ returnUrl: RETURN_URL }), {
    expiresAt: challenge().expiresAt, pollIntervalSeconds: 5,
  });
  const completing = returned.client.poll();
  while (!finishIdentity) await new Promise(resolve => setImmediate(resolve));
  assert.equal(returned.calls[0].url, `${API}/v1/auth/discord/exchange`);
  assert.equal(JSON.parse(returned.calls[0].init.body).codeVerifier, saved.codeVerifier);
  assert.equal(returned.calls[1].url, `${API}/v1/auth/me`);
  assert.equal(returned.calls[1].init.headers.Authorization, `Bearer ${TOKEN}`);
  assert.equal(returned.client.session(), null);
  assert.equal(storage.getItem(KEY), null);
  finishIdentity(json(account()));
  assert.equal((await completing).status, 'authenticated');
  assert.equal(returned.client.session().accountId, ID);
  assert.equal(storage.getItem(PENDING_KEY), null);
  assert.equal(returned.client.resume({ returnUrl: RETURN_URL }), null);
  assert.ok(!JSON.stringify(returned.calls.map(call => call.url)).includes(saved.codeVerifier));
  assert.ok(!JSON.stringify(returned.calls.map(call => call.url)).includes(TOKEN));
});

test('same-tab login requires writable session storage before requesting a challenge', async () => {
  for (const storage of [null, { getItem() { throw Error(); }, setItem() { throw Error(); }, removeItem() {} },
    { getItem() { return null; }, setItem() {}, removeItem() {} }]) {
    const { client, calls } = fixture({ storage });
    await assert.rejects(client.begin({ returnUrl: RETURN_URL }), { code: 'storage_unavailable' });
    assert.equal(calls.length, 0);
    assert.equal(client.session(), null);
  }
});

test('suspension retains a web proof and preserves server polling delays across reloads', async () => {
  const first = fixture({ responses: [json(challenge({ returnUrl: RETURN_URL })),
    json({ status: 'pending' }, 202, { 'Retry-After': '10' })] });
  await first.client.begin({ returnUrl: RETURN_URL });
  first.client.suspend();
  assert.ok(first.storage.getItem(PENDING_KEY));
  first.client.resume({ returnUrl: RETURN_URL });
  assert.deepEqual(await first.client.poll(), { status: 'pending', pollIntervalSeconds: 10 });
  first.client.suspend();
  const returned = fixture({ storage: first.storage, responses: [json(account()), json(account())] });
  returned.client.resume({ returnUrl: RETURN_URL });
  assert.deepEqual(await returned.client.poll(), { status: 'pending', pollIntervalSeconds: 10 });
  returned.advance(9999);
  assert.equal((await returned.client.poll()).status, 'pending');
  assert.equal(returned.calls.length, 0);
  returned.advance(1);
  assert.equal((await returned.client.poll()).status, 'authenticated');
  assert.equal(returned.calls.length, 2);
});

test('same-tab login refuses old servers that do not confirm the exact return URL', async () => {
  for (const returnUrl of [undefined, 'https://evil.example/account.html?lang=en', `${SITE}/account.html?lang=fr`]) {
    const { client, storage } = fixture({ responses: [json(challenge({ returnUrl }))] });
    await assert.rejects(client.begin({ returnUrl: RETURN_URL }), { code: 'return_unavailable' });
    assert.equal(storage.getItem(PENDING_KEY), null);
    assert.equal((await client.poll()).status, 'expired');
  }
});

test('same-tab return URL is restricted to this site account page and safe language/game parameters', async () => {
  for (const returnUrl of ['https://evil.example/account.html?lang=en', `${SITE}/play.html?lang=en`,
    `${SITE}/account.html?lang=de`, `${SITE}/account.html?lang=en&lang=fr`,
    `${SITE}/account.html?lang=en&returnTo=https://evil.example`, `${SITE}/account.html?lang=en#fragment`,
    `${SITE}/account.html?lang=en&code=secret`, `${SITE}/account.html?lang=en&returnTo=play.html&returnTo=play.html`,
    `${SITE}/account.html?lang=en&sessionToken=secret`]) {
    const { client, calls } = fixture();
    await assert.rejects(client.begin({ returnUrl }), { code: 'invalid_response' });
    assert.equal(calls.length, 0);
  }
  for (const returnUrl of [RETURN_URL, `${SITE}/account.html?lang=fr`]) {
    const { client } = fixture({ responses: [json(challenge({ returnUrl }))] });
    assert.ok((await client.begin({ returnUrl })).authorizationUrl);
  }
});

test('resume rejects and removes expired, corrupt, foreign-origin and wrong-return proofs', async () => {
  const first = fixture({ responses: [json(challenge({ returnUrl: RETURN_URL }))] });
  await first.client.begin({ returnUrl: RETURN_URL });
  const valid = JSON.parse(first.storage.getItem(PENDING_KEY));
  const invalid = ['{', 'null', [], { ...valid, apiOrigin: 'https://evil.example' },
    { ...valid, returnUrl: `${SITE}/account.html?lang=fr` }, { ...valid, version: 2 },
    { ...valid, codeVerifier: 'short' }, { ...valid, challengeId: '../x' }, { ...valid, interval: 0 },
    { ...valid, createdAt: START + 1 }, { ...valid, expires: START + 300001 },
    { ...valid, expires: START - 1 }];
  for (const value of invalid) {
    const storage = memoryStorage({ [PENDING_KEY]: typeof value === 'string' ? value : JSON.stringify(value) });
    const { client, calls } = fixture({ storage });
    assert.throws(() => client.resume({ returnUrl: RETURN_URL }), { code: 'invalid_response' });
    assert.equal(storage.getItem(PENDING_KEY), null);
    assert.equal(calls.length, 0);
  }
  const expired = fixture({ storage: first.storage });
  expired.advance(300000);
  assert.throws(() => expired.client.resume({ returnUrl: RETURN_URL }), { code: 'expired' });
  assert.equal(first.storage.getItem(PENDING_KEY), null);
});

test('web proof is removed after cancellation, denial, malformed reply, or exchange failure', async () => {
  for (const response of [json({}, 410), json({}, 400), json({ status: 'wrong' }, 202), new Error('offline')]) {
    const { client, storage } = fixture({ responses: [json(challenge({ returnUrl: RETURN_URL })), response] });
    await client.begin({ returnUrl: RETURN_URL });
    client.resume({ returnUrl: RETURN_URL });
    try { await client.poll(); } catch {}
    assert.equal(storage.getItem(PENDING_KEY), null);
    assert.equal(client.session(), null);
  }
  const { client, storage } = fixture({ responses: [json(challenge({ returnUrl: RETURN_URL }))] });
  await client.begin({ returnUrl: RETURN_URL });
  client.cancel();
  assert.equal(storage.getItem(PENDING_KEY), null);
  assert.equal(client.resume({ returnUrl: RETURN_URL }), null);
});

test('web session is never exposed when /me rejects it, disagrees, fails or completes after cancellation', async () => {
  for (const response of [json({}, 401), json(account({ accountId: '297d1fa3-83c2-45cb-bb26-f4b574e88ee8' })),
    json(account({ expiresAt: new Date(START + 7200000).toISOString() })), new Error('offline')]) {
    const { client, storage } = fixture({ responses: [json(challenge({ returnUrl: RETURN_URL })), json(account()), response] });
    await client.begin({ returnUrl: RETURN_URL });
    client.resume({ returnUrl: RETURN_URL });
    await assert.rejects(client.poll());
    assert.equal(client.session(), null);
    assert.equal(storage.getItem(PENDING_KEY), null);
    assert.equal(storage.getItem(KEY), null);
  }
  let finish;
  const { client, storage } = fixture({ responses: [json(challenge({ returnUrl: RETURN_URL })), json(account()),
    () => new Promise(resolve => { finish = resolve; })] });
  await client.begin({ returnUrl: RETURN_URL });
  client.resume({ returnUrl: RETURN_URL });
  const completing = client.poll();
  while (!finish) await new Promise(resolve => setImmediate(resolve));
  client.cancel();
  finish(json(account()));
  await assert.rejects(completing, { code: 'cancelled' });
  assert.equal(client.session(), null);
  assert.equal(storage.getItem(PENDING_KEY), null);
  assert.equal(storage.getItem(KEY), null);
});
