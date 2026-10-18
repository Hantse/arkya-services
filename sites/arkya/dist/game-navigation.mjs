export function gameReturnPath(search, language = 'fr') {
  const params = new URLSearchParams(search);
  // Exact local destination; never interpret a caller-supplied URL.
  if (params.getAll('returnTo').length !== 1 || params.get('returnTo') !== 'play.html') return null;
  return `play.html?lang=${language === 'en' ? 'en' : 'fr'}`;
}

export function gameSignInPath(language = 'fr') {
  return `account.html?returnTo=play.html&lang=${language === 'en' ? 'en' : 'fr'}`;
}
