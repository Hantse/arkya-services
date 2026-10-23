import { createRealmPoller, publicRealmUrl, realmIdFromHostname } from './realm-client.mjs';

const messages = {
  fr: {
    statuses: { Pending: 'En préparation', Deploying: 'En déploiement', Ready: 'Serveurs prêts', Failed: 'Incident', Unknown: 'État non vérifié' },
    loading: 'Chargement des realms…', synced: 'Actualisé à {time}', stale: 'Actualisation interrompue · état non vérifié', feedError: 'Actualisation indisponible',
    unavailable: 'Les informations des realms sont indisponibles. Réessayez dans quelques instants.',
    empty: 'Aucun realm n’est publié pour le moment.', missing: 'Ce realm n’existe pas ou n’est pas publié.',
    invalid: 'Ouvrez un realm depuis la liste pour consulter sa page.', servers: 'Serveurs prêts',
    noObservation: 'Aucune observation disponible', noPopulation: 'Le nombre de joueurs n’est pas encore disponible.',
    detailTitle: '{name} — Realm Arkya', listTitle: 'Arkya — Les realms',
    detailDescription: 'Consultez l’état actuel du realm {name}, ses serveurs et ses dernières observations.',
    listDescription: 'Les realms d’Arkya : consultez leur état et retrouvez la page de chaque monde.',
    listLabel: 'Realms d’Arkya', open: 'Ouvrir la page du realm {name}',
  },
  en: {
    statuses: { Pending: 'Preparing', Deploying: 'Deploying', Ready: 'Servers ready', Failed: 'Incident', Unknown: 'Status unverified' },
    loading: 'Loading realms…', synced: 'Updated at {time}', stale: 'Updates interrupted · status unverified', feedError: 'Updates unavailable',
    unavailable: 'Realm information is unavailable. Try again in a moment.',
    empty: 'No realms are published yet.', missing: 'This realm does not exist or is not published.',
    invalid: 'Open a realm from the list to view its page.', servers: 'Servers ready',
    noObservation: 'No observation available', noPopulation: 'The player count is not available yet.',
    detailTitle: '{name} — Arkya realm', listTitle: 'Arkya — Realms',
    detailDescription: 'Check the current status of the {name} realm, its servers and latest observations.',
    listDescription: 'Arkya’s realms: check their status and find each world’s page.',
    listLabel: 'Arkya realms', open: 'Open the {name} realm page',
  },
};
const language = () => window.ArkyaI18n?.language === 'en' ? 'en' : 'fr';
const copy = () => messages[language()];
const t = (key, values = {}) => copy()[key].replace(/\{(\w+)\}/g, (_, name) => String(values[name] ?? ''));
const root = document.querySelector('[data-realm-detail], [data-realm-list]');
const detail = root?.hasAttribute('data-realm-detail');
const realmId = realmIdFromHostname(location.hostname);
let current = { kind: 'loading', data: null, fetchedAt: null, stale: false, refreshing: false };
let rowSignature;

function globalLinks() {
  document.querySelectorAll('[data-realm-global-link]').forEach(link => {
    const url = new URL(link.href);
    url.searchParams.set('lang', language());
    link.href = url.href;
  });
}

