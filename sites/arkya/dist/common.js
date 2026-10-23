import { createAuthClient } from './auth-client.mjs';

let storage;
try { storage = sessionStorage; } catch { storage = null; }
export const auth = createAuthClient({ baseUrl: window.ArkyaAuthConfig?.identityBaseUrl || '',
  charactersBaseUrl: window.ArkyaAuthConfig?.charactersBaseUrl || '', storage, siteOrigin: location.origin });
export const ready = auth.available ? auth.restore() : Promise.resolve(null);
let expiryTimer;

export function announceSession() {
  const language = window.ArkyaI18n?.language || document.documentElement.lang;
  const user = auth.session();
  clearTimeout(expiryTimer);
  if (user) expiryTimer = setTimeout(renewSession, Math.max(1000, new Date(user.expiresAt).getTime() - Date.now() - 60000));
  document.querySelectorAll('[data-auth-link]').forEach((link) => {
    link.textContent = user ? user.displayName : language === 'en' ? 'Sign in with Discord' : 'Connexion Discord';
    link.setAttribute('aria-label', user ? `${language === 'en' ? 'My account' : 'Mon compte'} · ${user.displayName}` : link.textContent);
  });
  window.dispatchEvent(new CustomEvent('arkya:sessionchange'));
}

async function renewSession() {
  try { await auth.restore(); announceSession(); }
  catch {
    announceSession();
    // An unavailable API must not cause a tight refresh loop.
    clearTimeout(expiryTimer);
    expiryTimer = setTimeout(renewSession, 30000);
  }
}

ready.then(announceSession, announceSession);
window.addEventListener('arkya:languagechange', announceSession);
window.addEventListener('pageshow', (event) => {
  if (event.persisted && auth.available) auth.restore().then(announceSession, announceSession);
});
document.addEventListener('visibilitychange', () => {
  if (!document.hidden && auth.available) void renewSession();
});
