import './common.js';
import { createPublicationClient, betaRegistrationHref } from './content-client.mjs';

const client = createPublicationClient({ baseUrl: window.ArkyaAuthConfig?.identityBaseUrl || 'https://api.arkya.online' });
const $ = selector => document.querySelector(selector);
const tabs = [...document.querySelectorAll('[data-kind]')];
const pageSize = 12;
const language = () => window.ArkyaI18n?.language === 'en' ? 'en' : 'fr';
const t = (key, values = {}) => (window.ArkyaPageTranslations?.[language()]?.[`news.${key}`] || key)
  .replace(/\{(\w+)\}/g, (_, name) => String(values[name] ?? ''));
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
let current = readUrl();
let request;
let generation = 0;
let syncingLanguage = false;
let returnFocusId;

function readUrl() {
  const params = new URL(location.href).searchParams;
  const page = params.get('page') || '1';
  return {
    tab: params.get('tab') === 'release-note' ? 'release-note' : 'news',
    language: params.get('lang') === 'en' ? 'en' : params.get('lang') === 'fr' ? 'fr' : language(),
    page: /^\d+$/.test(page) && Number.isSafeInteger(Number(page)) ? Number(page) : 0,
    post: params.has('post') ? params.get('post') || 'invalid' : null,
  };
}

function urlFor(state) {
  const url = new URL('news.html', location.href);
  url.searchParams.set('lang', state.language);
  url.searchParams.set('tab', state.tab);
  if (state.page !== 1) url.searchParams.set('page', String(state.page));
  if (state.post) url.searchParams.set('post', state.post);
  return url.pathname + url.search;
}

function navigate(next, { replace = false, article = false, focus = false } = {}) {
  current = next;
  history[replace ? 'replaceState' : 'pushState']({ arkyaPublication: article }, '', urlFor(current));
  void load({ focus });
}

function element(tag, className, value) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (value !== undefined) node.textContent = value;
  return node;
}

function date(value, publicationLanguage) {
  return new Intl.DateTimeFormat(publicationLanguage === 'en' ? 'en-GB' : 'fr-BE', {
    year: 'numeric', month: 'long', day: 'numeric', timeZone: 'UTC',
  }).format(new Date(value));
}

function metadata(post) {
  const kind = element('span', 'news-kind', t(post.kind === 'news' ? 'tabNews' : 'tabRelease'));
  const published = element('time', '', date(post.publishedAt, current.language));
  published.dateTime = post.publishedAt;
  const nodes = [kind, published];
  if (post.gameVersion) nodes.push(element('span', 'news-version', t('versionLabel', { version: post.gameVersion })));
  if (post.language !== current.language) {
    const badge = element('span', 'news-publication-language', t(post.language === 'en' ? 'languageEn' : 'languageFr'));
    badge.lang = post.language;
    nodes.push(badge);
  }
  return nodes;
}

function card(post) {
  const article = element('article', 'news-card');
  article.lang = post.language;
  const meta = element('div', 'news-publication-meta');
  meta.append(...metadata(post));
  const heading = element('h3');
  const title = element('a', '', post.title);
  title.href = urlFor({ ...current, post: post.id });
  title.dataset.publicationId = post.id;
  heading.append(title);
  const summary = element('p', 'news-card-summary', post.summary);
  const actions = element('div', 'news-card-actions');
  const read = element('a', 'news-read-link', t('read'));
  read.href = title.href;
  read.dataset.publicationId = post.id;
  read.setAttribute('aria-label', `${t('read')} : ${post.title}`);
  const arrow = element('span', '', '↗');
  arrow.setAttribute('aria-hidden', 'true');
  read.append(arrow);
  actions.append(read);
  const betaHref = betaRegistrationHref(post, current.language);
  if (betaHref) {
    const beta = element('a', 'button primary news-beta-link', t('beta'));
    beta.href = betaHref;
    actions.append(beta);
  }
  article.append(meta, heading, summary, actions);
  return article;
}

function renderShell() {
  tabs.forEach(tab => {
    const selected = tab.dataset.kind === current.tab;
    tab.setAttribute('aria-selected', String(selected));
    tab.tabIndex = selected ? 0 : -1;
  });
  $('#publication-panel').setAttribute('aria-labelledby', `tab-${current.tab}`);
  $('#news-list-view').hidden = Boolean(current.post);
  $('#news-article-view').hidden = !current.post;
  $('#news-article').hidden = true;
  $('#news-back').href = urlFor({ ...current, post: null });
  $('#news-results-title').textContent = t(current.tab === 'news' ? 'latestNews' : 'latestReleases');
  $('#news-result-count').textContent = '';
  $('#news-items').replaceChildren();
  $('#news-pagination').hidden = true;
  document.title = window.ArkyaPageTranslations[language()]['page.title'];
  document.querySelector('meta[name="description"]').content = window.ArkyaPageTranslations[language()]['meta.description'];
}

