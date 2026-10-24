import test from 'node:test';
import assert from 'node:assert/strict';
import { createPublicationClient, betaRegistrationHref } from '../dist/content-client.mjs';

const API = 'https://api.arkya.online';
const ID = '197d1fa3-83c2-45cb-bb26-f4b574e88ee8';
const OTHER_ID = '6af01eaa-4bbf-40ee-9271-197794532afe';
const news = { id: ID, kind: 'news', language: 'fr', title: 'La bêta approche',
  summary: 'Inscrivez-vous aux prochaines vagues.', body: 'La bêta est ouverte aux inscriptions.\nhttps://arkya.online/beta.html?lang=fr',
  gameVersion: null, publishedAt: '2026-10-24T12:00:00+00:00' };
const release = { ...news, kind: 'release-note', title: 'Les premières fondations', gameVersion: '0.4.2-beta+3' };
const page = (items = [news], hasMore = false) => ({ items, hasMore });
const json = (value, status = 200, headers = {}) => new Response(JSON.stringify(value), {
  status, headers: { 'Content-Type': 'application/json', ...headers },
});
const settle = () => new Promise(resolve => setImmediate(resolve));

function fixture({ responses = [], ...options } = {}) {
  const calls = [];
  const client = createPublicationClient({ baseUrl: API, ...options, fetchImpl: async (url, init) => {
    calls.push({ url: String(url), init });
    const next = responses.shift();
    if (next instanceof Error) throw next;
    if (typeof next === 'function') return next(url, init);
    return next ?? json(page());
  } });
  return { client, calls };
}

async function rejectsCode(operation, code) {
  await assert.rejects(operation, error => {
    assert.equal(error.code, code);
    assert.ok(error instanceof Error);
    return true;
  });
}

test('public feed defaults to the French news tab with bounded first-page pagination', async () => {
  const { client, calls } = fixture();
  const result = await client.list();
  assert.equal(result.items[0].id, ID);
  assert.equal(result.hasMore, false);
  const url = new URL(calls[0].url);
  assert.equal(url.origin, API);
  assert.equal(url.pathname, '/v1/content/posts');
  assert.equal(url.searchParams.get('kind'), 'news');
  assert.equal(url.searchParams.get('language'), 'fr');
  assert.equal(url.searchParams.get('offset'), '0');
  assert.equal(url.searchParams.get('limit'), '12');
  assert.equal(url.searchParams.get('sort'), 'newest');
});

test('all filters reach the API without changing literal search or version characters', async () => {
  const q = 'bêta .* [test] + & ? # / "';
  const { client, calls } = fixture({ responses: [json(page([{ ...release, language: 'en' }], true))] });
  const result = await client.list({ kind: 'release-note', language: 'en', q, gameVersion: release.gameVersion,
    from: '2026-10-01', to: '2026-10-24', sort: 'oldest', offset: 24, limit: 12 });
  const params = new URL(calls[0].url).searchParams;
  assert.deepEqual(Object.fromEntries(params), { kind: 'release-note', language: 'en', q,
    gameVersion: release.gameVersion, from: '2026-10-01', to: '2026-10-24', sort: 'oldest', offset: '24', limit: '12' });
  assert.equal(params.getAll('q').length, 1);
  assert.equal(new URL(calls[0].url).hash, '');
  assert.equal(result.hasMore, true);
});

test('public reads omit credentials, do not cache withdrawals and never attach authorization', async () => {
  const { client, calls } = fixture({ responses: [json(page()), json(news)] });
  await client.list();
  await client.get(ID);
  assert.equal(calls[1].url, `${API}/v1/content/posts/${ID}`);
  for (const { init } of calls) {
    assert.equal(init.method, 'GET');
    assert.equal(init.credentials, 'omit');
    assert.equal(init.cache, 'no-store');
    assert.equal(init.redirect, 'error');
    const headers = new Headers(init.headers);
    assert.equal(headers.get('Accept'), 'application/json');
    assert.equal(headers.has('Authorization'), false);
    assert.equal(headers.has('Cookie'), false);
    assert.equal(init.body, undefined);
    assert.ok(init.signal instanceof AbortSignal);
  }
});

test('API configuration accepts an HTTPS origin and rejects unsafe or non-origin destinations', () => {
  for (const baseUrl of ['http://api.arkya.online', 'javascript:alert(1)', 'file:///tmp/feed',
    'https://operator:secret@api.arkya.online', 'https://api.arkya.online/admin',
    'https://api.arkya.online?key=secret', 'https://api.arkya.online#fragment', '', null]) {
    assert.throws(() => createPublicationClient({ baseUrl }), TypeError, String(baseUrl));
  }
});

