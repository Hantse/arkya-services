import test from 'node:test';
import assert from 'node:assert/strict';
import { categories, entries, reviewedAt } from '../dist/wiki-data.mjs';
import { normalizeSearch, searchEntries, readWikiState, wikiHref } from '../dist/wiki-model.mjs';

const local = (fr, en) => ({ fr, en });
const fixtures = [
  { id: 'sandstone-brick', category: 'recipes', title: local('Brique de grès', 'Sandstone brick'),
    summary: local('Fabriquer une brique avec les matériaux récoltés.', 'Craft a brick from gathered materials.'),
    facts: [{ label: local('Coût optimal', 'Optimal cost'), value: local('Deux morceaux au niveau 200', 'Two pieces at level 200') }],
    sections: [{ title: local('Apprendre la formule', 'Learn the formula'),
      text: local('Le maître maçon enseigne cette recette.', 'The mason trainer teaches this recipe.'),
      list: [local('Récoltez du grès près des rochers.', 'Gather sandstone near rocks.')] }] },
  { id: 'heal', category: 'abilities', title: local('Soin', 'Heal'),
    summary: local('Une capacité du Cleric.', 'A Cleric ability.'),
    sections: [{ title: local('Héritage', 'Inheritance'), text: local('Les rangs appris restent disponibles.', 'Learned ranks remain available.') }] },
  { id: 'miner', category: 'crafting', title: local('Mineur', 'Miner'),
    summary: local('Récoltez la pierre et les minerais.', 'Gather stone and ores.'),
    sections: [{ title: local('Outil', 'Tool'), text: local('Utilisez une pioche.', 'Use a pickaxe.') }] },
];
const ids = results => results.map(entry => entry.id);

test('wiki search ignores accents, case and surrounding whitespace', () => {
  assert.equal(normalizeSearch('  GRÈS ÉCOLE  '), 'gres ecole');
  assert.equal(normalizeSearch('Gre\u0300s'), 'gres');
  assert.deepEqual(ids(searchEntries(fixtures, { query: '  BRIQUE GrEs  ' })), ['sandstone-brick']);
});

test('all search words must match, regardless of their order or position in the article', () => {
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'rochers formule brique' })), ['sandstone-brick']);
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'brique absent' })), []);
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'brique soin' })), []);
});

test('search includes fact labels and values, section headings, body and lists', () => {
  for (const query of ['optimal 200', 'maitre macon', 'apprendre formule', 'recoltez rochers']) {
    assert.deepEqual(ids(searchEntries(fixtures, { query })), ['sandstone-brick'], query);
  }
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'optimal 200 trainer', language: 'en' })), ['sandstone-brick']);
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'inheritance learned', language: 'en' })), ['heal']);
});

test('search follows the selected language while stable identifiers remain searchable', () => {
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'gather ores', language: 'en' })), ['miner']);
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'gather ores', language: 'fr' })), []);
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'miner', language: 'fr' })), ['miner']);
  assert.deepEqual(ids(searchEntries(fixtures, { query: 'soin', language: 'en' })), []);
});

test('category and query filters intersect and preserve input order and catalog data', () => {
  const before = structuredClone(fixtures);
  assert.deepEqual(ids(searchEntries(fixtures, { category: 'recipes', query: 'gres' })), ['sandstone-brick']);
  assert.deepEqual(ids(searchEntries(fixtures, { category: 'abilities', query: 'gres' })), []);
  assert.deepEqual(ids(searchEntries(fixtures, { category: 'unknown' })), []);
  assert.deepEqual(ids(searchEntries(fixtures, { query: ' \t ' })), fixtures.map(entry => entry.id));
  assert.deepEqual(fixtures, before);
});

test('search treats HTML and regular-expression characters as literal text', () => {
  for (const query of ['.*', '[', '<img src=x onerror=alert(1)>', 'brique|soin', '(a+)+$']) {
    assert.deepEqual(searchEntries(fixtures, { query }), [], query);
  }
});

test('wiki URL state defaults safely and only accepts known categories', () => {
  assert.deepEqual(readWikiState('', categories), { category: '', query: '', entry: '' });
  assert.deepEqual(readWikiState('?category=unknown&q=gr%C3%A8s&entry=not-found', categories),
    { category: '', query: 'grès', entry: 'not-found' });
  const category = categories[0].id;
  assert.equal(readWikiState(`?category=${category}`, categories).category, category);
});

test('shared wiki URLs round trip literal characters without introducing parameters or fragments', () => {
  const state = { category: categories[0].id, query: 'grès & entry=other ? # + " <script>', entry: 'heal', language: 'en' };
  const href = wikiHref(state);
  const url = new URL(href, 'https://arkya.online/');
  assert.equal(url.origin, 'https://arkya.online');
  assert.equal(url.pathname, '/wiki.html');
  assert.equal(url.hash, '');
  assert.equal(url.searchParams.get('lang'), 'en');
  assert.equal(url.searchParams.getAll('entry').length, 1);
  assert.deepEqual(readWikiState(url.search, categories), { category: state.category, query: state.query, entry: state.entry });
});

