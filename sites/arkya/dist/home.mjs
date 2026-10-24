import { createPublicationClient } from './content-client.mjs';
const container = document.querySelector('#home-news');
const retry = document.querySelector('#home-news-retry');
const client = createPublicationClient({ baseUrl: window.ArkyaAuthConfig?.identityBaseUrl || 'https://api.arkya.online' });
const language = () => window.ArkyaI18n.language;
const t = key => window.ArkyaPageTranslations[language()][`home.${key}`];
let generation = 0;
let request;
function node(tag, className, text) {
  const result = document.createElement(tag);
  if (className) result.className = className;
  if (text !== undefined) result.textContent = text;
  return result;
}
async function loadNews() {
  const version = ++generation;
  request?.abort();
  request = new AbortController();
  container.dataset.state = 'loading';
  container.replaceChildren(node('p', 'home-feed-message', t('newsLoading')));
  retry.hidden = true;
  try {
    const result = await client.list({ kind: 'news', language: language(), limit: 3 }, { signal: request.signal });
    if (version !== generation) return;
    container.dataset.state = result.items.length ? 'ready' : 'empty';
    if (!result.items.length) { container.replaceChildren(node('p', 'home-feed-message', t('newsEmpty'))); return; }
    const cards = result.items.map(post => {
      const article = node('article', 'home-news-card');
      article.lang = post.language;
      const time = node('time', '', new Intl.DateTimeFormat(language() === 'fr' ? 'fr-FR' : 'en-GB', { dateStyle: 'long' }).format(new Date(post.publishedAt)));
      time.dateTime = post.publishedAt;
      const title = node('h3');
      const anchor = node('a', '', post.title);
      anchor.href = `news.html?lang=${language()}&post=${encodeURIComponent(post.id)}`;
      title.append(anchor);
      const read = node('a', 'home-news-read', t('read'));
      read.href = anchor.href;
      read.setAttribute('aria-label', `${t('read')} ${post.title}`);
      article.append(time, title, node('p', '', post.summary), read);
      return article;
    });
    container.replaceChildren(...cards);
  } catch {
    if (version !== generation) return;
    container.dataset.state = 'error';
    container.replaceChildren(node('p', 'home-feed-message', t('newsError')));
    retry.hidden = false;
  }
}
retry.addEventListener('click', loadNews);
window.addEventListener('arkya:languagechange', loadNews);
loadNews();
