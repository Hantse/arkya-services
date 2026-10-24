const idPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const versionPattern = /^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$/;
const timestampPattern = /^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,9})?(?:Z|[+-](?:[01]\d|2[0-3]):[0-5]\d)$/;
const maximumResponseBytes = 4 * 1024 * 1024;
const fail = code => Object.assign(new Error(`Publication request failed: ${code}`), { code });
const validId = value => typeof value === 'string' && idPattern.test(value) && value !== '00000000-0000-0000-0000-000000000000';

function calendarDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return false;
  const parsed = Date.parse(`${value}T00:00:00Z`);
  return Number.isFinite(parsed) && new Date(parsed).toISOString().slice(0, 10) === value;
}

function filters(input = {}) {
  if (input === null || typeof input !== 'object' || Array.isArray(input)) throw fail('invalid_filters');
  const value = { kind: 'news', language: 'fr', q: '', gameVersion: '', from: '', to: '', sort: 'newest', offset: 0, limit: 12, ...input };
  if (!['news', 'release-note'].includes(value.kind) || !['fr', 'en'].includes(value.language) ||
      typeof value.q !== 'string' || value.q.length > 100 || /[\u0000-\u001f\u007f-\u009f]/.test(value.q) ||
      typeof value.gameVersion !== 'string' || value.gameVersion && !versionPattern.test(value.gameVersion) ||
      typeof value.from !== 'string' || value.from && !calendarDate(value.from) ||
      typeof value.to !== 'string' || value.to && !calendarDate(value.to) || value.from && value.to && value.from > value.to ||
      !['newest', 'oldest'].includes(value.sort) || !Number.isInteger(value.offset) || value.offset < 0 || value.offset > 10000 ||
      !Number.isInteger(value.limit) || value.limit < 1 || value.limit > 50) throw fail('invalid_filters');
  value.q = value.q.trim();
  return value;
}

function publication(value) {
  const text = (item, maximum, required = false, multiline = false) => typeof item === 'string' && item.length <= maximum &&
    (!required || item.trim().length > 0) && !(multiline ? /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/ : /[\u0000-\u001f\u007f-\u009f]/).test(item);
  if (!value || typeof value !== 'object' || !validId(value.id) || !['news', 'release-note'].includes(value.kind) ||
      !['fr', 'en'].includes(value.language) || !text(value.title, 160, true) || !text(value.summary, 400) ||
      !text(value.body, 20000, true, true) || (value.kind === 'news' ? value.gameVersion !== null :
        typeof value.gameVersion !== 'string' || !versionPattern.test(value.gameVersion)) ||
      typeof value.publishedAt !== 'string' || !timestampPattern.test(value.publishedAt) || !calendarDate(value.publishedAt.slice(0, 10)) ||
      !Number.isFinite(Date.parse(value.publishedAt))) throw fail('invalid_response');
  return { id: value.id.toLowerCase(), kind: value.kind, language: value.language, title: value.title, summary: value.summary,
    body: value.body, gameVersion: value.gameVersion ?? null, publishedAt: value.publishedAt };
}

async function readJson(response, signal) {
  const declared = response.headers.get('content-length');
  if (declared !== null && (!/^\d+$/.test(declared) || Number(declared) > maximumResponseBytes)) throw fail('invalid_response');
  if (!response.body) throw fail('invalid_response');
  const reader = response.body.getReader();
  const cancel = () => { void reader.cancel().catch(() => {}); };
  signal.addEventListener('abort', cancel, { once: true });
  const chunks = [];
  let length = 0;
  try {
    while (true) {
      if (signal.aborted) throw fail('cancelled');
      const { done, value } = await reader.read();
      if (done) break;
      length += value.byteLength;
      if (length > maximumResponseBytes) { cancel(); throw fail('invalid_response'); }
      chunks.push(value);
    }
  } finally { signal.removeEventListener('abort', cancel); reader.releaseLock(); }
  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  try { return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes)); }
  catch { throw fail('invalid_response'); }
}

