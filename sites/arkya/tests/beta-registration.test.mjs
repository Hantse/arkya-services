import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { createAuthClient } from '../dist/auth-client.mjs';
import { accountReturnPath, gameReturnPath } from '../dist/game-navigation.mjs';

const API = 'https://api.arkya.online';
const SITE = 'https://arkya.online';
const START = Date.parse('2026-10-24T12:00:00Z');
const TOKEN = 'B'.repeat(43);
const user = { accountId: '197d1fa3-83c2-45cb-bb26-f4b574e88ee8', displayName: 'Beta Explorer',
  expiresAt: new Date(START + 3600000).toISOString() };
const pending = { registered: true, registeredAt: '2026-10-24T11:00:00+02:00', hasBetaAccess: false };
const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });

function fixture({ responses = [], authenticated = true } = {}) {
  const values = new Map(authenticated ? [['arkya-session-v1', JSON.stringify({ version: 1, apiOrigin: API, ...user, sessionToken: TOKEN })]] : []);
  const calls = [];
  const client = createAuthClient({ baseUrl: API, siteOrigin: SITE, cryptoImpl: webcrypto, now: () => START, locks: null,
    storage: { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) },
    fetchImpl: async (url, init) => {
      calls.push({ url, init });
      const next = responses.shift();
      assert.ok(next, `Unexpected request: ${url}`);
      if (typeof next === 'function') return next(url, init);
      if (next instanceof Error) throw next;
      return next;
    } });
  return { client, calls, values };
}

test('checking beta registration verifies the account and never writes a registration', async () => {
  const { client, calls } = fixture({ responses: [json(user), json({ registered: false, hasBetaAccess: false })] });
  assert.deepEqual(await client.betaRegistration(), { registered: false, registeredAt: null, hasBetaAccess: false });
  assert.deepEqual(calls.map(call => [call.url, call.init.method]), [[`${API}/v1/auth/me`, 'GET'], [`${API}/v1/beta/registration`, 'GET']]);
  const { init } = calls[1];
  assert.equal(init.headers.Authorization, `Bearer ${TOKEN}`);
  assert.equal(init.credentials, 'omit');
  assert.equal(init.redirect, 'error');
  assert.equal(init.cache, 'no-store');
  assert.equal(init.body, undefined);
  assert.equal('sessionToken' in client.session(), false);
});

test('explicit beta registration posts an empty body and retains the server registration date', async () => {
  const { client, calls } = fixture({ responses: [json(user), json(pending)] });
  assert.deepEqual(await client.betaRegistration({ register: true }), { ...pending, registeredAt: '2026-10-24T09:00:00.000Z' });
  assert.equal(calls[1].init.method, 'POST');
  assert.deepEqual(JSON.parse(calls[1].init.body), {});
  assert.equal(calls[1].init.headers['Content-Type'], 'application/json');
});

test('a verified live session is reused while each beta request is authorized by the server', async () => {
  const { client, calls } = fixture({ responses: [json(user), json({ registered: false, registeredAt: null, hasBetaAccess: false }), json(pending)] });
  await client.restore();
  await client.betaRegistration();
  await client.betaRegistration({ register: true });
  assert.deepEqual(calls.map(call => call.url), [`${API}/v1/auth/me`, `${API}/v1/beta/registration`, `${API}/v1/beta/registration`]);
});

test('already granted access is displayed independently of a registration record', async () => {
  for (const registration of [{ registered: false, registeredAt: null, hasBetaAccess: true }, { ...pending, hasBetaAccess: true }]) {
    const { client } = fixture({ responses: [json(user), json(registration)] });
    assert.equal((await client.betaRegistration()).hasBetaAccess, true);
  }
});

test('anonymous and rejected accounts cannot send a beta registration', async () => {
  const { client, calls } = fixture({ authenticated: false, responses: [json({}, 401)] });
  await assert.rejects(client.betaRegistration({ register: true }), { code: 'unauthenticated' });
  assert.deepEqual(calls.map(call => call.url), [`${API}/v1/auth/refresh`]);
});

