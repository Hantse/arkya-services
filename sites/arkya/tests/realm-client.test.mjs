import test from 'node:test';
import assert from 'node:assert/strict';
import { createRealmPoller, publicRealmUrl, realmIdFromHostname, validRealmId, validateRealmResponse } from '../dist/realm-client.mjs';

const realmId = 'beta-aubefont-eu';
const realm = { realmId, displayName: 'Aubefont · Bêta', status: 'Ready', readyServerCount: 2,
  serverCount: 3, updatedAt: '2026-10-23T10:00:00Z', lastObservedAt: '2026-10-23T10:00:00Z', playerCount: null };
const json = (data, status = 200) => new Response(JSON.stringify(data), { status });
const settle = () => new Promise(resolve => setImmediate(resolve));

function fixture(options = {}) {
  const states = [], requests = [], timers = new Map(), replies = [];
  let timerId = 0;
  const poller = createRealmPoller({ now: () => 1234, onChange: state => states.push(state),
    setTimer: (callback, delay) => { const id = ++timerId; timers.set(id, { callback, delay }); return id; },
    clearTimer: id => timers.delete(id),
    fetchImpl: async (url, init) => {
      requests.push({ url, init });
      const reply = replies.shift();
      if (reply instanceof Error) throw reply;
      return typeof reply === 'function' ? reply(init) : reply || json([realm]);
    }, ...options });
  return { poller, states, requests, timers, replies, latest: () => states.at(-1),
    fire(delay) {
      const entry = [...timers].find(([, timer]) => timer.delay === delay);
      assert.ok(entry, `Expected timer ${delay}`);
      timers.delete(entry[0]); entry[1].callback();
    } };
}

test('realm URLs accept one valid DNS label and reject reserved or nested hosts', () => {
  assert.equal(realmIdFromHostname(`${realmId}.arkya.online`), realmId);
  assert.equal(realmIdFromHostname('BETA-AUBEFONT-EU.ARKYA.ONLINE'), realmId);
  for (const host of ['arkya.online', 'www.arkya.online', 'admin.arkya.online', 'api.arkya.online',
    'backoffice.arkya.online', 'beta-game.arkya.online', `${realmId}.admin.arkya.online`,
    'ab.arkya.online', 'beta-.arkya.online', `${'a'.repeat(32)}.arkya.online`, 'beta.arkya.online.evil.test']) {
    assert.equal(realmIdFromHostname(host), null, host);
  }
  assert.ok(validRealmId('abc'));
  assert.ok(validRealmId('a'.repeat(31)));
  assert.equal(publicRealmUrl(realmId, 'en'), `https://${realmId}.arkya.online/?lang=en`);
  assert.throws(() => publicRealmUrl('bad/realm'), TypeError);
});

test('public data retains unavailable population and rejects malformed or mismatched realms', () => {
  assert.deepEqual(validateRealmResponse([{ ...realm, privateKey: 'must not be copied' }]), [realm]);
  for (const bad of [{ ...realm, status: 'Running' }, { ...realm, readyServerCount: 4 },
    { ...realm, playerCount: -1 }, { ...realm, lastObservedAt: 'invalid' }, { ...realm, realmId: 'api' }]) {
    assert.throws(() => validateRealmResponse([bad]), TypeError);
  }
  assert.throws(() => validateRealmResponse([realm, realm]), TypeError);
  assert.throws(() => validateRealmResponse(realm, 'another-realm'), TypeError);
  assert.deepEqual(validateRealmResponse({ ...realm, status: 'Unknown', lastObservedAt: null }, realmId).playerCount, null);
});

test('public polls omit credentials and schedule ten seconds after a request finishes', async () => {
  const f = fixture();
  f.poller.start();
  await settle();
  assert.equal(f.latest().kind, 'ready');
  assert.equal(f.latest().fetchedAt, 1234);
  assert.equal(f.requests[0].url, '/v1/public/realms');
  assert.equal(f.requests[0].init.credentials, 'omit');
  assert.equal(f.requests[0].init.cache, 'no-store');
  assert.equal(f.requests[0].init.redirect, 'error');
  assert.ok([...f.timers.values()].some(timer => timer.delay === 10000));
  f.fire(10000);
  await settle();
  assert.equal(f.requests.length, 2);
  f.poller.stop();
  assert.equal(f.timers.size, 0);
});

