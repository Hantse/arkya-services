const toggle = document.querySelector('.menu-toggle');
const menu = document.querySelector('#mobile-menu');
const isEnglish = () => document.documentElement.lang === 'en';
function setMenu(open) {
  if (!menu || !toggle) return;
  menu.hidden = !open;
  toggle.setAttribute('aria-expanded', String(open));
  toggle.setAttribute('aria-label', open ? (isEnglish() ? 'Close menu' : 'Fermer le menu') : (isEnglish() ? 'Open menu' : 'Ouvrir le menu'));
}
toggle?.addEventListener('click', () => setMenu(menu.hidden));
menu?.addEventListener('click', event => { if (event.target.closest('a')) setMenu(false); });
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && menu && !menu.hidden) { setMenu(false); toggle.focus(); }
});
window.addEventListener('arkya:languagechange', () => setMenu(menu && !menu.hidden));
setMenu(false);
