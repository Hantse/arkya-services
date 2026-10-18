import { createHash, randomUUID } from 'node:crypto';
import { copyFile, mkdir, open, readdir, readFile, realpath, rename, rm, lstat, writeFile } from 'node:fs/promises';
import { dirname, basename, resolve, join, relative, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { validateBuildManifest, UNITY_CHUNK_BYTES } from './dist/game-client.mjs';

const siteRoot = dirname(fileURLToPath(import.meta.url));
const payloadKeys = ['loaderUrl', 'dataUrl', 'frameworkUrl', 'codeUrl'];
const digest = bytes => createHash('sha256').update(bytes).digest('hex');

async function assertNoLinks(path) {
  for (let current = path; ; current = dirname(current)) {
    const info = await lstat(current).catch(error => { if (error.code !== 'ENOENT') throw error; });
    if (info?.isSymbolicLink()) throw new Error('Build paths must not contain symbolic links or junctions.');
    if (dirname(current) === current) break;
  }
}

/** Only replaces an explicitly resolved dist/game directory. The Unity source is read-only. */
export async function prepareUnityBuild({ source, destination = join(siteRoot, 'dist', 'game') }) {
  source = await realpath(resolve(source));
  destination = resolve(destination);
  if (basename(destination) !== 'game' || basename(dirname(destination)) !== 'dist' ||
      source === destination || source.startsWith(destination + sep) || destination.startsWith(source + sep))
    throw new Error('Source and dist/game output must be separate directories.');
  await assertNoLinks(destination);
  const manifest = JSON.parse(await readFile(join(source, 'build-config.json'), 'utf8'));
  validateBuildManifest(manifest, 'https://build.invalid/game/build-config.json', 'https://build.invalid');
  if (manifest.chunkedFiles) throw new Error('Input must be the complete canonical Unity build.');
  const files = [];
  async function visit(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isSymbolicLink()) throw new Error('Unity build must not contain symbolic links.');
      if (entry.isDirectory()) await visit(path);
      else if (entry.isFile()) files.push(path);
      else throw new Error('Unsupported Unity build entry.');
    }
  }
  await visit(source);
  const relativeFiles = new Set(files.map(file => relative(source, file).split(sep).join('/')));
  for (const key of payloadKeys) if (!relativeFiles.has(manifest[key])) throw new Error(`Missing Unity ${key}.`);
  const stage = join(dirname(destination), `.game-staging-${randomUUID()}`);
  const backup = join(dirname(destination), `.game-backup-${randomUUID()}`);
  const chunks = {};
  let backedUp = false;
  let published = false;
  await mkdir(join(stage, 'chunks'), { recursive: true });
  try {
    for (const file of files.sort()) {
      const name = relative(source, file).split(sep).join('/');
      // The standalone generated index bypasses the site's authenticated loader and its paths no longer apply.
      if (name === 'index.html' || name === 'build-config.json') continue;
      const size = (await lstat(file)).size;
      const key = payloadKeys.find(candidate => manifest[candidate] === name);
      if (size > UNITY_CHUNK_BYTES) {
        if (!key) throw new Error(`Oversized auxiliary file cannot be served intact: ${name}. Split it in the Unity source build first.`);
        const handle = await open(file, 'r');
        const whole = createHash('sha256');
        const parts = [];
        try {
          for (let offset = 0, index = 0; offset < size; index++) {
            const bytes = Buffer.allocUnsafe(Math.min(UNITY_CHUNK_BYTES, size - offset));
            let read = 0;
            while (read < bytes.length) {
              const result = await handle.read(bytes, read, bytes.length - read, offset + read);
              if (result.bytesRead === 0) throw new Error('Unity payload changed while being read.');
              read += result.bytesRead;
            }
            whole.update(bytes);
            const sha256 = digest(bytes);
            const url = `chunks/${sha256}.bin`;
            await writeFile(join(stage, url), bytes);
            parts.push({ index, offset, size: bytes.length, sha256, url });
            offset += bytes.length;
          }
        } finally { await handle.close(); }
        chunks[key] = { size, sha256: whole.digest('hex'), chunks: parts };
      } else {
        const target = join(stage, name);
        await mkdir(dirname(target), { recursive: true });
        await copyFile(file, target);
      }
    }
    const prepared = { ...manifest, chunkedFiles: chunks };
    validateBuildManifest(prepared, 'https://build.invalid/game/build-config.json', 'https://build.invalid');
    await writeFile(join(stage, 'build-config.json'), JSON.stringify(prepared, null, 2) + '\n');
    // Exact paths were validated above; no directory obtained from build content is removed recursively.
    await assertNoLinks(destination);
    try { await rename(destination, backup); backedUp = true; }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    await rename(stage, destination);
    published = true;
    if (backedUp) await rm(backup, { recursive: true });
    return { chunkSize: UNITY_CHUNK_BYTES, chunkedPayloads: Object.keys(chunks).length,
      chunks: Object.values(chunks).reduce((sum, file) => sum + file.chunks.length, 0), destination };
  } finally {
    if (!published) {
      await rm(stage, { recursive: true, force: true });
      if (backedUp) await rename(backup, destination);
    }
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  if (process.argv.length !== 3) throw new Error('Usage: node prepare-unity-build.mjs <complete Unity WebBeta directory>');
  console.log(JSON.stringify(await prepareUnityBuild({ source: process.argv[2] })));
}