export function createPublicationClient({ baseUrl = 'https://api.arkya.online', fetchImpl = globalThis.fetch, timeoutMs = 8000 } = {}) {
  let origin;
  try { origin = new URL(baseUrl); } catch { throw new TypeError('A trusted Content API origin is required.'); }
  if (origin.username || origin.password || origin.pathname !== '/' || origin.search || origin.hash ||
      !(origin.protocol === 'https:' || origin.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(origin.hostname)) ||
      !Number.isFinite(timeoutMs) || timeoutMs < 1 || timeoutMs > 30000 || typeof fetchImpl !== 'function')
    throw new TypeError('A trusted Content API origin and a bounded timeout are required.');

  async function request(path, { signal } = {}, missing = false) {
    if (signal?.aborted) throw fail('cancelled');
    const controller = new AbortController();
    let timedOut = false;
    const abort = () => controller.abort();
    signal?.addEventListener('abort', abort, { once: true });
    let rejectAbort;
    const aborted = new Promise((_, reject) => { rejectAbort = () => reject(fail(timedOut ? 'timeout' : 'cancelled')); });
    controller.signal.addEventListener('abort', rejectAbort, { once: true });
    const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
    const execute = async () => {
      const response = await fetchImpl(new URL(path, origin).href, { method: 'GET', credentials: 'omit', cache: 'no-store',
        redirect: 'error', headers: { Accept: 'application/json' }, signal: controller.signal });
      if (response.status === 404 && missing) throw fail('not_found');
      if (response.status !== 200) throw fail('unavailable');
      return readJson(response, controller.signal);
    };
    try { return await Promise.race([execute(), aborted]); }
    catch (error) {
      if (controller.signal.aborted) throw fail(timedOut ? 'timeout' : 'cancelled');
      if (['not_found', 'unavailable', 'invalid_response'].includes(error?.code)) throw error;
      throw fail('unavailable');
    } finally {
      clearTimeout(timer);
      signal?.removeEventListener('abort', abort);
      controller.signal.removeEventListener('abort', rejectAbort);
    }
  }

  async function list(input, options) {
    const query = filters(input);
    const params = new URLSearchParams();
    for (const key of ['kind', 'language', 'q', 'gameVersion', 'from', 'to', 'sort', 'offset', 'limit'])
      if (query[key] !== '') params.set(key, String(query[key]));
    const result = await request(`/v1/content/posts?${params}`, options);
    if (!result || !Array.isArray(result.items) || result.items.length > query.limit || typeof result.hasMore !== 'boolean')
      throw fail('invalid_response');
    const items = result.items.map(publication);
    if (new Set(items.map(item => item.id)).size !== items.length || items.some(item => item.kind !== query.kind || item.language !== query.language ||
        query.gameVersion && item.gameVersion !== query.gameVersion ||
        query.from && Date.parse(item.publishedAt) < Date.parse(`${query.from}T00:00:00Z`) ||
        query.to && Date.parse(item.publishedAt) >= Date.parse(`${query.to}T00:00:00Z`) + 86400000)) throw fail('invalid_response');
    return { items, hasMore: result.hasMore };
  }

  async function get(id, options) {
    if (!validId(id)) throw fail('invalid_filters');
    const result = publication(await request(`/v1/content/posts/${id.toLowerCase()}`, options, true));
    if (result.id !== id.toLowerCase()) throw fail('invalid_response');
    return result;
  }
  return Object.freeze({ list, get });
}

export function betaRegistrationHref(post, language = 'fr') {
  if (post?.kind !== 'news' || typeof post.body !== 'string') return null;
  for (const candidate of post.body.match(/https:\/\/[^\s<>"']+/gi) || []) {
    try {
      const url = new URL(candidate.replace(/[,.;:!?)\]}]+$/, ''));
      if (url.protocol === 'https:' && url.hostname === 'arkya.online' && !url.port && !url.username && !url.password && url.pathname === '/beta.html')
        return `beta.html?lang=${language === 'en' ? 'en' : 'fr'}`;
    } catch { /* Article text is never used as a navigation destination. */ }
  }
  return null;
}
