function gameError(code) { return Object.assign(new Error(code), { code }); }
export const UNITY_CHUNK_BYTES = 4 * 1024 * 1024;
const UNITY_PAYLOAD_KEYS = ['loaderUrl', 'dataUrl', 'frameworkUrl', 'codeUrl'];

export function validateBuildManifest(value, manifestUrl, siteOrigin) {
  const manifest = new URL(manifestUrl, siteOrigin);
  if (manifest.origin !== new URL(siteOrigin).origin || manifest.pathname !== '/game/build-config.json' ||
      manifest.search || manifest.hash || !value || value.schemaVersion !== 1 ||
      value.buildId !== 'arkya-web-beta-v1') throw gameError('build');
  const root = new URL('./', manifest);
  const resolvePath = (input, suffix) => {
    if (typeof input !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9._/-]*$/.test(input) ||
        input.split('/').some(part => !part || part === '.' || part === '..') ||
        (suffix && !suffix.test(input))) throw gameError('build');
    const url = new URL(input, root);
    if (url.origin !== root.origin || !url.pathname.startsWith(root.pathname)) throw gameError('build');
    return url.href;
  };
  const path = (key, suffix) => resolvePath(value[key], suffix);
  const config = {
    dataUrl: path('dataUrl', /\.data(?:\.unityweb)?$/),
    frameworkUrl: path('frameworkUrl', /\.framework\.js(?:\.unityweb)?$/),
    codeUrl: path('codeUrl', /\.wasm(?:\.unityweb)?$/),
    streamingAssetsUrl: path('streamingAssetsUrl'),
  };
  for (const key of ['companyName', 'productName', 'productVersion']) {
    if (typeof value[key] !== 'string' || !value[key].trim() || value[key].length > 128 || /[\x00-\x1f\x7f]/.test(value[key])) throw gameError('build');
    config[key] = value[key];
  }
  const chunkedFiles = {};
  if (value.chunkedFiles !== undefined) {
    if (!value.chunkedFiles || typeof value.chunkedFiles !== 'object' || Array.isArray(value.chunkedFiles)) throw gameError('build');
    let totalSize = 0;
    for (const [key, file] of Object.entries(value.chunkedFiles)) {
      if (!UNITY_PAYLOAD_KEYS.includes(key) || !file || !Number.isSafeInteger(file.size) ||
          file.size <= UNITY_CHUNK_BYTES || file.size > 512 * 1024 * 1024 ||
          typeof file.sha256 !== 'string' || !/^[a-f0-9]{64}$/.test(file.sha256) || !Array.isArray(file.chunks) ||
          file.chunks.length !== Math.ceil(file.size / UNITY_CHUNK_BYTES)) throw gameError('build');
      totalSize += file.size;
      if (totalSize > 1024 * 1024 * 1024) throw gameError('build');
      const chunks = file.chunks.map((chunk, index) => {
        const expectedSize = Math.min(UNITY_CHUNK_BYTES, file.size - index * UNITY_CHUNK_BYTES);
        if (!chunk || chunk.index !== index || chunk.offset !== index * UNITY_CHUNK_BYTES ||
            chunk.size !== expectedSize || typeof chunk.sha256 !== 'string' || !/^[a-f0-9]{64}$/.test(chunk.sha256) ||
            chunk.url !== `chunks/${chunk.sha256}.bin`) throw gameError('build');
        return { index, offset: chunk.offset, size: chunk.size, sha256: chunk.sha256,
          url: resolvePath(chunk.url, /\.bin$/) };
      });
      chunkedFiles[key] = { size: file.size, sha256: file.sha256, chunks };
    }
  }
  return { loaderUrl: path('loaderUrl', /\.loader\.js$/), config, chunkedFiles };
}

async function clearAndQuit(game) {
  // A failed bridge must not prevent the engine from being torn down.
  try { game.SendMessage('ArkyaWebSession', 'ClearSiteSession', ''); } catch { /* Continue teardown. */ }
  try { await game.Quit(); } catch { /* The canvas remains hidden. */ }
}

