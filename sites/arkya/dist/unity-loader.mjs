import { validateBuildManifest } from './game-client.mjs';
import { prepareUnityPayloads } from './unity-payloads.mjs';

const failure = (code) => Object.assign(new Error(code), { code });

export async function loadUnityGame(canvas, onProgress = () => {}, signal) {
  if (typeof WebAssembly !== 'object' || !document.createElement('canvas').getContext('webgl2')) throw failure('unsupported');
  const manifestUrl = new URL('game/build-config.json', location.href);
  const abort = new AbortController();
  const cancelManifest = () => abort.abort();
  signal?.throwIfAborted();
  signal?.addEventListener('abort', cancelManifest, { once: true });
  const timeout = setTimeout(() => abort.abort(), 15000);
  let build;
  try {
    const response = await fetch(manifestUrl, { credentials: 'omit', cache: 'no-store', redirect: 'error', signal: abort.signal });
    if (!response.ok || response.redirected || !/^application\/json(?:\s*;|$)/i.test(response.headers.get('Content-Type') || '')) throw failure('build');
    build = validateBuildManifest(await response.json(), manifestUrl.href, location.origin);
  } catch { throw failure('build'); }
  finally { clearTimeout(timeout); signal?.removeEventListener('abort', cancelManifest); }

  const payloads = await prepareUnityPayloads(build, { signal, onProgress: progress => onProgress(progress * 0.4) });
  const script = document.createElement('script');
  script.src = payloads.loaderUrl;
  script.async = true;
  let instance;
  try {
    signal?.throwIfAborted();
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => finish(failure('build')), 30000);
      const cancel = () => finish(failure('build'));
      const finish = (error) => {
        clearTimeout(timer);
        signal?.removeEventListener('abort', cancel);
        script.onload = script.onerror = null;
        if (error) { script.remove(); reject(error); } else resolve();
      };
      signal?.addEventListener('abort', cancel, { once: true });
      script.onload = () => finish();
      script.onerror = () => finish(failure('build'));
      document.head.append(script);
    });
    // Unity's generated loader exposes this factory; the game/session remain module-local.
    if (typeof window.createUnityInstance !== 'function') throw failure('build');
    instance = await window.createUnityInstance(canvas, {
      ...payloads.config,
      // Do not insert arbitrary engine messages into the page or log account payloads.
      showBanner: (_message, type) => { if (type === 'error') window.dispatchEvent(new CustomEvent('arkya:game-error')); },
    }, progress => onProgress(0.4 + progress * 0.6));
    if (!instance || typeof instance.SendMessage !== 'function' || typeof instance.Quit !== 'function') throw failure('build');
    signal?.throwIfAborted();
    const quit = instance.Quit.bind(instance);
    let quitting;
    instance.Quit = () => quitting ??= Promise.resolve().then(quit).finally(() => {
      payloads.dispose(); script.remove();
    });
    return instance;
  } catch (error) {
    try { await instance?.Quit?.(); } catch { /* Release payloads even when engine teardown fails. */ }
    payloads.dispose();
    script.remove();
    throw error;
  }
}
