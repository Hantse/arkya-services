'use strict';

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];
const messages = {
  fr: {
    statuses: { approved: 'Référence validée', proposal: 'Proposition', exploration: 'Exploration', prototype: 'Prototype Unity' },
    categories: { univers: 'Univers', vegetation: 'Végétation', mineraux: 'Minéraux', magie: 'Magie & outils', personnages: 'Personnages', architecture: 'Architecture', interfaces: 'Interfaces' },
    directions: {
      aube: { title: 'Les Royaumes de l’Aube', alt: 'Concept généré d’un village chaleureux aux toitures ocre' },
      ether: { title: 'Les Cités d’Éther', alt: 'Concept généré d’une cité de pierre claire, de bronze et de cristaux' },
      anciens: { title: 'Les Terres des Anciens', alt: 'Concept généré d’un village installé entre des arbres monumentaux' },
    },
    huds: { cuivre: 'Encre & cuivre', carnet: 'Carnet d’exploration', mana: 'Pierre de mana' },
    trees: { moussu: 'Ancien moussu', mystique: 'Mystique', brule: 'Brûlé' },
    ambience: 'Ambiance sélectionnée : {title}. L’image d’accueil a été mise à jour.',
    treeAlt: 'Arbre de référence peint V3 — {title}',
    hudAlt: 'Proposition du HUD {title}, version 3',
    enlargeHud: 'Agrandir le thème {title}',
    menuOpen: 'Ouvrir le menu', menuClose: 'Fermer le menu',
    enlargeArt: 'Agrandir : {title} — {status}',
    countOne: '{count} création', countMany: '{count} créations',
    progress: '{visible} sur {count} créations', position: '{index} sur {count} : {title}',
    artOrigin: 'Étude artistique',
    credit: 'Bases Rain / Snow — Blender Studio / Blender Foundation, CC BY 4.0. Adaptations Arkya ; crédits détaillés en pied de page.',
    hudOrigin: 'Concept généré',
    hudDescription: 'Habillage proposé pour le HUD d’exploration. Maquette générée : les textes, personnages et fonctionnalités montrés sont illustratifs.',
    loading: 'Chargement de l’atlas…', unavailable: 'Atlas indisponible',
  },
  en: {
    statuses: { approved: 'Approved reference', proposal: 'Proposal', exploration: 'Exploration', prototype: 'Unity prototype' },
    categories: { univers: 'World', vegetation: 'Vegetation', mineraux: 'Minerals', magie: 'Magic & tools', personnages: 'Characters', architecture: 'Architecture', interfaces: 'Interfaces' },
    directions: {
      aube: { title: 'The Realms of Dawn', alt: 'Generated concept of a welcoming village with ochre rooftops' },
      ether: { title: 'The Aether Cities', alt: 'Generated concept of a city of pale stone, bronze and crystals' },
      anciens: { title: 'The Lands of the Ancients', alt: 'Generated concept of a village nestled among monumental trees' },
    },
    huds: { cuivre: 'Ink & copper', carnet: 'Explorer’s journal', mana: 'Mana stone' },
    trees: { moussu: 'Ancient moss', mystique: 'Mystic', brule: 'Burnt' },
    ambience: 'Selected atmosphere: {title}. The cover image has been updated.',
    treeAlt: 'Painted V3 reference tree — {title}',
    hudAlt: 'Proposed {title} HUD, version 3',
    enlargeHud: 'Enlarge the {title} theme',
    menuOpen: 'Open menu', menuClose: 'Close menu',
    enlargeArt: 'Enlarge: {title} — {status}',
    countOne: '{count} artwork', countMany: '{count} artworks',
    progress: '{visible} of {count} artworks', position: '{index} of {count}: {title}',
    artOrigin: 'Art study',
    credit: 'Rain / Snow bases — Blender Studio / Blender Foundation, CC BY 4.0. Adapted for Arkya; full credits in the footer.',
    hudOrigin: 'Generated concept',
    hudDescription: 'Proposed theme for the exploration HUD. Generated mockup: the text, characters and features shown are illustrative.',
    loading: 'Loading the atlas…', unavailable: 'Atlas unavailable',
  },
};
const language = () => window.ArkyaI18n.language;
const labels = () => messages[language()];
const t = (key, values = {}) => labels()[key].replace(/\{(\w+)\}/g, (_, name) => String(values[name] ?? ''));
const state = { items: [], english: {}, filtered: [], category: 'all', status: 'all', search: '', visible: 18, currentHud: 'cuivre', currentDirection: 'anciens', currentTree: 'moussu', dialogItems: [], dialogIndex: 0, lastFocus: null, loadStatus: 'loading' };
const normalize = (value) => String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();

