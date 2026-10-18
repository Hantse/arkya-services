import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, webcrypto } from 'node:crypto';
import { UNITY_CHUNK_BYTES, validateBuildManifest } from '../dist/game-client.mjs';
import { prepareUnityPayloads } from '../dist/unity-payloads.mjs';

const SITE = 'https://arkya.example';
const MANIFEST = `${SITE}/game/build-config.json`;
const hash = value => createHash('sha256').update(value).digest('hex');
const buildError = error => error.code === 'build';
const abortError = error => error.name === 'AbortError';
const bytes = Uint8Array.from({ length: UNITY_CHUNK_BYTES + 17 }, (_, i) => (i * 31 + (i >>> 12)) & 255);
const otherBytes = bytes.map(value => value ^ 0x5a);

function descriptor(value) {
  const chunks = [];
  for (let offset = 0; offset < value.length; offset += UNITY_CHUNK_BYTES) {
    const part = value.subarray(offset, offset + UNITY_CHUNK_BYTES);
    const sha256 = hash(part);
    chunks.push({ index: chunks.length, offset, size: part.length, sha256, url: `chunks/${sha256}.bin` });
  }
  return { size: value.length, sha256: hash(value), chunks };
}

function manifest(chunkedFiles) {
  return {
    schemaVersion: 1, buildId: 'arkya-web-beta-v1', loaderUrl: 'Build/Arkya.loader.js',
    dataUrl: 'Build/Arkya.data.unityweb', frameworkUrl: 'Build/Arkya.framework.js.unityweb',
    codeUrl: 'Build/Arkya.wasm.unityweb', streamingAssetsUrl: 'StreamingAssets',
    companyName: 'Arkya', productName: 'Arkya', productVersion: 'beta',
    ...(chunkedFiles === undefined ? {} : { chunkedFiles }),
  };
}

function valid(value) { return validateBuildManifest(value, MANIFEST, SITE); }

function fixture(files = { dataUrl: bytes }) {
  const input = manifest(Object.fromEntries(Object.entries(files).map(([key, value]) => [key, descriptor(value)])));
  const build = valid(input);
  const bodies = new Map();
  for (const [key, value] of Object.entries(files)) {
    for (const chunk of build.chunkedFiles[key].chunks)
      bodies.set(chunk.url, value.subarray(chunk.offset, chunk.offset + chunk.size));
  }
  const calls = [], blobs = [], revoked = [], progress = [];
  const options = {
    cryptoImpl: webcrypto,
    fetchImpl: async (url, init) => {
      calls.push({ url, init });
      const body = bodies.get(url);
      assert.ok(body, 'Only a listed payload chunk may be fetched');
      return new Response(body, { headers: { 'Content-Length': String(body.length) } });
    },
    urlImpl: {
      createObjectURL(blob) { const url = `blob:arkya-test-${blobs.length}`; blobs.push({ url, blob }); return url; },
      revokeObjectURL(url) { revoked.push(url); },
    },
    onProgress: value => progress.push(value),
  };
  return { input, build, bodies, calls, blobs, revoked, progress, options };
}

test('legacy manifest leaves real Unity URLs intact and needs no chunk fetch, crypto or Blob', async () => {
  const build = valid(manifest());
  const original = structuredClone(build);
  const result = await prepareUnityPayloads(build, {
    fetchImpl: () => assert.fail('Unchunked files remain Unity loader responsibility'),
    cryptoImpl: undefined,
    urlImpl: { createObjectURL: () => assert.fail('No Blob needed'), revokeObjectURL: () => assert.fail('No URL owned') },
  });
  assert.equal(result.loaderUrl, `${SITE}/game/Build/Arkya.loader.js`);
  assert.deepEqual(result.config, original.config);
  assert.notEqual(result.config, build.config);
  assert.deepEqual(build, original);
  result.dispose();
  result.dispose();
});

test('manifest resolves ordered content-addressed chunks strictly below the game origin', () => {
  const build = valid(manifest({ dataUrl: descriptor(bytes), codeUrl: descriptor(otherBytes) }));
  assert.equal(build.chunkedFiles.dataUrl.size, bytes.length);
  assert.equal(build.chunkedFiles.dataUrl.chunks.length, 2);
  for (const file of Object.values(build.chunkedFiles)) {
    assert.deepEqual(file.chunks.map(chunk => [chunk.index, chunk.offset, chunk.size]),
      [[0, 0, UNITY_CHUNK_BYTES], [1, UNITY_CHUNK_BYTES, 17]]);
    for (const chunk of file.chunks) assert.equal(chunk.url, `${SITE}/game/chunks/${chunk.sha256}.bin`);
  }
});