test('loopback HTTP fixtures stay local and use the same public request protections', async () => {
  const { client, calls } = fixture({ baseUrl: 'http://127.0.0.1:51806' });
  await client.list();
  assert.equal(new URL(calls[0].url).origin, 'http://127.0.0.1:51806');
  assert.equal(calls[0].init.credentials, 'omit');
});

test('invalid filter types, unsupported values and out-of-range pages fail before any request', async () => {
  const invalid = [null, [], 'news', { kind: 'all' }, { kind: null }, { language: 'de' }, { language: 'FR' },
    { language: null }, { q: 123 }, { q: 'x'.repeat(101) }, { q: 'hello\nworld' }, { q: 'hello\u0085world' },
    { gameVersion: 'v 1' }, { gameVersion: 'é1' }, { gameVersion: '-1.0' }, { gameVersion: 'a'.repeat(65) },
    { gameVersion: 1 }, { gameVersion: '1.0\n' }, { sort: 'updated' }, { sort: null }, { offset: -1 },
    { offset: 10001 }, { offset: 0.5 }, { offset: '1' }, { limit: 0 }, { limit: 51 }, { limit: 1.5 }, { limit: '12' }];
  for (const filters of invalid) {
    const { client, calls } = fixture();
    await rejectsCode(() => client.list(filters), 'invalid_filters');
    assert.equal(calls.length, 0, JSON.stringify(filters));
  }
});

test('maximum search, version and page bounds remain usable', async () => {
  const version = `A${'a'.repeat(63)}`;
  const { client, calls } = fixture({ responses: [json(page([{ ...release, gameVersion: version }]))] });
  await client.list({ kind: 'release-note', language: 'fr', q: 'é'.repeat(100), gameVersion: version,
    offset: 10000, limit: 50 });
  const params = new URL(calls[0].url).searchParams;
  assert.equal(params.get('q'), 'é'.repeat(100));
  assert.equal(params.get('gameVersion'), version);
  assert.equal(params.get('offset'), '10000');
  assert.equal(params.get('limit'), '50');
});

test('calendar filters reject normalized invalid dates, non-calendar formats and reversed ranges', async () => {
  for (const filters of [{ from: '2026-02-29' }, { to: '2024-02-30' }, { from: '1900-02-29' },
    { from: '0000-01-01' }, { to: '10000-01-01' }, { from: '2026-13-01' }, { to: '2026-00-10' },
    { from: '2026-10-00' }, { to: '2026-10-32' }, { from: '2026-1-01' }, { from: '2026-01-01T00:00:00Z' },
    { from: 20261001 }, { from: '2026-10-25', to: '2026-10-24' }]) {
    const { client, calls } = fixture();
    await rejectsCode(() => client.list(filters), 'invalid_filters');
    assert.equal(calls.length, 0, JSON.stringify(filters));
  }
});

test('inclusive calendar boundaries accept leap days and the first and final supported years', async () => {
  for (const [from, to, publishedAt] of [
    ['2000-02-29', '2000-02-29', '2000-02-29T23:59:59.9999999+00:00'],
    ['0001-01-01', '0001-01-01', '0001-01-01T00:00:00Z'],
    ['9999-12-31', '9999-12-31', '9999-12-31T23:59:59.999Z'],
  ]) {
    const { client, calls } = fixture({ responses: [json(page([{ ...news, publishedAt }]))] });
    assert.equal((await client.list({ from, to })).items.length, 1);
    assert.equal(new URL(calls[0].url).searchParams.get('from'), from);
    assert.equal(new URL(calls[0].url).searchParams.get('to'), to);
  }
});

test('empty feeds and subsequent pages retain the server pagination state without fabricated rows', async () => {
  const { client, calls } = fixture({ responses: [json(page([news], true)), json(page([{ ...news, id: OTHER_ID }])), json(page([]))] });
  const first = await client.list({ limit: 1 });
  const second = await client.list({ offset: first.items.length, limit: 1 });
  const empty = await client.list({ offset: 2, limit: 1 });
  assert.equal(first.hasMore, true);
  assert.deepEqual(second.items.map(item => item.id), [OTHER_ID]);
  assert.equal(second.hasMore, false);
  assert.deepEqual(empty, page([]));
  assert.equal(new URL(calls[1].url).searchParams.get('offset'), '1');
});