function dateTime(value, timeOnly = false) {
  if (!value) return t('noObservation');
  return new Intl.DateTimeFormat(language() === 'en' ? 'en-GB' : 'fr-BE', {
    ...(timeOnly ? {} : { day: 'numeric', month: 'short', year: 'numeric' }),
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).format(new Date(value));
}

const isStale = realm => current.stale || !realm.lastObservedAt ||
  Date.now() - Date.parse(realm.lastObservedAt) > 90000;

function badge(node, realm) {
  const status = current.stale || (realm.status === 'Ready' && isStale(realm)) ? 'Unknown' : realm.status;
  node.dataset.status = status;
  node.textContent = copy().statuses[status];
}

function renderRows() {
  const items = root.querySelector('[data-realm-items]');
  const realms = current.data || [];
  // Preserve keyboard focus across unchanged polling snapshots.
  const signature = JSON.stringify([realms, language(), realms.map(isStale)]);
  if (rowSignature === signature) return;
  rowSignature = signature;
  const focusedId = document.activeElement?.dataset.realmId;
  items.replaceChildren(...realms.map(realm => {
    const link = document.createElement('a');
    link.className = 'realm-row';
    link.href = publicRealmUrl(realm.realmId, language());
    link.dataset.realmId = realm.realmId;
    link.setAttribute('aria-label', t('open', { name: realm.displayName }));
    const name = document.createElement('span');
    name.className = 'realm-row-name';
    const title = document.createElement('span');
    title.className = 'realm-row-title';
    title.textContent = realm.displayName;
    const id = document.createElement('span');
    id.className = 'realm-row-id';
    id.textContent = `${realm.realmId}.arkya.online`;
    name.append(title, id);
    const status = document.createElement('span');
    status.className = 'realm-status';
    badge(status, realm);
    const servers = document.createElement('span');
    servers.className = 'realm-row-servers';
    const label = document.createElement('small');
    label.textContent = t('servers');
    servers.append(label, isStale(realm) ? '—' : `${realm.readyServerCount} / ${realm.serverCount}`);
    const arrow = document.createElement('span');
    arrow.className = 'realm-row-arrow';
    arrow.setAttribute('aria-hidden', 'true');
    arrow.textContent = '↗';
    link.append(name, status, servers, arrow);
    return link;
  }));
  if (focusedId) [...items.children].find(link => link.dataset.realmId === focusedId)?.focus({ preventScroll: true });
}

function renderDetail() {
  const realm = current.data;
  root.querySelector('[data-realm-overview]').hidden = !realm;
  const name = realm?.displayName || realmId || 'Realm';
  document.querySelector('#realm-title').textContent = name;
  document.title = t('detailTitle', { name });
  document.querySelector('meta[name="description"]').content = t('detailDescription', { name });
  root.querySelector('[data-realm-address]').textContent = realmId ? `${realmId}.arkya.online` : '';
  if (!realm) return;
  badge(root.querySelector('[data-realm-status]'), realm);
  root.querySelector('[data-realm-servers]').textContent = isStale(realm) ? '—' : `${realm.readyServerCount} / ${realm.serverCount}`;
  root.querySelector('[data-realm-players]').textContent = isStale(realm) ? '—' : realm.playerCount ?? '—';
  root.querySelector('[data-realm-population-note]').textContent = realm.playerCount === null ? t('noPopulation') : '';
  const observed = root.querySelector('[data-realm-observed]');
  observed.textContent = dateTime(realm.lastObservedAt);
  if (realm.lastObservedAt) observed.dateTime = realm.lastObservedAt;
  else observed.removeAttribute('datetime');
}

function render() {
  globalLinks();
  if (!root) return;
  const feed = root.querySelector('[data-realm-feed-status]');
  const state = current.stale && current.data !== null ? 'stale' : current.kind;
  const statusText = state === 'loading' ? t('loading') : state === 'stale' ? t('stale') :
    state === 'error' ? t('feedError') : current.fetchedAt ? t('synced', { time: dateTime(current.fetchedAt, true) }) : '';
  if (feed.textContent !== statusText) feed.textContent = statusText;
  feed.parentElement.dataset.feedState = state;
  feed.parentElement.hidden = current.kind === 'invalid';
  const retry = root.querySelector('[data-realm-refresh]');
  retry.disabled = current.refreshing || (detail && !realmId);
  const message = root.querySelector('[data-realm-message]');
  message.textContent = current.kind === 'invalid' ? t('invalid') : current.kind === 'missing' ? t('missing') :
    current.kind === 'empty' ? t('empty') : current.kind === 'error' ? t('unavailable') : '';
  message.hidden = !message.textContent;
  if (detail) renderDetail();
  else {
    root.setAttribute('aria-label', t('listLabel'));
    renderRows();
    if (document.body.classList.contains('realm-page')) {
      document.title = t('listTitle');
      document.querySelector('meta[name="description"]').content = t('listDescription');
    }
  }
}

window.addEventListener('arkya:languagechange', render);
if (root && (!detail || realmId)) {
  const poller = createRealmPoller({ realmId: detail ? realmId : null, visible: !document.hidden,
    onChange: next => { current = next; render(); } });
  root.querySelector('[data-realm-refresh]').addEventListener('click', () => void poller.refresh());
  document.addEventListener('visibilitychange', () => poller.setVisible(!document.hidden));
  window.addEventListener('pagehide', () => poller.stop());
  window.addEventListener('pageshow', event => { if (event.persisted) poller.start(); });
  poller.start();
} else {
  if (detail) current.kind = 'invalid';
  render();
}