test('manifest rejects gaps, reordering, malformed hashes and paths, and inconsistent byte counts', async t => {
  const cases = {
    'unknown payload key': value => { value.chunkedFiles.secretUrl = value.chunkedFiles.dataUrl; },
    'missing chunk': value => { value.chunkedFiles.dataUrl.chunks.pop(); },
    'extra chunk': value => { value.chunkedFiles.dataUrl.chunks.push(value.chunkedFiles.dataUrl.chunks[1]); },
    'reordered chunks': value => { value.chunkedFiles.dataUrl.chunks.reverse(); },
    'wrong chunk index': value => { value.chunkedFiles.dataUrl.chunks[1].index = 0; },
    'offset gap': value => { value.chunkedFiles.dataUrl.chunks[1].offset++; },
    'offset overlap': value => { value.chunkedFiles.dataUrl.chunks[1].offset--; },
    'short first chunk': value => { value.chunkedFiles.dataUrl.chunks[0].size--; },
    'long final chunk': value => { value.chunkedFiles.dataUrl.chunks[1].size++; },
    'inconsistent total length': value => { value.chunkedFiles.dataUrl.size++; },
    'fractional length': value => { value.chunkedFiles.dataUrl.size += 0.5; },
    'unnecessarily chunked small file': value => { value.chunkedFiles.dataUrl.size = UNITY_CHUNK_BYTES; },
    'malformed file digest': value => { value.chunkedFiles.dataUrl.sha256 = 'invalid'; },
    'uppercase digest': value => { value.chunkedFiles.dataUrl.sha256 = value.chunkedFiles.dataUrl.sha256.toUpperCase(); },
    'digest does not name the URL': value => { value.chunkedFiles.dataUrl.chunks[0].sha256 = '0'.repeat(64); },
    'external URL': value => { value.chunkedFiles.dataUrl.chunks[0].url = 'https://other.example/payload.bin'; },
    'parent traversal': value => { value.chunkedFiles.dataUrl.chunks[0].url = '../payload.bin'; },
    'query credential': value => { value.chunkedFiles.dataUrl.chunks[0].url += '?token=test'; },
    'non-object descriptor': value => { value.chunkedFiles = []; },
  };
  for (const [name, mutate] of Object.entries(cases)) await t.test(name, () => {
    const input = manifest({ dataUrl: descriptor(bytes) });
    mutate(input);
    assert.throws(() => valid(input), buildError);
  });
});

test('manifest caps individual and aggregate allocations before downloading any bytes', () => {
  function sized(size) {
    const sha256 = 'a'.repeat(64);
    return { size, sha256, chunks: Array.from({ length: Math.ceil(size / UNITY_CHUNK_BYTES) }, (_, index) => ({
      index, offset: index * UNITY_CHUNK_BYTES, size: Math.min(UNITY_CHUNK_BYTES, size - index * UNITY_CHUNK_BYTES),
      sha256, url: `chunks/${sha256}.bin`,
    })) };
  }
  assert.throws(() => valid(manifest({ dataUrl: sized(512 * 1024 * 1024 + 1) })), buildError);
  assert.throws(() => valid(manifest({ dataUrl: sized(512 * 1024 * 1024), codeUrl: sized(512 * 1024 * 1024),
    frameworkUrl: sized(UNITY_CHUNK_BYTES + 1) })), buildError);
});

test('loader reconstructs exact bytes in order, omits credentials and reports monotonic progress', async () => {
  const f = fixture();
  const original = structuredClone(f.build);
  const result = await prepareUnityPayloads(f.build, f.options);
  assert.deepEqual(new Uint8Array(await f.blobs[0].blob.arrayBuffer()), bytes);
  assert.equal(f.blobs[0].blob.type, 'application/octet-stream');
  assert.equal(result.config.dataUrl, f.blobs[0].url);
  assert.equal(result.loaderUrl, original.loaderUrl);
  assert.equal(result.config.codeUrl, original.config.codeUrl);
  assert.deepEqual(f.build, original);
  assert.deepEqual(f.calls.map(call => call.url), original.chunkedFiles.dataUrl.chunks.map(chunk => chunk.url));
  for (const call of f.calls) {
    assert.equal(call.init.credentials, 'omit');
    assert.equal(call.init.redirect, 'error');
    assert.equal(call.init.cache, 'force-cache');
    assert.equal(call.init.headers, undefined);
    assert.ok(call.init.signal instanceof AbortSignal);
  }
  assert.ok(f.progress.every((value, index) => value >= (f.progress[index - 1] || 0) && value <= 1));
  assert.equal(f.progress.at(-1), 1);
  assert.ok(f.progress[0] < 1);
  result.dispose();
  result.dispose();
  assert.deepEqual(f.revoked, [f.blobs[0].url]);
});