test('article detail is read independently of the selected feed language and kind', async () => {
  const article = { ...release, language: 'en' };
  const { client } = fixture({ responses: [json(article)] });
  const result = await client.get(ID);
  assert.equal(result.kind, 'release-note');
  assert.equal(result.language, 'en');
  assert.equal(result.body, article.body);
});

test('detail identifiers cannot become paths, queries or the empty GUID', async () => {
  for (const id of ['', null, 42, '00000000-0000-0000-0000-000000000000', '../admin', `${ID}?draft=true`, `${ID}/publish`]) {
    const { client, calls } = fixture();
    await rejectsCode(() => client.get(id), 'invalid_filters');
    assert.equal(calls.length, 0, String(id));
  }
});

test('unpublished details, service outages and invalid JSON remain distinguishable safe errors', async () => {
  for (const [response, code] of [[json({ diagnostic: 'private-service-diagnostic' }, 404), 'not_found'],
    [json({ diagnostic: 'private-service-diagnostic' }, 503), 'unavailable'],
    [json({ diagnostic: 'private-service-diagnostic' }, 429), 'unavailable'],
    [new Error('private-service-diagnostic'), 'unavailable'],
    [new Response('{ private-service-diagnostic', { headers: { 'Content-Type': 'application/json' } }), 'invalid_response']]) {
    const { client } = fixture({ responses: [response] });
    await assert.rejects(client.get(ID), error => {
      assert.equal(error.code, code);
      assert.equal(String(error).includes('private-service-diagnostic'), false);
      assert.equal(JSON.stringify(error).includes('private-service-diagnostic'), false);
      return true;
    });
  }
});

test('failed feed reads never become a successful empty feed', async () => {
  for (const response of [json({}, 503), json({}, 401), json({}, 404), new Response(null, { status: 204 })]) {
    const { client } = fixture({ responses: [response] });
    await assert.rejects(client.list());
  }
});

test('malformed feed envelopes, non-boolean pagination and overfull responses are rejected', async () => {
  for (const value of [null, [], {}, news, { items: null, hasMore: false }, { items: [news] },
    { items: [], hasMore: 0 }, { items: [], hasMore: 'false' }, page([news, { ...news, id: OTHER_ID }])]) {
    const { client } = fixture({ responses: [json(value)] });
    await rejectsCode(() => client.list({ limit: 1 }), 'invalid_response');
  }
});

test('every required public field is checked before an article is returned', async () => {
  for (const field of Object.keys(news)) {
    const incomplete = { ...news };
    delete incomplete[field];
    const { client } = fixture({ responses: [json(incomplete)] });
    await rejectsCode(() => client.get(ID), 'invalid_response');
  }
});

test('public article validation enforces field types, text limits and kind-specific versions', async () => {
  const invalid = [null, [], { ...news, id: 'not-a-guid' }, { ...news, id: '00000000-0000-0000-0000-000000000000' },
    { ...news, kind: 'draft' }, { ...news, language: 'de' }, { ...news, title: '' }, { ...news, title: '   ' },
    { ...news, title: 'x'.repeat(161) }, { ...news, title: 'Title\nInjected' }, { ...news, title: 12 },
    { ...news, summary: null }, { ...news, summary: 'x'.repeat(401) }, { ...news, summary: 'x\u007f' },
    { ...news, body: '' }, { ...news, body: ' \n\t ' }, { ...news, body: 'x'.repeat(20001) },
    { ...news, body: 'x\u0000' }, { ...news, body: false }, { ...news, gameVersion: '1.0' },
    { ...release, gameVersion: null }, { ...release, gameVersion: '' }, { ...release, gameVersion: 'é1' },
    { ...release, gameVersion: 'a'.repeat(65) }, { ...release, gameVersion: '1.0\n' }];
  for (const value of invalid) {
    const { client } = fixture({ responses: [json(value)] });
    await rejectsCode(() => client.get(ID), 'invalid_response');
  }
});

test('valid maximum text, multiline bodies and unknown metadata preserve only the public contract', async () => {
  const article = { ...news, title: 'é'.repeat(160), summary: 's'.repeat(400),
    body: `${'x'.repeat(19996)}\r\n\tZ`, privateAdminKey: 'must-not-escape', revision: 42 };
  const { client } = fixture({ responses: [json(article)] });
  const result = await client.get(ID);
  assert.equal(result.title, article.title);
  assert.equal(result.summary, article.summary);
  assert.equal(result.body, article.body);
  assert.equal('privateAdminKey' in result, false);
  assert.equal('revision' in result, false);
});

