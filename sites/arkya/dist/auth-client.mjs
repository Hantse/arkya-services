const STORAGE_KEY = 'arkya-session-v1';
const PENDING_KEY = 'arkya-discord-pending-v1';
const MIN_POLL_SECONDS = 5;
const MAX_SESSION_MS = 8 * 60 * 60 * 1000;
const MAX_CHALLENGE_MS = 5 * 60 * 1000 + 30 * 1000;
const MAX_WEB_CHALLENGE_MS = 5 * 60 * 1000;
const REFRESH_AHEAD_MS = 60 * 1000;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SECRET = /^[A-Za-z0-9_-]{43}$/;

function authError(code) {
  return Object.assign(new Error(code), { code });
}

function defaultStorage() {
  try { return globalThis.sessionStorage; } catch { return null; }
}

function isLoopback(host) {
  host = host.replace(/\.$/, '');
  return host === 'localhost' || host.endsWith('.localhost') || host === '[::1]' || /^127(?:\.\d{1,3}){3}$/.test(host);
}

function isPrivateHost(host) {
  host = host.replace(/\.$/, '');
  if (isLoopback(host) || host.endsWith('.local')) return true;
  if (host.startsWith('[')) {
    const ip = host.slice(1, -1);
    if (ip === '::' || /^(fc|fd|fe[89ab])/i.test(ip)) return true;
    if (ip.startsWith('::ffff:')) {
      const parts = ip.slice(7).split(':');
      if (parts.length === 2) {
        const value = (Number.parseInt(parts[0], 16) * 65536) + Number.parseInt(parts[1], 16);
        return isPrivateHost([value >>> 24, (value >>> 16) & 255, (value >>> 8) & 255, value & 255].join('.'));
      }
    }
    return false;
  }
  if (!/^\d+\.\d+\.\d+\.\d+$/.test(host)) return false;
  const [a, b] = host.split('.').map(Number);
  return a === 0 || a === 10 || a === 127 || a >= 224 ||
    (a === 100 && b >= 64 && b <= 127) || (a === 169 && b === 254) ||
    (a === 172 && b >= 16 && b <= 31) || (a === 192 && (b === 168 || b === 0)) ||
    (a === 198 && (b === 18 || b === 19));
}

function apiOrigin(baseUrl, siteOrigin) {
  try {
    if (typeof baseUrl !== 'string' || !baseUrl || /[\s?#\\]/.test(baseUrl)) return null;
    const base = new URL(baseUrl);
    const site = new URL(siteOrigin);
    const localSite = ['http:', 'https:'].includes(site.protocol) && isLoopback(site.hostname);
    if (base.username || base.password || base.pathname !== '/') return null;
    if (base.protocol !== 'https:' && !(base.protocol === 'http:' && localSite && isLoopback(base.hostname))) return null;
    if (isPrivateHost(base.hostname) && !(localSite && isLoopback(base.hostname))) return null;
    return base.origin;
  } catch { return null; }
}

function expiry(value, now, maximum) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$/.test(value)) throw authError('invalid_response');
  const time = Date.parse(value);
  if (!Number.isFinite(time) || time <= now || time > now + maximum) throw authError('invalid_response');
  return time;
}

function publicSession(value) {
  return { accountId: value.accountId, displayName: value.displayName, expiresAt: value.expiresAt };
}

function validateSession(value, now, withToken) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || typeof value.accountId !== 'string' || !UUID.test(value.accountId) ||
    typeof value.displayName !== 'string' || !value.displayName.trim() || value.displayName.length > 128 ||
    /[\u0000-\u001f\u007f]/.test(value.displayName) ||
    (withToken && (typeof value.sessionToken !== 'string' || !SECRET.test(value.sessionToken)))) {
    throw authError('invalid_response');
  }
  const expiresAt = new Date(expiry(value.expiresAt, now, MAX_SESSION_MS)).toISOString();
  return { accountId: value.accountId, displayName: value.displayName, expiresAt,
    ...(withToken ? { sessionToken: value.sessionToken } : {}) };
}

