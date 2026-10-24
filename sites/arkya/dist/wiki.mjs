import { categories, entries, reviewedAt } from './wiki-data.mjs';
import { readWikiState, searchEntries, wikiHref } from './wiki-model.mjs';

const $ = selector => document.querySelector(selector);
const language = () => window.ArkyaI18n.language;
const local = value => value?.[language()] || '';
const t = key => window.ArkyaPageTranslations[language()][`wiki.${key}`];
let state = readWikiState(location.search, categories);
const entryById = new Map(entries.map(entry => [entry.id, entry]));
function node(tag, className, text) {
  const result = document.createElement(tag);
  if (className) result.className = className;
  if (text !== undefined) result.textContent = text;
  return result;
}
function link(text, change, className) {
  const result = node('a', className, text);
  result.href = wikiHref({ ...change, language: language() });
  result.dataset.wikiLink = '';
  return result;
}
function status(entry) { return node('span', `status ${entry.status === 'design' ? 'proposal' : 'prototype'}`, t(entry.status)); }
function renderCategories() {
  const selectedCategory = entryById.get(state.entry)?.category ?? state.category;
  const all = { id: '', title: { fr: t('all'), en: t('all') }, mark: '✦' };
  $('#wiki-categories').replaceChildren(...[all, ...categories].map(category => {
    const result = link('', { category: category.id, query: state.query });
    if (selectedCategory === category.id) result.setAttribute('aria-current', 'true');
    const mark = node('span', 'wiki-mark', category.mark);
    mark.setAttribute('aria-hidden', 'true');
    result.append(mark, node('span', '', local(category.title)), node('span', 'wiki-side-count', String(entries.filter(entry => !category.id || entry.category === category.id).length)));
    return result;
  }));
}
function renderIndex() {
  const content = $('#wiki-content');
  const category = categories.find(item => item.id === state.category);
  const results = searchEntries(entries, { ...state, language: language() });
  const heading = node('div', 'wiki-index-heading');
  heading.append(node('h2', '', category ? local(category.title) : t('all')));
  const count = node('p', '', `${results.length} ${t('results')}`);
  count.id = 'wiki-result-count';
  count.setAttribute('role', 'status');
  heading.append(count);
  content.replaceChildren(heading);
  if (category) content.append(node('p', 'wiki-category-intro', local(category.description)));
  if (!state.category && !state.query) {
    const featured = node('div', 'wiki-featured');
    for (const id of ['classes', 'recipes', 'crafting']) {
      const item = categories.find(category => category.id === id);
      if (!item) continue;
      const card = link('', { category: id });
      const mark = node('span', 'wiki-mark', item.mark);
      mark.setAttribute('aria-hidden', 'true');
      card.append(mark, node('h3', '', local(item.title)), node('p', '', local(item.description)));
      featured.append(card);
    }
    content.append(featured);
  }
  if (!results.length) { content.append(empty(t('empty'), t('emptyHelp'))); return; }
  const grid = node('div', 'wiki-entry-grid');
  for (const entry of results) {
    const article = node('article', 'wiki-entry-card');
    const meta = node('div', 'wiki-entry-meta');
    meta.append(node('span', '', local(categories.find(item => item.id === entry.category)?.title)), status(entry));
    const title = node('h3');
    title.append(link(local(entry.title), { ...state, entry: entry.id }));
    article.append(meta, title, node('p', '', local(entry.summary)));
    grid.append(article);
  }
  content.append(grid);
}
function empty(title, help) {
  const result = node('div', 'wiki-empty');
  result.append(node('h2', '', title), node('p', '', help), link(t('reset'), {}, 'button secondary'));
  return result;
}
function renderEntry() {
  const entry = entryById.get(state.entry);
  const content = $('#wiki-content');
  if (!entry) { content.replaceChildren(empty(t('missing'), t('missingHelp'))); return; }
  const category = categories.find(item => item.id === entry.category);
  const article = node('article', 'wiki-article');
  const breadcrumb = node('nav', 'wiki-breadcrumb');
  breadcrumb.setAttribute('aria-label', language() === 'fr' ? 'Fil d’Ariane' : 'Breadcrumb');
  breadcrumb.append(link('Wiki', {}), node('span', '', '/'), link(local(category.title), { category: category.id }), node('span', '', '/'), node('span', '', local(entry.title)));
  const heading = node('h2', '', local(entry.title));
  heading.tabIndex = -1;
  heading.id = 'wiki-article-title';
  article.setAttribute('aria-labelledby', heading.id);
  article.append(breadcrumb, status(entry), heading, node('p', 'wiki-article-lead', local(entry.summary)), node('p', 'wiki-article-status', t(`${entry.status}Note`)));
  if (entry.facts?.length) {
    const facts = node('dl', 'wiki-facts');
    for (const fact of entry.facts) {
      const pair = node('div');
      pair.append(node('dt', '', local(fact.label)), node('dd', '', local(fact.value)));
      facts.append(pair);
    }
    article.append(facts);
  }
  const toc = node('nav', 'wiki-toc');
  toc.setAttribute('aria-label', t('contents'));
  toc.append(node('strong', '', t('contents')));
  entry.sections.forEach((section, index) => {
    const anchor = node('a', '', local(section.title));
    anchor.href = `#section-${index}`;
    toc.append(anchor);
  });
  article.append(toc);
  entry.sections.forEach((section, index) => {
    const block = node('section', 'wiki-section');
    block.id = `section-${index}`;
    block.append(node('h3', '', local(section.title)));
    if (local(section.text)) block.append(node('p', '', local(section.text)));
    if (section.list?.length) {
      const list = node('ul');
      list.append(...section.list.map(value => node('li', '', local(value))));
      block.append(list);
    }
    article.append(block);
  });
  if (entry.category === 'classes') {
    const grimoire = node('a', 'text-link', `${t('classBook')} ↗`);
    grimoire.href = `classes.html?lang=${language()}&class=${encodeURIComponent(entry.id)}#grimoire`;
    article.append(grimoire);
  }
  const date = new Intl.DateTimeFormat(language() === 'fr' ? 'fr-FR' : 'en-GB', { dateStyle: 'long', timeZone: 'UTC' }).format(new Date(`${reviewedAt}T00:00:00Z`));
  article.append(node('p', 'wiki-reviewed', `${t('updated')} ${date}`));
  if (entry.related?.length) {
    const related = node('section', 'wiki-related');
    const links = node('div');
    for (const id of entry.related) {
      const item = entryById.get(id);
      if (item) links.append(link(local(item.title), { entry: id }));
    }
    related.append(node('h3', '', t('related')), links);
    article.append(related);
  }
  article.append(link(`← ${t('back')}`, { ...state, entry: '' }, 'text-link wiki-back'));
  content.replaceChildren(article);
}
function render() {
  $('#wiki-search').value = state.query;
  $('#wiki-clear').hidden = !state.query;
  renderCategories();
  if (state.entry) renderEntry(); else renderIndex();
  const selected = entryById.get(state.entry);
  const title = selected ? `${local(selected.title)} — Wiki Arkya` : window.ArkyaPageTranslations[language()]['page.title'];
  document.title = title;
  document.querySelector('meta[property="og:title"]').content = title;
  document.querySelector('meta[name="description"]').content = selected ? local(selected.summary) : window.ArkyaPageTranslations[language()]['meta.description'];
  document.querySelector('meta[property="og:description"]').content = document.querySelector('meta[name="description"]').content;
}
function navigate(next, { replace = false, focus = false } = {}) {
  state = next;
  history[replace ? 'replaceState' : 'pushState'](null, '', wikiHref({ ...state, language: language() }));
  render();
  if (focus) {
    const target = $('#wiki-content h2');
    target?.setAttribute('tabindex', '-1');
    target?.focus({ preventScroll: true });
    $('#wiki-content').scrollIntoView({ block: 'start', behavior: 'instant' });
  }
}
document.addEventListener('click', event => {
  const anchor = event.target.closest('a[data-wiki-link]');
  if (!anchor || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  navigate(readWikiState(new URL(anchor.href).search, categories), { focus: true });
});
$('#wiki-search').addEventListener('input', event => navigate({ ...state, query: event.target.value.slice(0, 120), entry: '' }, { replace: true }));
$('#wiki-search-form').addEventListener('submit', event => { event.preventDefault(); navigate({ ...state, entry: '' }, { focus: true }); });
$('#wiki-clear').addEventListener('click', () => { navigate({ ...state, query: '', entry: '' }, { replace: true }); $('#wiki-search').focus(); });
window.addEventListener('popstate', () => {
  state = readWikiState(location.search, categories);
  const requested = new URLSearchParams(location.search).get('lang');
  if (['fr', 'en'].includes(requested) && requested !== language()) window.ArkyaI18n.setLanguage(requested);
  else render();
});
window.addEventListener('arkya:languagechange', render);
render();