test('loader accepts fragmented streams without Content-Length and preserves every payload byte', async () => {
  const f = fixture();
  f.options.fetchImpl = async url => {
    const body = f.bodies.get(url);
    return new Response(new ReadableStream({ start(controller) {
      for (let offset = 0; offset < body.length; offset += 65537) controller.enqueue(body.subarray(offset, offset + 65537));
      controller.close();
    } }));
  };
  const result = await prepareUnityPayloads(f.build, f.options);
  assert.deepEqual(new Uint8Array(await f.blobs[0].blob.arrayBuffer()), bytes);
  result.dispose();
});

test('corrupt chunk digest fails before any Blob URL is exposed', async () => {
  const f = fixture();
  const firstUrl = f.build.chunkedFiles.dataUrl.chunks[0].url;
  const corrupt = f.bodies.get(firstUrl).slice();
  corrupt[123] ^= 1;
  f.bodies.set(firstUrl, corrupt);
  await assert.rejects(prepareUnityPayloads(f.build, f.options), buildError);
  assert.equal(f.calls.length, 1);
  assert.deepEqual(f.blobs, []);
});

test('whole-file digest independently rejects correct chunks joined under an incorrect file digest', async () => {
  const f = fixture();
  f.build.chunkedFiles.dataUrl.sha256 = '0'.repeat(64);
  await assert.rejects(prepareUnityPayloads(f.build, f.options), buildError);
  assert.equal(f.calls.length, 2);
  assert.deepEqual(f.blobs, []);
});

test('loader rejects response failures and actual body size mismatches instead of zero padding or truncating', async t => {
  const cases = {
    'HTTP failure': () => new Response(null, { status: 503 }),
    'redirected response': body => ({ ok: true, redirected: true, headers: new Headers(), body: new Response(body).body }),
    'wrong declared length': body => new Response(body, { headers: { 'Content-Length': String(body.length + 1) } }),
    'short body': body => new Response(body.subarray(0, body.length - 1)),
    'oversized body': body => new Response(new Uint8Array(body.length + 1)),
    'missing body': () => new Response(null),
  };
  for (const [name, response] of Object.entries(cases)) await t.test(name, async () => {
    const f = fixture();
    f.options.fetchImpl = async url => response(f.bodies.get(url));
    await assert.rejects(prepareUnityPayloads(f.build, f.options), buildError);
    assert.deepEqual(f.blobs, []);
  });
});

test('a later payload failure revokes earlier Blob URLs and never mutates the validated manifest', async () => {
  const f = fixture({ loaderUrl: bytes, dataUrl: otherBytes });
  const original = structuredClone(f.build);
  const badUrl = f.build.chunkedFiles.dataUrl.chunks[0].url;
  f.bodies.set(badUrl, f.bodies.get(badUrl).map(value => value ^ 1));
  await assert.rejects(prepareUnityPayloads(f.build, f.options), buildError);
  assert.equal(f.blobs.length, 1);
  assert.equal(f.blobs[0].blob.type, 'text/javascript');
  assert.deepEqual(f.revoked, [f.blobs[0].url]);
  assert.deepEqual(f.build, original);
});

test('an already cancelled session does not fetch any chunk or allocate a Blob URL', async () => {
  const f = fixture();
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(prepareUnityPayloads(f.build, { ...f.options, signal: controller.signal }), abortError);
  assert.deepEqual(f.calls, []);
  assert.deepEqual(f.blobs, []);
});

test('session cancellation aborts an in-flight fetch and retains AbortError semantics', async () => {
  const f = fixture();
  const controller = new AbortController();
  let requestSignal;
  const fetchImpl = async (_url, { signal }) => {
    requestSignal = signal;
    return new Promise((_resolve, reject) => {
      signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true });
      queueMicrotask(() => controller.abort());
    });
  };
  await assert.rejects(prepareUnityPayloads(f.build, { ...f.options, fetchImpl, signal: controller.signal }), abortError);
  assert.equal(requestSignal.aborted, true);
  assert.deepEqual(f.blobs, []);
});

test('cancellation during the final integrity digest cannot publish a completed payload', async () => {
  const f = fixture();
  const controller = new AbortController();
  const cryptoImpl = { subtle: { async digest(algorithm, buffer) {
    const digest = await webcrypto.subtle.digest(algorithm, buffer);
    if (buffer.byteLength === bytes.length) controller.abort();
    return digest;
  } } };
  await assert.rejects(prepareUnityPayloads(f.build, { ...f.options, cryptoImpl, signal: controller.signal }), abortError);
  assert.deepEqual(f.blobs, []);
});

test('missing browser digest support fails closed without requesting or exposing payload bytes', async () => {
  const f = fixture();
  await assert.rejects(prepareUnityPayloads(f.build, { ...f.options, cryptoImpl: {} }), buildError);
  assert.deepEqual(f.calls, []);
  assert.deepEqual(f.blobs, []);
});
