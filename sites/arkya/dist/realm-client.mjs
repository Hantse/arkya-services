const realmPattern = /^[a-z][a-z0-9-]{1,29}[a-z0-9]$/;
const reservedNames = new Set(['www', 'api', 'admin', 'backoffice', 'beta-game']);
const statuses = new Set(['Pending', 'Deploying', 'Ready', 'Failed', 'Unknown']);

export function validRealmId(value) {
  return typeof value === 'string' && realmPattern.test(value) && !reservedNames.has(value);
}

export function realmIdFromHostname(hostname) {
  const host = hostname.toLowerCase();
  const suffix = '.arkya.online';
  if (!host.endsWith(suffix)) return null;
  const id = host.slice(0, -suffix.length);
  return validRealmId(id) ? id : null;
}

export function publicRealmUrl(realmId, language = 'fr') {
  if (!validRealmId(realmId)) throw new TypeError('Invalid realm identifier');
  return `https://${realmId}.arkya.online/?lang=${language === 'en' ? 'en' : 'fr'}`;
}

function readRealm(value) {
  const count = number => Number.isSafeInteger(number) && number >= 0;
  const date = text => typeof text === 'string' && Number.isFinite(Date.parse(text));
  if (!value || !validRealmId(value.realmId) || typeof value.displayName !== 'string' ||
      !value.displayName.trim() || !statuses.has(value.status) ||
      !count(value.serverCount) || !count(value.readyServerCount) || value.readyServerCount > value.serverCount ||
      !date(value.updatedAt) || (value.lastObservedAt !== null && !date(value.lastObservedAt)) ||
      (value.playerCount !== null && !count(value.playerCount))) throw new TypeError('Invalid public realm response');
  return { realmId: value.realmId, displayName: value.displayName, status: value.status,
    readyServerCount: value.readyServerCount, serverCount: value.serverCount,
    updatedAt: value.updatedAt, lastObservedAt: value.lastObservedAt, playerCount: value.playerCount };
}

export function validateRealmResponse(value, realmId = null) {
  if (realmId) {
    const realm = readRealm(value);
    if (realm.realmId !== realmId) throw new TypeError('Unexpected realm');
    return realm;
  }
  if (!Array.isArray(value)) throw new TypeError('Invalid public realm catalogue');
  const realms = value.map(readRealm);
  if (new Set(realms.map(realm => realm.realmId)).size !== realms.length) throw new TypeError('Duplicate realms');
  return realms;
}

// Complete one request before scheduling the next; a hidden page has no active poll.
export function createRealmPoller({ realmId = null, onChange, fetchImpl = globalThis.fetch,
  intervalMs = 10000, timeoutMs = 8000, now = Date.now,
  setTimer = setTimeout, clearTimer = clearTimeout, visible = true } = {}) {
  if (realmId !== null && !validRealmId(realmId)) throw new TypeError('Invalid realm identifier');
  let state = { kind: 'loading', data: null, fetchedAt: null, stale: false, refreshing: false };
  let running = false;
  let active = null;
  let timer;
  let queued = false;
  const publish = next => { state = { ...state, ...next }; onChange?.(state); };
  const schedule = () => {
    clearTimer(timer);
    if (running && visible) timer = setTimer(refresh, intervalMs);
  };

  async function refresh() {
    clearTimer(timer);
    if (!running || !visible) return;
    if (active) return;
    const request = { controller: new AbortController(), cancelled: false, timedOut: false };
    active = request;
    publish({ refreshing: true });
    const deadline = setTimer(() => { request.timedOut = true; request.controller.abort(); }, timeoutMs);
    try {
      const response = await fetchImpl(`/v1/public/realms${realmId ? `/${encodeURIComponent(realmId)}` : ''}`, {
        method: 'GET', credentials: 'omit', cache: 'no-store', redirect: 'error',
        headers: { Accept: 'application/json' }, signal: request.controller.signal,
      });
      if (request.cancelled || !running || !visible) return;
      if (request.timedOut) throw new Error('Request timed out');
      if (response.status === 404 && realmId) {
        publish({ kind: 'missing', data: null, fetchedAt: now(), stale: false });
      } else {
        if (!response.ok) throw new Error('Realm service unavailable');
        const data = validateRealmResponse(await response.json(), realmId);
        if (request.cancelled || !running || !visible) return;
        if (request.timedOut) throw new Error('Request timed out');
        publish({ kind: Array.isArray(data) && !data.length ? 'empty' : 'ready', data, fetchedAt: now(), stale: false });
      }
    } catch {
      if (!request.cancelled && running && visible) {
        publish({ kind: state.data === null ? 'error' : state.kind, stale: true });
      }
    } finally {
      clearTimer(deadline);
      active = null;
      if (running && visible) {
        publish({ refreshing: false });
        if (queued) { queued = false; void refresh(); }
        else schedule();
      }
    }
  }

  function pause() {
    clearTimer(timer);
    if (active) { active.cancelled = true; active.controller.abort(); }
  }

  return {
    start() { if (running) return; running = true; if (active) queued = true; else void refresh(); },
    stop() { running = false; queued = false; pause(); },
    refresh,
    setVisible(next) {
      if (visible === Boolean(next)) return;
      visible = Boolean(next);
      if (!visible) { queued = false; pause(); }
      else if (running) { if (active) queued = true; else void refresh(); }
    },
  };
}