test('refreshes never overlap a pending request', async () => {
  let finish;
  const f = fixture();
  f.replies.push(() => new Promise(resolve => { finish = resolve; }));
  f.poller.start();
  await f.poller.refresh();
  f.poller.start();
  assert.equal(f.requests.length, 1);
  assert.equal([...f.timers.values()].filter(timer => timer.delay === 10000).length, 0);
  finish(json([realm]));
  await settle();
  assert.equal(f.requests.length, 1);
  assert.equal(f.latest().refreshing, false);
  f.poller.stop();
});

test('an outage preserves the last snapshot as stale and recovery clears that state', async () => {
  const f = fixture();
  f.poller.start(); await settle();
  f.replies.push(new Error('offline'));
  await f.poller.refresh();
  assert.equal(f.latest().kind, 'ready');
  assert.equal(f.latest().stale, true);
  assert.deepEqual(f.latest().data, [realm]);
  assert.equal(f.latest().fetchedAt, 1234);
  await f.poller.refresh();
  assert.equal(f.latest().stale, false);
  f.poller.stop();
});

test('an empty catalogue, an outage, an invalid response and an unpublished realm are distinct', async () => {
  const empty = fixture(); empty.replies.push(json([])); empty.poller.start(); await settle();
  assert.equal(empty.latest().kind, 'empty'); empty.poller.stop();
  const outage = fixture(); outage.replies.push(json({}, 503)); outage.poller.start(); await settle();
  assert.equal(outage.latest().kind, 'error'); assert.equal(outage.latest().data, null); outage.poller.stop();
  const invalid = fixture(); invalid.replies.push(json({})); invalid.poller.start(); await settle();
  assert.equal(invalid.latest().kind, 'error'); invalid.poller.stop();
  const missing = fixture({ realmId }); missing.replies.push(json({}, 404)); missing.poller.start(); await settle();
  assert.equal(missing.requests[0].url, `/v1/public/realms/${realmId}`);
  assert.equal(missing.latest().kind, 'missing'); missing.poller.stop();
});

test('hiding aborts the poll, ignores late responses and resumes with one fresh request', async () => {
  let finish;
  const f = fixture();
  f.replies.push(() => new Promise(resolve => { finish = resolve; }));
  f.poller.start();
  f.poller.setVisible(false);
  assert.equal(f.requests[0].init.signal.aborted, true);
  f.poller.setVisible(true);
  assert.equal(f.requests.length, 1);
  finish(json([{ ...realm, displayName: 'Obsolete response' }]));
  await settle();
  assert.equal(f.requests.length, 2);
  assert.equal(f.latest().data[0].displayName, realm.displayName);
  assert.ok(f.states.every(state => !state.data || state.data[0].displayName !== 'Obsolete response'));
  f.poller.stop();
});

test('hidden pages make no requests and stopping ignores an unfinished response', async () => {
  let finish;
  const f = fixture({ visible: false });
  f.replies.push(() => new Promise(resolve => { finish = resolve; }));
  f.poller.start();
  assert.equal(f.requests.length, 0);
  f.poller.setVisible(true);
  f.poller.stop();
  finish(json([realm])); await settle();
  assert.equal(f.requests[0].init.signal.aborted, true);
  assert.equal(f.latest().data, null);
  assert.equal(f.timers.size, 0);
});

test('a timed out request is aborted, reported unavailable and retried on schedule', async () => {
  const f = fixture();
  f.replies.push(init => new Promise((resolve, reject) => {
    init.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true });
  }));
  f.poller.start(); f.fire(8000); await settle();
  assert.equal(f.latest().kind, 'error');
  assert.equal(f.latest().refreshing, false);
  f.fire(10000); await settle();
  assert.equal(f.latest().kind, 'ready');
  f.poller.stop();
});