function localizeItem(item) {
  if (item.hudKey) return { ...item, title: `${labels().huds[item.hudKey]} — HUD V3`, collection: labels().categories.interfaces, origin: t('hudOrigin'), description: t('hudDescription') };
  return language() === 'en' ? { ...item, ...state.english[item.id] } : item;
}

function refreshPresentation() {
  const direction = labels().directions[state.currentDirection];
  $('#hero-image').alt = direction.alt;
  $('#hero-caption').textContent = direction.title;
  $('#tree-image').alt = t('treeAlt', { title: labels().trees[state.currentTree] });
  const hudTitle = labels().huds[state.currentHud];
  $('#hud-image').alt = t('hudAlt', { title: hudTitle });
  $('#hud-caption').textContent = `${hudTitle} — V3`;
  $('#enlarge-hud').setAttribute('aria-label', t('enlargeHud', { title: hudTitle }));
  $('.menu-toggle').setAttribute('aria-label', t($('#mobile-menu').hidden ? 'menuOpen' : 'menuClose'));
  $('#direction-announcement').textContent = '';
}

function updatePressed(selector, activeButton) {
  $$(selector).forEach((button) => {
    const selected = button === activeButton;
    button.classList.toggle('active', selected);
    button.setAttribute('aria-pressed', String(selected));
  });
}

$$('[data-direction]').forEach((button) => button.addEventListener('click', () => {
  state.currentDirection = button.dataset.direction;
  const direction = labels().directions[state.currentDirection];
  $('#hero-image').src = `assets/${state.currentDirection}.webp`;
  refreshPresentation();
  updatePressed('[data-direction]', button);
  $('#direction-announcement').textContent = t('ambience', { title: direction.title });
  $('#accueil').scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
}));

$$('[data-tree]').forEach((button) => button.addEventListener('click', () => {
  state.currentTree = button.dataset.tree;
  $('#tree-image').src = `assets/tree-${state.currentTree}.webp`;
  refreshPresentation();
  updatePressed('[data-tree]', button);
}));

$$('[data-hud]').forEach((button) => button.addEventListener('click', () => {
  state.currentHud = button.dataset.hud;
  $('#hud-image').src = `assets/hud-${state.currentHud}.webp`;
  refreshPresentation();
  updatePressed('[data-hud]', button);
}));

function setMenu(open) {
  $('#mobile-menu').hidden = !open;
  $('.menu-toggle').setAttribute('aria-expanded', String(open));
  $('.menu-toggle').setAttribute('aria-label', t(open ? 'menuClose' : 'menuOpen'));
}
$('.menu-toggle').addEventListener('click', () => setMenu($('#mobile-menu').hidden));
$$('#mobile-menu a').forEach((link) => link.addEventListener('click', () => setMenu(false)));
document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && !$('#mobile-menu').hidden) { setMenu(false); $('.menu-toggle').focus(); }
});

function writeFilterUrl() {
  const url = new URL(location.href);
  for (const [key, value] of Object.entries({ category: state.category, status: state.status, q: state.search })) {
    if (!value || value === 'all') url.searchParams.delete(key);
    else url.searchParams.set(key, value);
  }
  history.replaceState(null, '', url);
}

