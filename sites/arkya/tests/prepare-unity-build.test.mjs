import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, readdir, rm, stat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { prepareUnityBuild } from '../prepare-unity-build.mjs';
import { UNITY_CHUNK_BYTES } from '../dist/game-client.mjs';

const sha = bytes => createHash('sha256').update(bytes).digest('hex');
async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'arkya-chunks-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const source = join(root, 'UnityBuild');
  const destination = join(root, 'dist', 'game');
  await mkdir(join(source, 'Build'), { recursive: true });
  await mkdir(join(source, 'StreamingAssets'));
  const manifest = { schemaVersion: 1, buildId: 'arkya-web-beta-v1', loaderUrl: 'Build/game.loader.js',
    dataUrl: 'Build/game.data.unityweb', frameworkUrl: 'Build/game.framework.js.unityweb',
    codeUrl: 'Build/game.wasm.unityweb', streamingAssetsUrl: 'StreamingAssets',
    companyName: 'Arkya', productName: 'Arkya', productVersion: 'test' };
  await writeFile(join(source, 'build-config.json'), JSON.stringify(manifest));
  for (const key of ['loaderUrl', 'dataUrl', 'frameworkUrl', 'codeUrl']) await writeFile(join(source, manifest[key]), key);
  await writeFile(join(source, 'CHARACTER-CREDITS.txt'), 'Character attribution');
  await writeFile(join(source, 'StreamingAssets', 'catalog.json'), '{"version":1}');
  await writeFile(join(source, 'index.html'), 'unused standalone loader');
  return { source, destination, manifest, root };
}

test('preparation splits actual bytes, retains auxiliary assets, removes old complete output and is deterministic', async t => {
  const f = await fixture(t);
  const bytes = Buffer.alloc(UNITY_CHUNK_BYTES + 123);
  for (let i = 0; i < bytes.length; i++) bytes[i] = (i * 31) % 251;
  await writeFile(join(f.source, f.manifest.dataUrl), bytes);
  await writeFile(join(f.source, f.manifest.codeUrl), Buffer.alloc(UNITY_CHUNK_BYTES, 17));
  await mkdir(f.destination, { recursive: true });
  await writeFile(join(f.destination, 'old-full-payload.data'), bytes);
  const result = await prepareUnityBuild(f);
  assert.equal(result.chunkedPayloads, 1);
  assert.equal(result.chunks, 2);
  const manifestBytes = await readFile(join(f.destination, 'build-config.json'));
  const prepared = JSON.parse(manifestBytes);
  const file = prepared.chunkedFiles.dataUrl;
  assert.equal(file.size, bytes.length);
  assert.equal(file.sha256, sha(bytes));
  const reconstructed = [];
  for (const [index, chunk] of file.chunks.entries()) {
    const part = await readFile(join(f.destination, chunk.url));
    assert.equal(chunk.index, index);
    assert.equal(chunk.offset, index * UNITY_CHUNK_BYTES);
    assert.equal(chunk.size, part.length);
    assert.ok(part.length <= UNITY_CHUNK_BYTES);
    assert.equal(chunk.sha256, sha(part));
    reconstructed.push(part);
  }
  assert.deepEqual(Buffer.concat(reconstructed), bytes);
  assert.deepEqual(await readFile(join(f.source, f.manifest.dataUrl)), bytes, 'canonical payload remains unchanged');
  assert.equal((await stat(join(f.destination, f.manifest.codeUrl))).size, UNITY_CHUNK_BYTES);
  assert.equal(await readFile(join(f.destination, 'CHARACTER-CREDITS.txt'), 'utf8'), 'Character attribution');
  assert.equal(await readFile(join(f.destination, 'StreamingAssets', 'catalog.json'), 'utf8'), '{"version":1}');
  for (const removed of ['index.html', 'old-full-payload.data', f.manifest.dataUrl])
    await assert.rejects(stat(join(f.destination, removed)), { code: 'ENOENT' });
  await prepareUnityBuild(f);
  assert.deepEqual(await readFile(join(f.destination, 'build-config.json')), manifestBytes);
  assert.deepEqual(await readdir(join(f.root, 'dist')), ['game']);
});

test('missing source payload fails without replacing a previously prepared game', async t => {
  const f = await fixture(t);
  await mkdir(f.destination, { recursive: true });
  await writeFile(join(f.destination, 'keep.txt'), 'previous build');
  await rm(join(f.source, f.manifest.codeUrl));
  await assert.rejects(prepareUnityBuild(f), /Missing Unity codeUrl/);
  assert.equal(await readFile(join(f.destination, 'keep.txt'), 'utf8'), 'previous build');
});

test('unaddressable oversized StreamingAssets fails explicitly and leaves no partial build', async t => {
  const f = await fixture(t);
  await mkdir(f.destination, { recursive: true });
  await writeFile(join(f.destination, 'keep.txt'), 'previous build');
  await writeFile(join(f.source, 'StreamingAssets', 'bundle'), Buffer.alloc(UNITY_CHUNK_BYTES + 1));
  await assert.rejects(prepareUnityBuild(f), /Oversized auxiliary file/);
  assert.equal(await readFile(join(f.destination, 'keep.txt'), 'utf8'), 'previous build');
  assert.deepEqual(await readdir(join(f.root, 'dist')), ['game']);
});

test('preparation rejects an overlapping or unrestricted output directory', async t => {
  const f = await fixture(t);
  await assert.rejects(prepareUnityBuild({ ...f, destination: f.source }), /separate directories/);
  await assert.rejects(prepareUnityBuild({ ...f, destination: join(f.source, 'dist', 'game') }), /separate directories/);
  await assert.rejects(prepareUnityBuild({ ...f, destination: f.root }), /separate directories/);
});