test('a revoked session at the beta endpoint clears local access', async () => {
  const { client, values } = fixture({ responses: [json(user), json({}, 401)] });
  await assert.rejects(client.betaRegistration(), { code: 'unauthenticated' });
  assert.equal(client.session(), null);
  assert.equal(values.has('arkya-session-v1'), false);
});

test('transient beta failures preserve the verified account and do not imply registration', async () => {
  for (const response of [json({}, 503), json({}, 429), new Error('offline')]) {
    const { client } = fixture({ responses: [json(user), response] });
    await assert.rejects(client.betaRegistration({ register: true }), { code: 'network' });
    assert.equal(client.session().accountId, user.accountId);
  }
});

test('malformed beta data cannot become a success state', async () => {
  const values = [null, [], {}, { ...pending, registered: 'true' }, { ...pending, hasBetaAccess: 1 },
    { ...pending, registeredAt: null }, { ...pending, registeredAt: 'yesterday' },
    { ...pending, registeredAt: '2026-10-24T11:00:00' }, { ...pending, registeredAt: '2026-99-99T11:00:00Z' },
    { registered: false, registeredAt: pending.registeredAt, hasBetaAccess: false }];
  for (const value of values) {
    const { client } = fixture({ responses: [json(user), json(value)] });
    await assert.rejects(client.betaRegistration(), { code: 'invalid_response' });
  }
});

test('logout prevents an in-flight beta result from being accepted', async () => {
  let finish;
  const { client } = fixture({ responses: [json(user), () => new Promise(resolve => { finish = resolve; }), new Response(null, { status: 204 })] });
  const request = client.betaRegistration({ register: true });
  while (!finish) await new Promise(resolve => setImmediate(resolve));
  await client.logout();
  finish(json(pending));
  await assert.rejects(request, { code: 'cancelled' });
  assert.equal(client.session(), null);
});

test('account return paths allow only exact beta or launcher destinations and preserve language', () => {
  assert.equal(accountReturnPath('?returnTo=beta.html', 'en'), 'beta.html?lang=en');
  assert.equal(accountReturnPath('?returnTo=beta.html', 'fr'), 'beta.html?lang=fr');
  assert.equal(accountReturnPath('?returnTo=play.html', 'en'), 'play.html?lang=en');
  assert.equal(gameReturnPath('?returnTo=beta.html'), null);
  for (const value of ['', 'https://evil.example', '//evil.example/beta.html', '/beta.html', '../beta.html',
    'beta.html?register=true', 'beta.html#fragment', 'BETA.HTML', 'javascript:alert(1)']) {
    assert.equal(accountReturnPath(`?returnTo=${encodeURIComponent(value)}`), null, value);
  }
  assert.equal(accountReturnPath('?returnTo=beta.html&returnTo=play.html'), null);
});

test('Discord allows beta return only through the trusted account callback', async () => {
  const returnUrl = `${SITE}/account.html?lang=fr&returnTo=beta.html`;
  const { client, calls } = fixture({ responses: [json({ challengeId: 'a'.repeat(32),
    authorizationUrl: `${API}/v1/auth/discord/authorize/${'a'.repeat(32)}`,
    expiresAt: new Date(START + 300000).toISOString(), pollIntervalSeconds: 5, returnUrl })] });
  await client.begin({ returnUrl });
  assert.equal(JSON.parse(calls[0].init.body).returnUrl, returnUrl);
  assert.equal(client.resume({ returnUrl }).pollIntervalSeconds, 5);
  for (const target of [`${SITE}/beta.html?lang=fr`, `${SITE}/account.html?lang=fr&returnTo=/beta.html`,
    `${SITE}/account.html?lang=fr&returnTo=beta.html&register=true`,
    `${SITE}/account.html?lang=fr&returnTo=beta.html&returnTo=beta.html`]) {
    await assert.rejects(client.begin({ returnUrl: target }), { code: 'invalid_response' });
  }
});
