import { auth, ready, announceSession } from './common.js';
import { createGameController } from './game-client.mjs';
import { gameSignInPath } from './game-navigation.mjs';
import { loadUnityGame } from './unity-loader.mjs';

const $ = selector => document.querySelector(selector);
const language = () => window.ArkyaI18n?.language || document.documentElement.lang;
const canvas = $('#unity-canvas');
let state = { status: 'checking' };
let loggingOut = false;

function render() {
  const copy = window.ArkyaPageTranslations[language()];
  const playing = state.status === 'playing';
  $('#game-heading').textContent = copy[`play.${state.status}Title`] || copy['play.failedTitle'];
  $('#game-status').textContent = copy[`play.${state.status}`] || copy['play.failed'];
  $('#game-cover').hidden = playing;
  $('.game-frame').classList.toggle('is-playing', playing);
  canvas.tabIndex = playing ? 0 : -1;
  canvas.setAttribute('aria-hidden', String(!playing));
  $('#game-progress').hidden = state.status !== 'loading';
  $('#game-progress').value = state.progress || 0;
  $('#game-sign-in').hidden = !['signed-out', 'expired', 'rejected'].includes(state.status);
  $('#game-sign-in').href = gameSignInPath(language());
  $('#game-retry').hidden = ['playing', 'checking', 'loading', 'signed-out', 'expired', 'rejected'].includes(state.status);
  $('#game-fullscreen').disabled = !playing;
  $('#game-logout').hidden = !auth.session();
  $('#game-logout').disabled = loggingOut;
}

const controller = createGameController({ auth, loadGame: (progress, signal) => loadUnityGame(canvas, progress, signal),
  onState(next) { state = next; render(); } });

async function logout(status = 'signed-out') {
  if (loggingOut) return;
  loggingOut = true;
  // Hide/clear the game before waiting on network revocation.
  const stopped = controller.stop(status);
  const revoked = auth.logout();
  announceSession();
  render();
  await Promise.allSettled([stopped, revoked]);
  loggingOut = false;
  announceSession();
  render();
}

$('#game-logout').addEventListener('click', () => { void logout(); });
$('#game-retry').addEventListener('click', () => location.reload());
$('#game-fullscreen').addEventListener('click', () => controller.fullscreen());
canvas.addEventListener('click', () => canvas.focus());
canvas.addEventListener('contextmenu', event => event.preventDefault());
window.addEventListener('arkya:languagechange', render);
window.addEventListener('arkya:sessionchange', () => { controller.sessionChanged(); render(); });
window.addEventListener('arkya:logout', () => { void logout(); });
window.addEventListener('arkya:session-rejected', () => { void logout('rejected'); });
window.addEventListener('arkya:game-error', () => { void controller.stop('failed'); });
window.addEventListener('pagehide', () => { void controller.stop(); });
window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
document.addEventListener('visibilitychange', () => {
  if (!document.hidden && state.status === 'playing') void controller.revalidate();
});
ready.then(() => controller.start(), () => controller.stop('network'));
render();