/** Owns one engine lifetime. No session bearer is retained outside the auth callback. */
export function createGameController({ auth, loadGame, onState = () => {}, now = Date.now,
  setTimer = setTimeout, clearTimer = clearTimeout } = {}) {
  let revision = 0;
  let started = false;
  let instance = null;
  let expiryTimer;
  let recheckTimer;
  let accountId;
  let active = false;
  let rechecking = false;
  let expiresAt = 0;
  let loadingAbort;

  function cancelTimers() { clearTimer(expiryTimer); clearTimer(recheckTimer); }

  async function stop(status = 'signed-out') {
    ++revision;
    active = false;
    loadingAbort?.abort();
    cancelTimers();
    const game = instance;
    instance = null;
    onState({ status });
    if (game) await clearAndQuit(game);
  }

  function armExpiry(user) {
    clearTimer(expiryTimer);
    expiresAt = Date.parse(user.expiresAt);
    const remaining = expiresAt - now();
    if (!(remaining > 0)) { void stop('expired'); return false; }
    expiryTimer = setTimer(() => { void stop('expired'); }, remaining);
    return true;
  }

  function sessionChanged() {
    if (!active) return;
    const user = auth.session();
    // restore temporarily marks the credential unverified. Let that request decide;
    // explicit logout and the original expiry deadline still stop immediately.
    if (!user && rechecking && now() < expiresAt) return;
    if (!user || user.accountId !== accountId) { void stop('signed-out'); return; }
    armExpiry(user);
  }

  async function revalidate() {
    if (!active || rechecking) return;
    rechecking = true;
    const run = revision;
    try {
      const user = await auth.restore();
      if (run !== revision) return;
      if (!user || user.accountId !== accountId) { await stop('signed-out'); return; }
      if (armExpiry(user)) {
        scheduleRecheck();
        if (instance) onState({ status: 'playing' });
      }
    } catch {
      if (run === revision) await stop('network');
    } finally { rechecking = false; }
  }

  function scheduleRecheck() {
    clearTimer(recheckTimer);
    recheckTimer = setTimer(() => { void revalidate(); }, 60000);
  }

  async function start() {
    if (started) return;
    started = true;
    const run = ++revision;
    onState({ status: 'checking' });
    try {
      const user = await auth.restore();
      if (run !== revision) return;
      if (!user) { await stop('signed-out'); return; }
      accountId = user.accountId;
      active = true;
      if (!armExpiry(user)) return;
      onState({ status: 'loading', progress: 0 });
      loadingAbort = new AbortController();
      const game = await loadGame(progress => {
        if (run === revision) onState({ status: 'loading', progress: Math.min(1, Math.max(0, Number(progress) || 0)) });
      }, loadingAbort.signal);
      if (run !== revision) { await clearAndQuit(game); return; }
      instance = game;
      // Loading can take minutes. Revalidate with the server immediately before import.
      await auth.withGameSession(payload => {
        if (run !== revision || payload.accountId !== accountId || Date.parse(payload.expiresAt) <= now()) throw gameError('unauthenticated');
        game.SendMessage('ArkyaWebSession', 'AcceptSiteSession', JSON.stringify(payload));
      });
      if (run !== revision) return;
      const current = auth.session();
      if (!current || !armExpiry(current)) { await stop('expired'); return; }
      onState({ status: 'playing' });
      scheduleRecheck();
    } catch (error) {
      if (run !== revision) return;
      await stop(['build', 'unsupported', 'network'].includes(error?.code) ? error.code :
        error?.code === 'unauthenticated' ? 'signed-out' : 'failed');
    }
  }

  return Object.freeze({ start, stop, sessionChanged, revalidate,
    fullscreen() { if (instance && active) instance.SetFullscreen(1); } });
}