test('legacy skill category links retain their ability filter, search and selected article', () => {
  const state = readWikiState('?lang=en&category=skills&q=heal&entry=heal', categories);
  assert.deepEqual(state, { category: 'abilities', query: 'heal', entry: 'heal' });
  const results = searchEntries(entries, { ...state, language: 'en' });
  assert.ok(results.some(entry => entry.id === 'heal'));
  assert.ok(results.every(entry => entry.category === 'abilities'));
  const href = wikiHref({ ...state, language: 'en' });
  assert.equal(new URL(href, 'https://arkya.online').searchParams.get('category'), 'abilities');
});

test('long search parameters are bounded on reading and creating links', () => {
  const query = 'é'.repeat(500);
  assert.equal(readWikiState(`?q=${encodeURIComponent(query)}`, categories).query, query.slice(0, 120));
  assert.equal(new URL(wikiHref({ query }), 'https://arkya.online').searchParams.get('q'), query.slice(0, 120));
});

test('wiki links use a local destination and a supported language even with hostile values', () => {
  const url = new URL(wikiHref({ entry: '//attacker.invalid/#x', language: 'javascript:alert(1)' }), 'https://arkya.online');
  assert.equal(url.origin, 'https://arkya.online');
  assert.equal(url.pathname, '/wiki.html');
  assert.equal(url.searchParams.get('lang'), 'fr');
  assert.equal(url.searchParams.get('entry'), '//attacker.invalid/#x');
  assert.equal(url.hash, '');
  assert.equal(wikiHref(), 'wiki.html?lang=fr');
});

function assertLocalized(value, label) {
  for (const language of ['fr', 'en']) {
    assert.equal(typeof value?.[language], 'string', `${label}.${language}`);
    assert.ok(value[language].trim(), `${label}.${language} must be nonempty`);
  }
}

test('the published wiki has localized categories and unique, stable identifiers', () => {
  assert.ok(categories.length > 0);
  assert.ok(entries.length > 0);
  assert.equal(new Set(categories.map(category => category.id)).size, categories.length);
  assert.equal(new Set(entries.map(entry => entry.id)).size, entries.length);
  for (const category of categories) {
    assert.match(category.id, /^[a-z0-9]+(?:-[a-z0-9]+)*$/);
    assertLocalized(category.title, `${category.id}.title`);
    assertLocalized(category.description, `${category.id}.description`);
    assert.ok(entries.some(entry => entry.category === category.id), `${category.id} contains articles`);
  }
  for (const id of ['classes', 'recipes', 'abilities']) assert.ok(categories.some(category => category.id === id), `${id} category exists`);
  assert.match(reviewedAt, /^\d{4}-\d{2}-\d{2}$/);
  assert.equal(new Date(`${reviewedAt}T00:00:00Z`).toISOString().slice(0, 10), reviewedAt);
});

test('wiki articles contain complete bilingual content and valid related links', () => {
  const entryIds = new Set(entries.map(entry => entry.id));
  const categoryIds = new Set(categories.map(category => category.id));
  for (const entry of entries) {
    assert.match(entry.id, /^[a-z0-9]+(?:-[a-z0-9]+)*$/);
    assert.ok(categoryIds.has(entry.category), `${entry.id}: known category`);
    assert.ok(['prototype', 'design'].includes(entry.status), `${entry.id}: explicit development status`);
    assertLocalized(entry.title, `${entry.id}.title`);
    assertLocalized(entry.summary, `${entry.id}.summary`);
    assert.ok(entry.sections.length > 0, `${entry.id}: article sections`);
    for (const [index, section] of entry.sections.entries()) {
      assertLocalized(section.title, `${entry.id}.sections[${index}].title`);
      for (const language of ['fr', 'en']) {
        assert.ok(section.text?.[language]?.trim() || section.list?.length, `${entry.id}: section has ${language} content`);
      }
      if (section.text?.fr || section.text?.en) assertLocalized(section.text, `${entry.id}.sections[${index}].text`);
      for (const item of section.list || []) assertLocalized(item, `${entry.id}.sections[${index}].list`);
    }
    for (const fact of entry.facts || []) {
      assertLocalized(fact.label, `${entry.id}.facts.label`);
      assertLocalized(fact.value, `${entry.id}.facts.value`);
    }
    assert.equal(new Set(entry.related || []).size, (entry.related || []).length, `${entry.id}: unique related links`);
    for (const id of entry.related || []) {
      assert.ok(entryIds.has(id), `${entry.id}: missing related article ${id}`);
      assert.notEqual(id, entry.id, `${entry.id}: related link points to another article`);
    }
  }
});

test('every wiki article records a project documentation source without URL or traversal syntax', () => {
  const sources = new Set(entries.map(entry => entry.source));
  for (const source of sources) {
    assert.match(source, /^docs\/[a-zA-Z0-9/_-]+\.md$/);
  }
});

test('real catalog searches expose recipes in French and abilities in English', () => {
  assert.ok(searchEntries(entries, { query: 'brique', category: 'recipes', language: 'fr' }).length > 0);
  assert.ok(searchEntries(entries, { query: 'heal', category: 'abilities', language: 'en' }).length > 0);
});