function status(kind) {
  const loading = kind === 'loading';
  $('#publication-panel').setAttribute('aria-busy', String(loading));
  $('#publication-panel').dataset.state = kind;
  $('#news-status').hidden = kind === 'ready';
  const title = kind === 'ready' ? '' : loading ? t(current.post ? 'loadingArticle' : 'loading') : t(kind);
  $('#news-status-title').textContent = title;
  $('#news-status-description').textContent = loading || kind === 'ready' ? '' : t(`${kind}Description`);
  $('#news-retry').hidden = kind !== 'error';
  $('#news-first-page').hidden = Boolean(current.post) || !(kind === 'invalid' || kind === 'empty' && current.page > 1);
}

function renderList(data) {
  $('#news-items').replaceChildren(...data.items.map(card));
  $('#news-result-count').textContent = t('results', { count: data.items.length });
  const pagination = $('#news-pagination');
  pagination.hidden = current.page === 1 && !data.hasMore;
  $('#news-page-number').textContent = t('page', { page: current.page });
  for (const [selector, enabled, page] of [
    ['#news-previous', current.page > 1, current.page - 1],
    ['#news-next', data.hasMore, current.page + 1],
  ]) {
    const link = $(selector);
    link.href = urlFor({ ...current, page });
    link.setAttribute('aria-disabled', String(!enabled));
    link.tabIndex = enabled ? 0 : -1;
    link.dataset.page = String(page);
  }
  status(data.items.length ? 'ready' : 'empty');
  if (returnFocusId) {
    const link = [...$('#news-items').querySelectorAll('[data-publication-id]')].find(node => node.dataset.publicationId === returnFocusId);
    link?.focus({ preventScroll: true });
    returnFocusId = null;
  }
}

function renderArticle(post) {
  const article = $('#news-article');
  article.lang = post.language;
  article.hidden = false;
  $('#news-article-meta').replaceChildren(...metadata(post));
  $('#news-article-title').textContent = post.title;
  $('#news-article-summary').textContent = post.summary;
  $('#news-article-body').textContent = post.body;
  const beta = $('#news-article-beta');
  const href = betaRegistrationHref(post, current.language);
  beta.hidden = !href;
  if (href) beta.href = href;
  $('#news-permalink').href = urlFor(current);
  document.title = `${post.title} — Arkya`;
  document.querySelector('meta[name="description"]').content = post.summary;
  status('ready');
}

async function load({ focus = false } = {}) {
  history.replaceState(history.state, '', urlFor(current));
  request?.abort();
  request = new AbortController();
  const thisGeneration = ++generation;
  const snapshot = { ...current };
  renderShell();
  status('loading');
  try {
    if (snapshot.post) {
      if (!uuid.test(snapshot.post)) { status('missing'); return; }
      const post = await client.get(snapshot.post, { signal: request.signal });
      if (thisGeneration !== generation) return;
      renderArticle(post);
      if (focus) $('#news-article-title').focus({ preventScroll: true });
    } else {
      if (snapshot.page < 1) { status('invalid'); return; }
      const result = await client.list({ kind: snapshot.tab, language: snapshot.language,
        offset: (snapshot.page - 1) * pageSize, limit: pageSize }, { signal: request.signal });
      if (thisGeneration !== generation) return;
      renderList(result);
      if (focus) $('#news-results-title').scrollIntoView({ block: 'start', behavior: 'instant' });
    }
  } catch (error) {
    if (thisGeneration !== generation || error.code === 'cancelled') return;
    status(error.code === 'not_found' ? 'missing' : error.code === 'invalid_filters' ? 'invalid' : 'error');
  }
}

$('#news-first-page').addEventListener('click', () => navigate({ ...current, page: 1, post: null }));
$('#news-retry').addEventListener('click', () => void load());

function selectTab(tab) {
  if (current.tab === tab && !current.post) return;
  navigate({ ...current, tab, post: null, page: 1 });
}
tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => selectTab(tab.dataset.kind));
  tab.addEventListener('keydown', event => {
    const target = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 :
      event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index + tabs.length - 1) % tabs.length : -1;
    if (target < 0) return;
    event.preventDefault();
    tabs[target].focus();
    selectTab(tabs[target].dataset.kind);
  });
});

$('#publication-panel').addEventListener('click', event => {
  const link = event.target.closest('a');
  if (!link || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  if (link.getAttribute('aria-disabled') === 'true') { event.preventDefault(); return; }
  if (link.dataset.publicationId) {
    event.preventDefault();
    returnFocusId = link.dataset.publicationId;
    navigate({ ...current, post: link.dataset.publicationId }, { article: true, focus: true });
  } else if (link.dataset.page) {
    event.preventDefault();
    navigate({ ...current, page: Number(link.dataset.page), post: null }, { focus: true });
  } else if (link.id === 'news-back') {
    event.preventDefault();
    if (history.state?.arkyaPublication) history.back();
    else navigate({ ...current, post: null }, { replace: true });
  }
});

window.addEventListener('arkya:languagechange', () => {
  if (syncingLanguage) return;
  returnFocusId = null;
  navigate({ ...current, language: language(), post: null, page: 1 }, { replace: true });
});
window.addEventListener('popstate', () => {
  current = readUrl();
  if (current.language !== language()) {
    syncingLanguage = true;
    window.ArkyaI18n?.setLanguage(current.language);
    syncingLanguage = false;
  }
  void load();
});
window.addEventListener('pagehide', () => { request?.abort(); generation += 1; });
window.addEventListener('pageshow', event => { if (event.persisted) void load(); });
current.language = language();
void load();
