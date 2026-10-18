const failure = () => Object.assign(new Error('build'), { code: 'build' });
const hex = bytes => Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0')).join('');

/** Reassemble compressed bytes before Unity's own decompressor sees them. No credentials are sent. */
export async function prepareUnityPayloads(build, { fetchImpl = fetch, cryptoImpl = globalThis.crypto,
  urlImpl = URL, signal, onProgress = () => {} } = {}) {
  const urls = [];
  const result = { loaderUrl: build.loaderUrl, config: { ...build.config }, dispose() {
    for (const url of urls.splice(0)) urlImpl.revokeObjectURL(url);
  } };
  let completed = 0;
  const total = Object.values(build.chunkedFiles).reduce((sum, file) => sum + file.size, 0);
  try {
    for (const [key, file] of Object.entries(build.chunkedFiles)) {
      if (!cryptoImpl?.subtle) throw failure();
      // One contiguous buffer plus one <=4 MiB response; do not retain all chunk responses.
      const bytes = new Uint8Array(file.size);
      for (const chunk of file.chunks) {
        signal?.throwIfAborted();
        const abort = new AbortController();
        const cancel = () => abort.abort();
        signal?.addEventListener('abort', cancel, { once: true });
        const timeout = setTimeout(cancel, 60000);
        try {
          const response = await fetchImpl(chunk.url, { credentials: 'omit', redirect: 'error',
            cache: 'force-cache', signal: abort.signal });
          if (!response.ok || response.redirected ||
              (response.headers.get('Content-Length') !== null && Number(response.headers.get('Content-Length')) !== chunk.size)) throw failure();
          const buffer = new Uint8Array(chunk.size);
          const reader = response.body?.getReader();
          if (!reader) throw failure();
          let offset = 0;
          try {
            while (true) {
              const part = await reader.read();
              if (part.done) break;
              if (offset + part.value.byteLength > buffer.byteLength) throw failure();
              buffer.set(part.value, offset);
              offset += part.value.byteLength;
            }
          } finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
          if (offset !== chunk.size || hex(await cryptoImpl.subtle.digest('SHA-256', buffer)) !== chunk.sha256) throw failure();
          bytes.set(buffer, chunk.offset);
          completed += chunk.size;
          onProgress(total ? completed / total : 1);
        } finally { clearTimeout(timeout); signal?.removeEventListener('abort', cancel); }
      }
      signal?.throwIfAborted();
      if (hex(await cryptoImpl.subtle.digest('SHA-256', bytes)) !== file.sha256) throw failure();
      signal?.throwIfAborted();
      const type = key === 'loaderUrl' ? 'text/javascript' : 'application/octet-stream';
      const url = urlImpl.createObjectURL(new Blob([bytes], { type }));
      urls.push(url);
      if (key === 'loaderUrl') result.loaderUrl = url;
      else result.config[key] = url;
    }
    onProgress(1);
    return result;
  } catch (error) { result.dispose(); throw error?.name === 'AbortError' ? error : failure(); }
}