function applyFilters({ reset = true, updateUrl = true } = {}) {
  if (reset) state.visible = 18;
  const terms = normalize(state.search).split(/\s+/).filter(Boolean);
  state.filtered = state.items.filter((item) => {
    const matchesCategory = state.category === 'all' || item.category === state.category;
    const matchesStatus = state.status === 'all' || item.status === state.status;
    const english = state.english[item.id];
    const text = normalize([item.title, item.collection, item.description, messages.fr.categories[item.category], ...(item.tags || []), english?.title, english?.collection, english?.description, messages.en.categories[item.category], ...(english?.tags || [])].join(' '));
    return matchesCategory && matchesStatus && terms.every((term) => text.includes(term));
  });
  updatePressed('[data-category]', $(`[data-category="${state.category}"]`));
  $('#gallery-status').value = state.status;
  $('#gallery-search').value = state.search;
  renderGallery();
  if (updateUrl) writeFilterUrl();
}

function makeCard(item) {
  const content = localizeItem(item);
  const card = document.createElement('button');
  card.className = 'art-card';
  card.setAttribute('aria-label', t('enlargeArt', { title: content.title, status: labels().statuses[item.status] }));
  const figure = document.createElement('div');
  figure.className = 'card-image';
  const img = document.createElement('img');
  img.src = item.image;
  img.alt = content.title;
  img.width = item.width;
  img.height = item.height;
  img.loading = 'lazy';
  img.decoding = 'async';
  const badge = document.createElement('span');
  badge.className = `status ${item.status}`;
  badge.textContent = labels().statuses[item.status];
  const arrow = document.createElement('span');
  arrow.className = 'card-arrow';
  arrow.setAttribute('aria-hidden', 'true');
  arrow.textContent = '↗';
  figure.append(img, badge, arrow);
  const meta = document.createElement('div');
  meta.className = 'card-meta';
  const category = document.createElement('span');
  category.textContent = content.collection || labels().categories[item.category];
  const title = document.createElement('h3');
  title.textContent = content.title;
  meta.append(category, title);
  card.append(figure, meta);
  card.addEventListener('click', () => openArt(state.filtered, state.filtered.indexOf(item)));
  return card;
}

function renderGallery() {
  const visibleItems = state.filtered.slice(0, state.visible);
  const fragment = document.createDocumentFragment();
  visibleItems.forEach((item) => fragment.append(makeCard(item)));
  $('#gallery-grid').replaceChildren(fragment);
  const count = state.filtered.length;
  $('#gallery-count').textContent = t(count === 1 ? 'countOne' : 'countMany', { count });
  $('#gallery-empty').hidden = count !== 0;
  $('#load-more').hidden = visibleItems.length >= count;
  $('#gallery-progress').textContent = count ? t('progress', { visible: visibleItems.length, count }) : '';
}

$$('[data-category]').forEach((button) => button.addEventListener('click', () => { state.category = button.dataset.category; applyFilters(); }));
$('#gallery-status').addEventListener('change', (event) => { state.status = event.target.value; applyFilters(); });
let searchTimer;
$('#gallery-search').addEventListener('input', (event) => {
  state.search = event.target.value;
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => applyFilters(), 120);
});
$('#load-more').addEventListener('click', () => {
  const previousVisible = state.visible;
  state.visible += 18;
  renderGallery();
  $$('#gallery-grid .art-card')[previousVisible]?.focus({ preventScroll: true });
});
$('#reset-filters').addEventListener('click', () => { state.category = 'all'; state.status = 'all'; state.search = ''; applyFilters(); $('#gallery-search').focus(); });
$$('[data-quick-category], [data-quick-status]').forEach((link) => link.addEventListener('click', () => {
  state.category = link.dataset.quickCategory || 'all';
  state.status = link.dataset.quickStatus || 'all';
  state.search = '';
  applyFilters();
}));

