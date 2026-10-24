import { auth, ready, announceSession } from './common.js';

const $ = selector => document.querySelector(selector);
const language = () => window.ArkyaI18n?.language || document.documentElement.lang;
const t = key => window.ArkyaPageTranslations[language()][`beta.${key}`];
let state = 'checking';
let message = 'checking';
let registration = null;
let accountId = null;
let revision = 0;
let initialized = false;

function render() {
  const user = auth.session();
  const busy = ['checking', 'submitting'].includes(state);
  $('#beta-panel').setAttribute('aria-busy', String(busy));
  $('#beta-panel').dataset.state = state;
  $('#beta-status').textContent = t(message);
  $('#beta-status').classList.toggle('is-error', state === 'error');
  $('#beta-sign-in').hidden = state !== 'signedOut';
  $('#beta-register').hidden = !['available', 'submitting'].includes(state);
  $('#beta-register').disabled = busy;
  $('#beta-play').hidden = state !== 'granted';
  $('#beta-refresh').hidden = !user && state !== 'error';
  $('#beta-refresh').disabled = busy;
  $('#beta-account').hidden = !user;
  $('#beta-account').textContent = user?.displayName || '';
  $('#beta-date').hidden = !registration?.registeredAt || !user;
  $('#beta-date').textContent = registration?.registeredAt && user
    ? `${t('date')} ${new Intl.DateTimeFormat(language(), { dateStyle: 'long' }).format(new Date(registration.registeredAt))}` : '';
}

async function load({ register = false } = {}) {
  const run = ++revision;
  registration = null;
  state = register ? 'submitting' : 'checking';
  message = state;
  render();
  try {
    if (!auth.available) throw { code: 'unavailable' };
    const result = await auth.betaRegistration({ register });
    if (run !== revision) return;
    registration = result;
    accountId = auth.session()?.accountId || null;
    state = registration.hasBetaAccess ? 'granted' : registration.registered ? 'pending' : 'available';
    message = state;
  } catch (error) {
    if (run !== revision) return;
    if (error?.code === 'unauthenticated') {
      accountId = null;
      state = message = 'signedOut';
      announceSession();
    } else {
      state = 'error';
      message = ['unavailable', 'network', 'rejected', 'invalid_response'].includes(error?.code) ? error.code : 'network';
    }
  }
  render();
}

$('#beta-register').addEventListener('click', () => {
  if (state === 'available' && auth.session()) void load({ register: true });
});
$('#beta-refresh').addEventListener('click', () => {
  if (!['checking', 'submitting'].includes(state)) void load();
});
window.addEventListener('arkya:languagechange', render);
window.addEventListener('arkya:sessionchange', () => {
  if (!initialized) return;
  const currentId = auth.session()?.accountId || null;
  if (accountId === currentId) { render(); return; }
  accountId = currentId;
  if (currentId) void load();
  else { ++revision; registration = null; state = message = 'signedOut'; render(); }
});
window.addEventListener('pagehide', () => { ++revision; });
window.addEventListener('pageshow', event => {
  if (event.persisted && initialized) void load();
});
ready.then(() => {
  initialized = true;
  accountId = auth.session()?.accountId || null;
  if (accountId) void load();
  else { state = message = auth.available ? 'signedOut' : 'error'; if (!auth.available) message = 'unavailable'; render(); }
}, () => {
  initialized = true;
  state = 'error'; message = 'network'; render();
});
render();
