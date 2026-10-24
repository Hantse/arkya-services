import { auth, ready, announceSession } from './common.js';
import { accountReturnPath } from './game-navigation.mjs';

const $ = (selector) => document.querySelector(selector);
const language = () => window.ArkyaI18n?.language || document.documentElement.lang;
const t = (key) => window.ArkyaPageTranslations[language()][key];
let mode = 'checking';
let message = 'account.loading';
let timer;
let attempt = 0;
let deadline = 0;
let navigatingToDiscord = false;
let error = false;

function accountReturnUrl() {
  const target = new URL('/account.html', location.origin);
  target.searchParams.set('lang', language() === 'en' ? 'en' : 'fr');
  const returnPath = accountReturnPath(location.search, language());
  if (returnPath) target.searchParams.set('returnTo', returnPath.split('?')[0]);
  return target.href;
}

function returnToDestination() {
  const target = accountReturnPath(location.search, language());
  if (auth.session() && target) location.replace(target);
}

function render() {
  const user = auth.session();
  $('#auth-unavailable').hidden = auth.available;
  $('#auth-login').hidden = !auth.available || ['pending', 'signed-in', 'logging-out'].includes(mode);
  $('#auth-pending').hidden = mode !== 'pending';
  $('#auth-profile').hidden = !['signed-in', 'logging-out'].includes(mode) || !user;
  $('#discord-login').disabled = !auth.available || ['checking', 'starting', 'logging-out'].includes(mode);
  $('#auth-logout').disabled = mode === 'logging-out';
  let text = t(message) || t('auth.invalid_response');
  if (mode === 'pending' && deadline) {
    const seconds = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
    text += ` ${t('auth.remaining')} ${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}.`;
  }
  $('#auth-status').textContent = text;
  $('#auth-status').classList.toggle('is-error', error);
  if (user) {
    $('#account-name').textContent = user.displayName;
    $('#account-expiry').textContent = `${t('auth.expires')} ${new Intl.DateTimeFormat(language(), { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(user.expiresAt))}`;
  } else {
    $('#account-name').textContent = '';
    $('#account-expiry').textContent = '';
  }
}

function reportError(reason) {
  mode = 'idle';
  error = true;
  navigatingToDiscord = false;
  const code = ['unavailable', 'network', 'rejected', 'expired', 'invalid_response', 'storage_unavailable', 'return_unavailable'].includes(reason?.code) ? reason.code : 'invalid_response';
  message = `auth.${code}`;
  auth.cancel();
  render();
}

async function pollAttempt(run) {
  if (run !== attempt) return;
  if (Date.now() >= deadline) { reportError({ code: 'expired' }); return; }
  try {
    const result = await auth.poll();
    if (run !== attempt) return;
    if (result.status === 'authenticated') {
      mode = 'signed-in'; message = 'auth.connected'; error = false;
      announceSession(); render();
      returnToDestination();
    } else if (result.status === 'pending') {
      render(); schedulePoll(run, result.pollIntervalSeconds);
    } else {
      reportError({ code: 'expired' });
    }
  } catch (reason) { if (run === attempt) reportError(reason); }
}

function schedulePoll(run, seconds = 5) {
  clearTimeout(timer);
  timer = setTimeout(() => pollAttempt(run), Math.max(5, Number(seconds) || 5) * 1000);
}

$('#discord-login').addEventListener('click', async () => {
  if (!auth.available || ['checking', 'starting', 'pending', 'logging-out'].includes(mode)) return;
  const run = ++attempt;
  clearTimeout(timer);
  mode = 'starting'; message = 'auth.starting'; error = false; render();
  try {
    const result = await auth.begin({ returnUrl: accountReturnUrl() });
    if (run !== attempt) return;
    deadline = new Date(result.expiresAt).getTime();
    mode = 'pending'; message = 'auth.pending'; render();
    navigatingToDiscord = true;
    location.assign(result.authorizationUrl);
  } catch (reason) { if (run === attempt) reportError(reason); }
});

$('#auth-cancel').addEventListener('click', () => {
  ++attempt; clearTimeout(timer); auth.cancel(); navigatingToDiscord = false;
  mode = 'idle'; message = 'auth.cancelled'; error = false; render();
});

$('#auth-logout').addEventListener('click', async () => {
  ++attempt; clearTimeout(timer);
  mode = 'logging-out'; message = 'auth.logout'; error = false; render();
  let revoked = false;
  try { revoked = (await auth.logout()).revoked; } catch { /* Local state is cleared by the auth client. */ }
  mode = 'idle'; message = revoked ? 'auth.loggedOut' : 'auth.localLogout';
  announceSession(); render();
});

window.addEventListener('arkya:languagechange', render);
window.addEventListener('arkya:sessionchange', () => {
  if (mode === 'signed-in' && !auth.session()) { mode = 'idle'; message = 'auth.expired'; }
  render();
});
window.addEventListener('pagehide', () => {
  ++attempt; clearTimeout(timer);
  // Keep the tab's short-lived proof through Discord navigation and page reloads.
  auth.suspend();
  if (!navigatingToDiscord && ['starting', 'pending'].includes(mode)) {
    mode = 'idle'; message = 'auth.cancelled'; error = false;
    deadline = 0; render();
  }
});

async function initialize() {
  const run = ++attempt;
  clearTimeout(timer);
  navigatingToDiscord = false;
  if (auth.available && !auth.session()) {
    try {
      const resumed = auth.resume({ returnUrl: accountReturnUrl() });
      if (resumed) {
        mode = 'pending'; message = 'auth.pending'; error = false;
        deadline = Date.parse(resumed.expiresAt);
        render();
        await pollAttempt(run);
        return;
      }
    } catch (reason) { if (run === attempt) reportError(reason); return; }
  }
  auth.cancel();
  mode = auth.session() ? 'signed-in' : 'idle';
  message = !auth.available ? 'auth.unavailable' : auth.session() ? 'auth.connected' : 'auth.idle';
  announceSession();
  render();
  returnToDestination();
}

window.addEventListener('pageshow', event => {
  if (event.persisted) {
    mode = 'checking'; message = 'account.loading'; render();
    auth.restore().then(initialize, reportError);
  }
});
ready.then(initialize, reportError);
render();
