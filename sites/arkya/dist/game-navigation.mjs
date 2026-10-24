export function gameReturnPath(search, language = 'fr') {
  const path = accountReturnPath(search, language);
  return path?.startsWith('play.html?') ? path : null;
}

export function accountReturnPath(search, language = 'fr') {
  const params = new URLSearchParams(search);
  // Exact local destination; never interpret a caller-supplied URL.
  if (params.getAll('returnTo').length !== 1 || !['play.html', 'beta.html'].includes(params.get('returnTo'))) return null;
  return `${params.get('returnTo')}?lang=${language === 'en' ? 'en' : 'fr'}`;
}

export function gameSignInPath(language = 'fr') {
  return `account.html?returnTo=play.html&lang=${language === 'en' ? 'en' : 'fr'}`;
}
