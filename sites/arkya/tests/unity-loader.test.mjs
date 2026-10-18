import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, webcrypto } from 'node:crypto';
import { loadUnityGame } from '../dist/unity-loader.mjs';
import { UNITY_CHUNK_BYTES } from '../dist/game-client.mjs';

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
function setup(t, { failure = '', onCreate } = {}) {
  const bytes = Buffer.alloc(UNITY_CHUNK_BYTES + 1, 23);
  const parts = [bytes.subarray(0, UNITY_CHUNK_BYTES), bytes.subarray(UNITY_CHUNK_BYTES)];
  const manifest = { schemaVersion: 1, buildId: 'arkya-web-beta-v1', loaderUrl: 'Build/x.loader.js',
    dataUrl: 'Build/x.data.unityweb', frameworkUrl: 'Build/x.framework.js.unityweb', codeUrl: 'Build/x.wasm.unityweb',
    streamingAssetsUrl: 'StreamingAssets', companyName: 'Arkya', productName: 'Arkya', productVersion: 'test',
    chunkedFiles: { dataUrl: { size: bytes.length, sha256: hash(bytes), chunks: parts.map((part, index) => ({
      index, offset: index * UNITY_CHUNK_BYTES, size: part.length, sha256: hash(part), url: `chunks/${hash(part)}.bin`,
    })) } } };
  const originals = new Map(['fetch', 'window', 'document', 'location', 'crypto'].map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  const originalCreate = URL.createObjectURL;
  const originalRevoke = URL.revokeObjectURL;
  const created = [];
  const revoked = [];
  let removed = 0;
  let quits = 0;
  let config;
  URL.createObjectURL = blob => { assert.equal(blob.size, bytes.length); const url = originalCreate(blob); created.push(url); return url; };
  URL.revokeObjectURL = url => { revoked.push(url); originalRevoke(url); };
  const game = { SendMessage() {}, async Quit() { quits++; if (failure === 'quit') throw new Error('quit failed'); } };
  Object.defineProperty(globalThis, 'crypto', { configurable: true, value: webcrypto });
  globalThis.location = { origin: 'https://site.test', href: 'https://site.test/play.html' };
  globalThis.document = {
    createElement(tag) { return tag === 'canvas' ? { getContext: () => ({}) } : { remove() { removed++; } }; },
    head: { append(script) { queueMicrotask(() => failure === 'script' ? script.onerror() : script.onload()); } },
  };
  globalThis.window = { async createUnityInstance(_canvas, options, progress) {
    config = options;
    assert.ok(options.dataUrl.startsWith('blob:'));
    assert.equal(options.streamingAssetsUrl, 'https://site.test/game/StreamingAssets');
    progress(0.5);
    if (failure === 'engine') throw new Error('engine failed');
    await onCreate?.();
    return game;
  } };
  globalThis.fetch = async (url, init) => {
    assert.equal(init.credentials, 'omit');
    if (String(url).endsWith('build-config.json')) return new Response(JSON.stringify(manifest), { headers: { 'Content-Type': 'application/json' } });
    const index = manifest.chunkedFiles.dataUrl.chunks.findIndex(chunk => String(url).endsWith(chunk.url));
    assert.ok(index >= 0);
    return new Response(parts[index]);
  };
  t.after(() => {
    for (const url of created) originalRevoke(url);
    URL.createObjectURL = originalCreate; URL.revokeObjectURL = originalRevoke;
    for (const [key, descriptor] of originals) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
    }
  });
  return { created, revoked, get removed() { return removed; }, get quits() { return quits; }, get config() { return config; } };
}

test('real loader integration keeps Blob alive for engine lifetime and disposes once on repeated Quit', async t => {
  const f = setup(t);
  const progress = [];
  const game = await loadUnityGame({}, value => progress.push(value));
  assert.equal(f.created.length, 1);
  assert.deepEqual(f.revoked, []);
  assert.equal(f.config.codeUrl, 'https://site.test/game/Build/x.wasm.unityweb');
  assert.ok(progress.at(-1) >= 0.4);
  await Promise.all([game.Quit(), game.Quit()]);
  assert.equal(f.quits, 1);
  assert.deepEqual(f.revoked, f.created);
  assert.equal(f.removed, 1);
});

for (const failure of ['script', 'engine', 'quit']) test(`Blob resources released after ${failure} failure`, async t => {
  const f = setup(t, { failure });
  if (failure === 'quit') {
    const game = await loadUnityGame({});
    await assert.rejects(game.Quit(), /quit failed/);
  } else await assert.rejects(loadUnityGame({}));
  assert.deepEqual(f.revoked, f.created);
  assert.ok(f.removed >= 1);
});

test('logout while engine is starting tears down the late instance and its Blob', async t => {
  const abort = new AbortController();
  const f = setup(t, { onCreate: () => abort.abort() });
  await assert.rejects(loadUnityGame({}, undefined, abort.signal), { name: 'AbortError' });
  assert.equal(f.quits, 1);
  assert.deepEqual(f.revoked, f.created);
});