test('published timestamps require real calendar dates, legal times and an explicit timezone', async () => {
  for (const publishedAt of [null, 1, 'yesterday', '2026-10-24', '2026-10-24T12:00:00',
    '2026-02-29T12:00:00Z', '2026-04-31T12:00:00Z', '2026-10-24T24:00:00Z',
    '2026-10-24T12:60:00Z', '2026-10-24T12:00:60Z', '0000-01-01T00:00:00Z',
    '2026-10-24T12:00:00+25:00']) {
    const { client } = fixture({ responses: [json({ ...news, publishedAt })] });
    await rejectsCode(() => client.get(ID), 'invalid_response');
  }
});

test('feed rows must match the selected kind, language, version and inclusive UTC date range', async () => {
  for (const article of [{ ...release, language: 'en' }, news, { ...release, gameVersion: '0.4.1' },
    { ...release, publishedAt: '2026-10-23T23:59:59.999Z' }, { ...release, publishedAt: '2026-10-25T00:00:00Z' }]) {
    const { client } = fixture({ responses: [json(page([article]))] });
    await rejectsCode(() => client.list({ kind: 'release-note', language: 'fr', gameVersion: release.gameVersion,
      from: '2026-10-24', to: '2026-10-24' }), 'invalid_response');
  }
  const article = { ...release, publishedAt: '2026-10-25T01:59:59.999+02:00' };
  const { client } = fixture({ responses: [json(page([article]))] });
  assert.equal((await client.list({ kind: 'release-note', from: '2026-10-24', to: '2026-10-24' })).items.length, 1);
});

test('duplicate GUIDs and a mismatched detail identifier cannot masquerade as distinct articles', async () => {
  const { client: duplicate } = fixture({ responses: [json(page([news, { ...news, id: ID.toUpperCase() }]))] });
  await rejectsCode(() => duplicate.list(), 'invalid_response');
  const { client: mismatch } = fixture({ responses: [json({ ...news, id: OTHER_ID })] });
  await rejectsCode(() => mismatch.get(ID), 'invalid_response');
});

test('a full Unicode page below the response ceiling is accepted without an ASCII-size assumption', async () => {
  const articles = Array.from({ length: 50 }, (_, index) => ({ ...news,
    id: `${(index + 1).toString(16).padStart(8, '0')}-83c2-45cb-bb26-f4b574e88ee8`, body: '界'.repeat(20000) }));
  const { client } = fixture({ responses: [json(page(articles))] });
  const result = await client.list({ limit: 50 });
  assert.equal(result.items.length, 50);
  assert.equal(result.items.at(-1).body.length, 20000);
});

test('a full page accepts Unicode escaped by the default .NET JSON serializer', async () => {
  const articles = Array.from({ length: 48 }, (_, index) => ({ ...news,
    id: `${(index + 1).toString(16).padStart(8, '0')}-83c2-45cb-bb26-f4b574e88ee8`, body: '界'.repeat(20000) }));
  const escaped = JSON.stringify(page(articles)).replace(/[^\x00-\x7f]/g,
    character => `\\u${character.charCodeAt(0).toString(16).padStart(4, '0')}`);
  assert.ok(Buffer.byteLength(escaped) > 4 * 1024 * 1024);
  const { client } = fixture({ responses: [new Response(escaped,
    { headers: { 'Content-Type': 'application/json' } })] });
  const result = await client.list({ limit: 48 });
  assert.equal(result.items.length, 48);
  assert.equal(result.items.at(-1).body, articles.at(-1).body);
});

test('oversized declared and chunked responses fail without exposing upstream content', async () => {
  const byteLimit = 8 * 1024 * 1024;
  const oversized = new ReadableStream({ start(controller) {
    controller.enqueue(new Uint8Array(byteLimit));
    controller.enqueue(new TextEncoder().encode('private-service-diagnostic'));
    controller.close();
  } });
  for (const response of [json(page(), 200, { 'Content-Length': String(byteLimit + 1) }),
    new Response(oversized, { headers: { 'Content-Type': 'application/json' } })]) {
    const { client } = fixture({ responses: [response] });
    await assert.rejects(client.list(), error => {
      assert.equal(error.code, 'invalid_response');
      assert.equal(String(error).includes('private-service-diagnostic'), false);
      return true;
    });
  }
});

