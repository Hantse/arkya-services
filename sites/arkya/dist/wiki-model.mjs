export function normalizeSearch(value) {
  return String(value).normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
}

export function searchEntries(entries, { query = '', category = '', language = 'fr' } = {}) {
  const terms = normalizeSearch(query).split(/\s+/).filter(Boolean);
  const local = value => value?.[language] || '';
  return entries.filter(entry => {
    if (category && entry.category !== category) return false;
    const content = normalizeSearch([entry.id, local(entry.title), local(entry.summary),
      ...(entry.facts || []).flatMap(fact => [local(fact.label), local(fact.value)]),
      ...entry.sections.flatMap(section => [local(section.title), local(section.text), ...(section.list || []).map(local)])].join(' '));
    return terms.every(term => content.includes(term));
  });
}

export function readWikiState(search, categories) {
  const params = new URLSearchParams(search);
  const category = params.get('category') === 'skills' ? 'abilities' : params.get('category');
  return {
    category: categories.some(item => item.id === category) ? category : '',
    query: (params.get('q') || '').slice(0, 120),
    entry: params.get('entry') || '',
  };
}

export function wikiHref({ category = '', query = '', entry = '', language = 'fr' } = {}) {
  const params = new URLSearchParams({ lang: language === 'en' ? 'en' : 'fr' });
  if (category) params.set('category', category);
  if (query) params.set('q', query.slice(0, 120));
  if (entry) params.set('entry', entry);
  return `wiki.html?${params}`;
}