const dialog = $('#art-dialog');
function openArt(items, index) {
  if (!items.length || index < 0) return;
  state.dialogItems = items;
  state.dialogIndex = index;
  state.lastFocus = document.activeElement;
  showCurrentArt();
  dialog.showModal();
  document.body.classList.add('modal-open');
  $('.dialog-close').focus();
}
function showCurrentArt() {
  const item = localizeItem(state.dialogItems[state.dialogIndex]);
  $('#dialog-image').src = item.image;
  $('#dialog-image').alt = item.title;
  $('#dialog-title').textContent = item.title;
  $('#dialog-description').textContent = item.description;
  $('#dialog-status').className = `status ${item.status}`;
  $('#dialog-status').textContent = labels().statuses[item.status];
  const origin = item.origin || t('artOrigin');
  $('#dialog-source').textContent = `${item.collection} · ${origin}${item.credit ? ` · ${t('credit')}` : ''}`;
  $('#dialog-original').href = item.image;
  $('.dialog-prev').hidden = state.dialogItems.length < 2;
  $('.dialog-next').hidden = state.dialogItems.length < 2;
  $('#dialog-announcement').textContent = t('position', { index: state.dialogIndex + 1, count: state.dialogItems.length, title: item.title });
}
function moveArt(delta) {
  state.dialogIndex = (state.dialogIndex + delta + state.dialogItems.length) % state.dialogItems.length;
  showCurrentArt();
}
$('.dialog-close').addEventListener('click', () => dialog.close());
$('.dialog-prev').addEventListener('click', () => moveArt(-1));
$('.dialog-next').addEventListener('click', () => moveArt(1));
dialog.addEventListener('click', (event) => { if (event.target === dialog) { const bounds = dialog.getBoundingClientRect(); if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close(); } });
dialog.addEventListener('keydown', (event) => {
  if (event.key === 'ArrowLeft') { event.preventDefault(); moveArt(-1); }
  if (event.key === 'ArrowRight') { event.preventDefault(); moveArt(1); }
});
dialog.addEventListener('close', () => { document.body.classList.remove('modal-open'); state.lastFocus?.focus({ preventScroll: true }); });
$('#enlarge-hud').addEventListener('click', () => {
  const items = Object.keys(labels().huds).map((key) => ({ hudKey: key, category: 'interfaces', status: 'proposal', image: `assets/hud-${key}.webp` }));
  openArt(items, Object.keys(labels().huds).indexOf(state.currentHud));
});

async function loadGallery() {
  $('#gallery-error').hidden = true;
  $('#gallery-empty').hidden = true;
  state.loadStatus = 'loading';
  $('#gallery-count').textContent = t('loading');
  try {
    const [items, english] = await Promise.all(['gallery.json', 'gallery.en.json'].map(async (url) => {
      const response = await fetch(url);
      if (!response.ok) throw new Error('Gallery unavailable');
      return response.json();
    }));
    if (!Array.isArray(items) || !english || items.some((item) => !english[item.id])) throw new Error('Gallery translation unavailable');
    state.items = items;
    state.english = english;
    state.loadStatus = 'ready';
    const parameters = new URLSearchParams(location.search);
    state.category = Object.hasOwn(labels().categories, parameters.get('category')) ? parameters.get('category') : 'all';
    state.status = Object.hasOwn(labels().statuses, parameters.get('status')) ? parameters.get('status') : 'all';
    state.search = parameters.get('q') || '';
    applyFilters({ updateUrl: false });
  } catch {
    state.loadStatus = 'error';
    $('#gallery-error').hidden = false;
    $('#gallery-count').textContent = t('unavailable');
  }
}
$('#retry-gallery').addEventListener('click', loadGallery);
window.addEventListener('arkya:languagechange', () => {
  refreshPresentation();
  if (state.loadStatus === 'ready') {
    applyFilters({ reset: false, updateUrl: false });
  } else $('#gallery-count').textContent = t(state.loadStatus === 'error' ? 'unavailable' : 'loading');
  if (dialog.open) showCurrentArt();
});
refreshPresentation();
loadGallery();