test('the byte ceiling terminates an oversized stream before waiting for its end', { timeout: 1500 }, async () => {
  let cancelled = false;
  const body = new ReadableStream({
    start(controller) { controller.enqueue(new Uint8Array(8 * 1024 * 1024 + 1)); },
    cancel() { cancelled = true; },
  });
  const { client } = fixture({ timeoutMs: 1000,
    responses: [new Response(body, { headers: { 'Content-Type': 'application/json' } })] });
  await rejectsCode(() => client.list(), 'invalid_response');
  assert.equal(cancelled, true);
});

test('the request deadline covers response headers and aborts the network request', { timeout: 1500 }, async () => {
  const { client, calls } = fixture({ timeoutMs: 25, responses: [() => new Promise(() => {})] });
  await rejectsCode(() => client.list(), 'timeout');
  assert.equal(calls[0].init.signal.aborted, true);
});

test('the same deadline includes a body that stalls after successful response headers', { timeout: 1500 }, async () => {
  const stalledBody = new ReadableStream({ start(controller) { controller.enqueue(new TextEncoder().encode('{"items":[')); } });
  const { client, calls } = fixture({ timeoutMs: 25,
    responses: [new Response(stalledBody, { headers: { 'Content-Type': 'application/json' } })] });
  await rejectsCode(() => client.list(), 'timeout');
  assert.equal(calls[0].init.signal.aborted, true);
});

test('already-cancelled callers make no request', async () => {
  const controller = new AbortController();
  controller.abort();
  const { client, calls } = fixture();
  await rejectsCode(() => client.list({}, { signal: controller.signal }), 'cancelled');
  assert.equal(calls.length, 0);
});

test('caller cancellation interrupts a pending fetch and never accepts its late result', { timeout: 1500 }, async () => {
  const controller = new AbortController();
  let finish;
  const { client, calls } = fixture({ responses: [() => new Promise(resolve => { finish = resolve; })] });
  const request = client.get(ID, { signal: controller.signal });
  await settle();
  controller.abort();
  await rejectsCode(() => request, 'cancelled');
  assert.equal(calls[0].init.signal.aborted, true);
  finish(json(news));
  await settle();
});

test('caller cancellation also interrupts an unfinished response body', { timeout: 1500 }, async () => {
  const controller = new AbortController();
  const stalledBody = new ReadableStream({ start(stream) { stream.enqueue(new TextEncoder().encode('{')); } });
  const { client, calls } = fixture({
    responses: [new Response(stalledBody, { headers: { 'Content-Type': 'application/json' } })] });
  const request = client.get(ID, { signal: controller.signal });
  await settle();
  controller.abort();
  await rejectsCode(() => request, 'cancelled');
  assert.equal(calls[0].init.signal.aborted, true);
});

test('beta invitations produce only the fixed local registration destination for the selected language', () => {
  for (const body of ['Inscription : https://arkya.online/beta.html',
    'Rejoignez-nous (https://arkya.online/beta.html?lang=fr).',
    'https://arkya.online/beta.html?lang=en&returnTo=https://evil.example#fragment']) {
    assert.equal(betaRegistrationHref({ ...news, body }, 'fr'), 'beta.html?lang=fr');
    assert.equal(betaRegistrationHref({ ...news, body }, 'en'), 'beta.html?lang=en');
  }
  assert.equal(betaRegistrationHref(news, 'de'), 'beta.html?lang=fr');
  assert.equal(betaRegistrationHref({ ...release, body: news.body }, 'fr'), null);
});

test('CTA detection rejects spoofed hosts, credentials, protocols, nested paths and URLs hidden in other URLs', () => {
  for (const body of ['Pas de lien d’inscription.', 'beta.html', 'http://arkya.online/beta.html',
    'https://arkya.online.evil.example/beta.html', 'https://evil.example/arkya.online/beta.html',
    'https://arkya.online@evil.example/beta.html', 'https://operator@arkya.online/beta.html',
    'https://arkya.online:8443/beta.html', 'https://arkya.online/beta.html.evil',
    'https://arkya.online/beta.html/continue', 'https://arkya.online/other/beta.html',
    'https://evil.example/?return=https://arkya.online/beta.html',
    'javascript:alert("beta")', '<a href="https://evil.example">Inscription bêta</a>']) {
    assert.equal(betaRegistrationHref({ ...news, body }, 'fr'), null, body);
  }
  assert.equal(betaRegistrationHref(null, 'fr'), null);
});