function base64Url(bytes) {
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function webReturnUrl(value, siteOrigin) {
  try {
    const target = new URL(value);
    const site = new URL(siteOrigin);
    if (typeof value !== 'string' || target.href !== value || target.origin !== site.origin ||
      target.username || target.password || target.pathname !== '/account.html' || target.hash ||
      (target.protocol !== 'https:' && !(target.protocol === 'http:' && isLoopback(target.hostname)))) return null;
    const params = target.searchParams;
    if ([...params.keys()].some(key => !['lang', 'returnTo'].includes(key)) ||
      params.getAll('lang').length !== 1 || !['fr', 'en'].includes(params.get('lang')) ||
      params.getAll('returnTo').length > 1 || (params.has('returnTo') && !['play.html', 'beta.html'].includes(params.get('returnTo')))) return null;
    return target.href;
  } catch { return null; }
}

/** Arkya Identity transport. Discord credentials never enter this client. */
export function createAuthClient({
  baseUrl,
  charactersBaseUrl = baseUrl,
  fetchImpl = globalThis.fetch,
  storage = defaultStorage(),
  cryptoImpl = globalThis.crypto,
  now = Date.now,
  siteOrigin = globalThis.location?.origin,
  locks = globalThis.navigator?.locks,
} = {}) {
  const origin = apiOrigin(baseUrl, siteOrigin);
  const charactersOrigin = apiOrigin(charactersBaseUrl, siteOrigin);
  const available = Boolean(origin && typeof fetchImpl === 'function' && typeof cryptoImpl?.getRandomValues === 'function' && typeof cryptoImpl?.subtle?.digest === 'function');
  let pending = null;
  let loginRevision = 0;
  let sessionRevision = 0;
  let record = null;
  let verified = false;
  let restoring = null;
  const loginRequests = new Set();
  const sessionRequests = new Set();

  function removeStorage() {
    try { storage?.removeItem(STORAGE_KEY); } catch { /* Memory-only mode. */ }
  }

  function clearSession() {
    record = null;
    verified = false;
    removeStorage();
  }

  function saveSession(value) {
    record = { version: 1, apiOrigin: origin, ...value };
    verified = true;
    try { storage?.setItem(STORAGE_KEY, JSON.stringify(record)); } catch { /* Memory-only mode. */ }
  }

  function readSession() {
    try {
      const raw = record || JSON.parse(storage?.getItem(STORAGE_KEY) || 'null');
      if (!raw) return null;
      if (raw.version !== 1 || raw.apiOrigin !== origin) throw authError('invalid_response');
      return { version: 1, apiOrigin: origin, ...validateSession(raw, now(), true) };
    } catch {
      clearSession();
      return null;
    }
  }

  function session() {
    if (record && Date.parse(record.expiresAt) <= now()) clearSession();
    return verified && record ? publicSession(record) : null;
  }

  function suspend() {
    loginRevision += 1;
    pending = null;
    for (const controller of loginRequests) controller.abort();
  }

  function cancel() {
    suspend();
    try { storage?.removeItem(PENDING_KEY); } catch { /* No new navigation can start without writable storage. */ }
  }

  function persistPending(value) {
    try {
      if (!storage) throw new Error();
      const serialized = JSON.stringify(value);
      storage.setItem(PENDING_KEY, serialized);
      if (storage.getItem(PENDING_KEY) !== serialized) throw new Error();
    } catch {
      cancel();
      throw authError('storage_unavailable');
    }
  }

  function resume({ returnUrl } = {}) {
    let raw;
    try { raw = storage?.getItem(PENDING_KEY); }
    catch { throw authError('storage_unavailable'); }
    if (!raw) return null;
    try {
      const value = JSON.parse(raw);
      if (!available || !value || value.version !== 1 || value.apiOrigin !== origin ||
        !webReturnUrl(value.returnUrl, siteOrigin) || value.returnUrl !== returnUrl ||
        typeof value.challengeId !== 'string' || !/^[0-9a-f]{32}$/i.test(value.challengeId) ||
        typeof value.codeVerifier !== 'string' || !SECRET.test(value.codeVerifier) ||
        !Number.isInteger(value.createdAt) || value.createdAt > now() ||
        !Number.isInteger(value.expires) || value.expires <= value.createdAt ||
        value.expires > value.createdAt + MAX_WEB_CHALLENGE_MS ||
        (value.nextPollAt !== undefined && (!Number.isInteger(value.nextPollAt) ||
          value.nextPollAt < value.createdAt || value.nextPollAt > value.expires + 300000)) ||
        !Number.isInteger(value.interval) || value.interval < MIN_POLL_SECONDS || value.interval > 300) throw authError('invalid_response');
      if (now() >= value.expires) throw authError('expired');
      loginRevision += 1;
      for (const controller of loginRequests) controller.abort();
      pending = { ...value, nextPollAt: value.nextPollAt ?? now(), revision: loginRevision, inFlight: null };
      return { expiresAt: new Date(value.expires).toISOString(), pollIntervalSeconds: value.interval };
    } catch (error) {
      cancel();
      throw authError(error?.code === 'expired' ? 'expired' : 'invalid_response');
    }
  }

  async function request(path, { method = 'GET', body, token, refreshCookie = false, controllers = loginRequests } = {}) {
    if (!available) throw authError('unavailable');
    const controller = new AbortController();
    controllers.add(controller);
    const timer = setTimeout(() => controller.abort(), 15000);
    try {
      const expectedUrl = `${origin}${path}`;
      const response = await fetchImpl(expectedUrl, {
        method, mode: 'cors', credentials: refreshCookie ? 'include' : 'omit', cache: 'no-store', redirect: 'error',
        referrer: '', referrerPolicy: 'no-referrer', signal: controller.signal,
        headers: { Accept: 'application/json', ...(body ? { 'Content-Type': 'application/json' } : {}),
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...(refreshCookie ? { 'X-Arkya-Refresh': '1' } : {}) },
        ...(body ? { body: JSON.stringify(body) } : {}),
      });
      if (response.redirected || (response.url && response.url !== expectedUrl) ||
        !Number.isInteger(response.status) || response.status < 100 || response.status > 599) throw authError('invalid_response');
      let data = null;
      if (response.status === 200 || response.status === 202) {
        if (!/^application\/json(?:\s*;|$)/i.test(response.headers.get('Content-Type') || '')) throw authError('invalid_response');
        try { data = await response.json(); } catch { throw authError('invalid_response'); }
      }
      return { status: response.status, data, retryAfter: response.headers.get('Retry-After') };
    } catch (error) {
      throw authError(error?.code === 'invalid_response' ? 'invalid_response' : 'network');
    } finally {
      clearTimeout(timer);
      controllers.delete(controller);
    }
  }

  function failedResponse(status) {
    return authError(status >= 500 || status === 429 ? 'network' : 'rejected');
  }

  function withRefreshLock(action) {
    // The HttpOnly cookie is shared across tabs; serialize its one-use rotations.
    return locks?.request ? locks.request(`arkya-refresh:${origin}`, action) : action();
  }

  async function refresh(revision, expectedAccountId) {
    return withRefreshLock(async () => {
      if (revision !== sessionRevision) throw authError('cancelled');
      const response = await request('/v1/auth/refresh', { method: 'POST', body: {}, refreshCookie: true, controllers: sessionRequests });
      if (revision !== sessionRevision) throw authError('cancelled');
      if (response.status === 401) { clearSession(); return null; }
      if (response.status !== 200) throw failedResponse(response.status);
      try {
        const value = validateSession(response.data, now(), true);
        if (expectedAccountId && value.accountId !== expectedAccountId) throw authError('invalid_response');
        saveSession(value);
        return session();
      } catch (error) { clearSession(); throw error; }
    });
  }

  async function restoreSession() {
    if (!available) return null;
    const revision = ++sessionRevision;
    verified = false;
    const saved = readSession();
    if (!saved) return refresh(revision);
    record = saved;
    if (Date.parse(saved.expiresAt) - now() <= REFRESH_AHEAD_MS) return refresh(revision, saved.accountId);
    const response = await request('/v1/auth/me', { token: saved.sessionToken, controllers: sessionRequests });
    if (revision !== sessionRevision) throw authError('cancelled');
    if (response.status === 401) {
      clearSession();
      return refresh(revision, saved.accountId);
    }
    if (response.status !== 200) throw failedResponse(response.status);
    try {
      const identity = validateSession(response.data, now(), false);
      if (identity.accountId !== saved.accountId || Date.parse(identity.expiresAt) > Date.parse(saved.expiresAt)) throw authError('invalid_response');
      saveSession({ ...identity, sessionToken: saved.sessionToken });
      return session();
    } catch (error) {
      clearSession();
      throw error;
    }
  }

  function restore() {
    if (!restoring) restoring = restoreSession().finally(() => { restoring = null; });
    return restoring;
  }

  async function begin({ returnUrl } = {}) {
    if (!available) throw authError('unavailable');
    cancel();
    if (returnUrl !== undefined && !webReturnUrl(returnUrl, siteOrigin)) throw authError('invalid_response');
    if (returnUrl !== undefined) {
      // Fail before leaving the page if this tab cannot retain its proof.
      persistPending({ probe: true });
      try { storage.removeItem(PENDING_KEY); } catch { cancel(); throw authError('storage_unavailable'); }
    }
    const revision = loginRevision;
    const createdAt = now();
    const verifier = base64Url(cryptoImpl.getRandomValues(new Uint8Array(32)));
    const digest = await cryptoImpl.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
    if (revision !== loginRevision) throw authError('cancelled');
    const response = await request('/v1/auth/discord/challenges', { method: 'POST', body: {
      codeChallenge: base64Url(new Uint8Array(digest)), ...(returnUrl !== undefined ? { returnUrl } : {}),
    } });
    if (revision !== loginRevision) throw authError('cancelled');
    if (response.status !== 200) throw failedResponse(response.status);
    const value = response.data;
    if (!value || typeof value !== 'object' || typeof value.challengeId !== 'string' || !/^[0-9a-f]{32}$/i.test(value.challengeId) ||
      !Number.isInteger(value.pollIntervalSeconds) || value.pollIntervalSeconds < MIN_POLL_SECONDS || value.pollIntervalSeconds > 300 ||
      value.authorizationUrl !== `${origin}/v1/auth/discord/authorize/${value.challengeId}`) throw authError('invalid_response');
    if (returnUrl !== undefined && value.returnUrl !== returnUrl) throw authError('return_unavailable');
    const serverExpires = expiry(value.expiresAt, now(), MAX_CHALLENGE_MS);
    const expires = returnUrl === undefined ? serverExpires : Math.min(serverExpires, createdAt + MAX_WEB_CHALLENGE_MS);
    if (expires <= now()) throw authError('expired');
    pending = { challengeId: value.challengeId, codeVerifier: verifier, expires,
      interval: value.pollIntervalSeconds, nextPollAt: now() + value.pollIntervalSeconds * 1000, revision, inFlight: null,
      ...(returnUrl !== undefined ? { returnUrl, createdAt } : {}) };
    if (returnUrl !== undefined) persistPending({ version: 1, apiOrigin: origin, returnUrl, createdAt,
      challengeId: pending.challengeId, codeVerifier: verifier, expires, interval: pending.interval });
    return { authorizationUrl: value.authorizationUrl, expiresAt: new Date(expires).toISOString(), pollIntervalSeconds: value.pollIntervalSeconds };
  }

  async function poll() {
    if (!available) throw authError('unavailable');
    const challenge = pending;
    if (!challenge || now() >= challenge.expires) {
      cancel();
      return { status: 'expired', pollIntervalSeconds: MIN_POLL_SECONDS };
    }
    if (challenge.inFlight) return challenge.inFlight;
    if (now() < challenge.nextPollAt) return { status: 'pending', pollIntervalSeconds: Math.max(challenge.interval, Math.ceil((challenge.nextPollAt - now()) / 1000)) };
    challenge.nextPollAt = now() + challenge.interval * 1000;
    challenge.inFlight = (async () => {
      try {
        const response = await withRefreshLock(() => {
          if (challenge.revision !== loginRevision) throw authError('cancelled');
          return request('/v1/auth/discord/exchange', { method: 'POST', refreshCookie: true,
            body: { challengeId: challenge.challengeId, codeVerifier: challenge.codeVerifier, useRefreshCookie: true } });
        });
        if (challenge.revision !== loginRevision) throw authError('cancelled');
        if (response.status === 410 || now() >= challenge.expires) {
          cancel();
          return { status: 'expired', pollIntervalSeconds: MIN_POLL_SECONDS };
        }
        if (response.status === 202) {
          if (response.data?.status !== 'pending') throw authError('invalid_response');
          const retry = Number(response.retryAfter);
          if (Number.isInteger(retry) && retry >= MIN_POLL_SECONDS && retry <= 300) challenge.interval = Math.max(challenge.interval, retry);
          challenge.nextPollAt = now() + challenge.interval * 1000;
          if (challenge.returnUrl) persistPending({ version: 1, apiOrigin: origin, returnUrl: challenge.returnUrl,
            createdAt: challenge.createdAt, challengeId: challenge.challengeId, codeVerifier: challenge.codeVerifier,
            expires: challenge.expires, interval: challenge.interval, nextPollAt: challenge.nextPollAt });
          return { status: 'pending', pollIntervalSeconds: challenge.interval };
        }
        if (response.status !== 200) throw failedResponse(response.status);
        let value = validateSession(response.data, now(), true);
        const savedRevision = ++sessionRevision;
        if (challenge.returnUrl) {
          const identityResponse = await request('/v1/auth/me', { token: value.sessionToken });
          if (challenge.revision !== loginRevision || savedRevision !== sessionRevision) throw authError('cancelled');
          if (identityResponse.status !== 200) throw failedResponse(identityResponse.status);
          const identity = validateSession(identityResponse.data, now(), false);
          if (identity.accountId !== value.accountId || Date.parse(identity.expiresAt) > Date.parse(value.expiresAt)) throw authError('invalid_response');
          value = { ...identity, sessionToken: value.sessionToken };
        }
        saveSession(value);
        cancel();
        return { status: 'authenticated', pollIntervalSeconds: MIN_POLL_SECONDS, session: session() };
      } catch (error) {
        if (challenge.revision === loginRevision && (challenge.returnUrl || error.code !== 'network')) cancel();
        throw error;
      } finally {
        challenge.inFlight = null;
      }
    })();
    return challenge.inFlight;
  }

  async function logout() {
    cancel();
    sessionRevision += 1;
    for (const controller of sessionRequests) controller.abort();
    const saved = available ? readSession() : null;
    clearSession();
    if (!available) return { revoked: false };
    try {
      const response = await withRefreshLock(() => request('/v1/auth/logout', { method: 'POST', token: saved?.sessionToken,
        refreshCookie: true, controllers: sessionRequests }));
      return { revoked: response.status === 204 || response.status === 401 };
    } catch { return { revoked: false }; }
  }

  async function betaRegistration({ register = false } = {}) {
    const current = session();
    const user = current && Date.parse(current.expiresAt) - now() > REFRESH_AHEAD_MS ? current : await restore();
    if (!user || !session()) throw authError('unauthenticated');
    const revision = sessionRevision;
    const response = await request('/v1/beta/registration', { method: register ? 'POST' : 'GET',
      ...(register ? { body: {} } : {}), token: record.sessionToken, controllers: sessionRequests });
    if (revision !== sessionRevision) throw authError('cancelled');
    if (response.status === 401) { clearSession(); throw authError('unauthenticated'); }
    if (response.status !== 200) throw failedResponse(response.status);
    const value = response.data;
    if (!value || typeof value !== 'object' || Array.isArray(value) ||
      typeof value.registered !== 'boolean' || typeof value.hasBetaAccess !== 'boolean' ||
      (value.registered ? typeof value.registeredAt !== 'string' ||
        !/^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$/.test(value.registeredAt) ||
        !Number.isFinite(Date.parse(value.registeredAt)) : value.registeredAt != null)) throw authError('invalid_response');
    return { registered: value.registered, registeredAt: value.registered ? new Date(value.registeredAt).toISOString() : null,
      hasBetaAccess: value.hasBetaAccess };
  }

  // Only the first-party Unity bridge consumes this temporary value, synchronously.
  async function withGameSession(callback) {
    if (typeof callback !== 'function') throw new TypeError('A game session callback is required.');
    if (!charactersOrigin) throw authError('unavailable');
    const user = await restore();
    if (!user || !session()) throw authError('unauthenticated');
    const payload = { sessionToken: record.sessionToken, ...publicSession(record),
      identityBaseUrl: origin, charactersBaseUrl: charactersOrigin };
    try { callback(payload); }
    finally { payload.sessionToken = ''; }
  }

  return Object.freeze({ available, restore, begin, resume, poll, suspend, cancel, logout, session, betaRegistration, withGameSession });
}
