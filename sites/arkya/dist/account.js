import { auth, ready, announceSession } from './common.js';
import { gameReturnPath } from './game-navigation.mjs';

const $ = (selector) => document.querySelector(selector);
const language = () => window.ArkyaI18n?.language || document.documentElement.lang;
const t = (key) => window.ArkyaPageTranslations[language()][key];
let mode = 'checking';
let message = 'account.loading';
let timer;
let popup;
let attempt = 0;
let deadline = 0;
let authorizationUrl = '';
let popupBlocked = false;
let error = false;

function returnToGame() {
  const target = gameReturnPath(location.search, language());
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
    if (popupBlocked) text += ` ${t('auth.popup')}`;
  }
  $('#auth-status').textContent = text;
  $('#auth-status').classList.toggle('is-error', error);
  $('#auth-open-discord').setAttribute('href', authorizationUrl || '#');
  if (user) {
    $('#account-name').textContent = user.displayName;
    $('#account-expiry').textContent = `${t('auth.expires')} ${new Intl.DateTimeFormat(language(), { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(user.expiresAt))}`;
  } else {
    $('#account-name').textContent = '';
    $('#account-expiry').textContent = '';
  }
}

function closePopup() {
  try { if (popup && !popup.closed) popup.close(); } catch { /* The authorization tab may already be closed. */ }
  popup = null;
}

function reportError(reason) {
  mode = 'idle';
  error = true;
  const code = ['unavailable', 'network', 'rejected', 'expired', 'invalid_response'].includes(reason?.code) ? reason.code : 'invalid_response';
  message = `auth.${code}`;
  auth.cancel();
  authorizationUrl = '';
  closePopup();
  render();
}

function schedulePoll(run, seconds = 5) {
  clearTimeout(timer);
  timer = setTimeout(async () => {
    if (run !== attempt) return;
    if (Date.now() >= deadline) { reportError({ code: 'expired' }); return; }
    try {
      const result = await auth.poll();
      if (run !== attempt) return;
      if (result.status === 'authenticated') {
        mode = 'signed-in'; message = 'auth.connected'; error = false;
        authorizationUrl = ''; closePopup(); announceSession(); render();
        returnToGame();
      } else if (result.status === 'pending') {
        render(); schedulePoll(run, result.pollIntervalSeconds);
      } else {
        reportError({ code: 'expired' });
      }
    } catch (reason) { if (run === attempt) reportError(reason); }
  }, Math.max(5, Number(seconds) || 5) * 1000);
}

$('#discord-login').addEventListener('click', async () => {
  if (!auth.available || ['checking', 'starting', 'pending', 'logging-out'].includes(mode)) return;
  const run = ++attempt;
  clearTimeout(timer);
  mode = 'starting'; message = 'auth.starting'; error = false; render();
  // Create the tab during the click, before awaiting the API, to avoid popup blocking.
  popup = window.open('about:blank', '_blank');
  popupBlocked = !popup;
  if (popup) popup.opener = null;
  try {
    const result = await auth.begin();
    if (run !== attempt) return;
    authorizationUrl = result.authorizationUrl;
    deadline = new Date(result.expiresAt).getTime();
    if (popup) {
      try { popup.location.replace(authorizationUrl); } catch { popupBlocked = true; closePopup(); }
    }
    mode = 'pending'; message = 'auth.pending'; render();
    schedulePoll(run, result.pollIntervalSeconds);
  } catch (reason) { if (run === attempt) reportError(reason); }
});

$('#auth-cancel').addEventListener('click', () => {
  ++attempt; clearTimeout(timer); auth.cancel(); closePopup(); authorizationUrl = '';
  mode = 'idle'; message = 'auth.cancelled'; error = false; render();
});

$('#auth-logout').addEventListener('click', async () => {
  ++attempt; clearTimeout(timer); closePopup();
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
  ++attempt; clearTimeout(timer); auth.cancel(); closePopup();
  if (['starting', 'pending'].includes(mode)) {
    mode = 'idle'; message = 'auth.cancelled'; error = false;
    authorizationUrl = ''; deadline = 0; render();
  }
});

ready.then(() => {
  mode = auth.session() ? 'signed-in' : 'idle';
  message = !auth.available ? 'auth.unavailable' : auth.session() ? 'auth.connected' : 'auth.idle';
  render();
  returnToGame();
}, reportError);
render();
