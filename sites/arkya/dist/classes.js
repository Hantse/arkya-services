(() => {
  'use strict';
  const classes = window.ArkyaClasses;
  const $ = (selector) => document.querySelector(selector);
  const $$ = (selector) => [...document.querySelectorAll(selector)];
  const language = () => window.ArkyaI18n.language;
  const t = (key) => window.ArkyaPageTranslations[language()][key];
  const local = (value) => value[language()];
  const colors = { warrior: '#d4a17f', scout: '#a5bf9a', mage: '#baa9dd', cleric: '#dac48e', druid: '#8dbeb0' };
  const paths = {
    warrior: '<path d="m21 3 7 7-13 13-7-7Z M5 19l8 8M4 28l5-5M18 6l7 7"/>',
    scout: '<path d="M9 3q27 13 0 26l6-13ZM4 16h25m-5-4 5 4-5 4"/>',
    mage: '<path d="m16 2 9 14-9 14-9-14ZM7 16h18M16 2v28M2 7l3 2m22 14 3 2"/>',
    cleric: '<path d="M16 3v26M3 16h26M9 9l14 14M23 9 9 23"/><circle cx="16" cy="16" r="9"/>',
    druid: '<path d="M27 4C7 3 2 14 7 24c9 7 21-1 20-20ZM4 29 23 9M9 24l-1-8m6 3 8-1"/>',
  };
  const query = new URLSearchParams(location.search);
  let activeClass = classes.find((item) => item.id === query.get('class')) || classes[0];
  let level = Number(query.get('level'));
  if (!Number.isInteger(level) || level < 1 || level > 10) level = 5;
  let clericLevel = 5;

  function node(tag, className, text) {
    const result = document.createElement(tag);
    if (className) result.className = className;
    if (text !== undefined) result.textContent = text;
    return result;
  }

  function saveBookSelection() {
    const url = new URL(location.href);
    url.searchParams.set('class', activeClass.id);
    url.searchParams.set('level', level);
    url.searchParams.set('lang', language());
    history.replaceState(null, '', url);
  }

  for (const item of classes) {
    const button = node('button', 'class-choice');
    button.type = 'button';
    button.dataset.classId = item.id;
    button.style.setProperty('--class-color', colors[item.id]);
    button.setAttribute('aria-controls', 'class-sheet');
    const icon = node('span', 'class-sigil');
    icon.setAttribute('aria-hidden', 'true');
    icon.innerHTML = `<svg viewBox="0 0 32 32">${paths[item.id]}</svg>`;
    const label = node('span');
    label.append(node('strong', '', item.name), node('small', '', local(item.role)));
    button.append(icon, label);
    button.addEventListener('click', () => { activeClass = item; saveBookSelection(); renderBook(); });
    $('#class-picker').append(button);
  }

  function renderBook() {
    $$('.class-choice').forEach((button) => {
      const item = classes.find((entry) => entry.id === button.dataset.classId);
      button.setAttribute('aria-pressed', String(item.id === activeClass.id));
      button.querySelector('small').textContent = local(item.role);
    });
    $('#class-sheet').style.setProperty('--class-color', colors[activeClass.id]);
    $('#class-name').textContent = activeClass.name;
    $('#class-role').textContent = local(activeClass.role);
    $('#class-description').textContent = local(activeClass.description);
    $('#class-note').textContent = local(activeClass.note);
    $('#class-level').value = level;
    $('#class-level-value').value = level;
    $('#class-level').setAttribute('aria-valuetext', `${activeClass.name} · ${t('ui.level')} ${level}`);
    const available = activeClass.spells.filter((spell) => spell.level <= level).length;
    const inherited = activeClass.spells.filter((spell) => spell.rank2 && spell.rank2 <= level).length;
    $('#spell-summary').textContent = `${activeClass.name} · ${t('ui.level')} ${level} — ${available} ${t('ui.available')} · ${inherited} ${t('ui.heirCount')}`;
    const fragment = document.createDocumentFragment();
    for (const spell of activeClass.spells) {
      const isAvailable = level >= spell.level;
      const isRank2 = spell.rank2 !== null && level >= spell.rank2;
      const row = node('article', `spell-row${isAvailable ? ' is-available' : ''}`);
      row.dataset.spell = spell.id;
      const badge = node('div', 'spell-level');
      badge.append(node('small', '', t('ui.level')), node('strong', '', spell.level));
      const body = node('div', 'spell-body');
      const top = node('div', 'spell-top');
      top.append(node('h4', '', local(spell.name)), node('span', 'spell-rank', isAvailable ? `${t('ui.originRank')} · ${isRank2 ? 'R2' : 'R1'}` : t('ui.future')));
      const unlocks = node('div', 'spell-unlocks');
      unlocks.append(node('span', '', `${t('ui.unlock')} · ${t('ui.level')} ${spell.level}`));
      if (spell.rank2) unlocks.append(node('span', '', `R2 · ${t('ui.level')} ${spell.rank2}`));
      const inheritance = spell.rank2 ? (isRank2 ? t('ui.inherited') : `${t('ui.inheritAt')} ${spell.rank2}`) : t('ui.reserved');
      body.append(top, node('p', 'spell-description', local(spell.description)), unlocks, node('p', `inherit-tag${spell.rank2 ? '' : ' reserved'}`, inheritance));
      if (spell.confirmed) body.append(node('p', 'heal-confirmed', t('ui.confirmedHeal')));
      row.append(badge, body);
      fragment.append(row);
    }
    $('#spell-list').replaceChildren(fragment);
  }
  $('#class-level').addEventListener('input', (event) => {
    level = Number(event.target.value);
    saveBookSelection();
    renderBook();
  });

  function renderInheritance() {
    const learnedR2 = clericLevel === 5;
    $$('[data-cleric-level]').forEach((button) => button.setAttribute('aria-pressed', String(Number(button.dataset.clericLevel) === clericLevel)));
    $('#inherit-explanation').textContent = t(learnedR2 ? 'ui.cleric5' : 'ui.cleric1');
    $('#cleric-rank').textContent = learnedR2 ? 'R2' : 'R1';
    $('#cleric-spell').textContent = t(learnedR2 ? 'ui.heal2' : 'ui.heal1');
    $('#warrior-rank').textContent = learnedR2 ? 'R1' : '—';
    $('#warrior-rank').classList.toggle('is-unavailable', !learnedR2);
    $('#warrior-spell').textContent = t(learnedR2 ? 'ui.heal1' : 'ui.noHeal');
  }
  $$('[data-cleric-level]').forEach((button) => button.addEventListener('click', () => {
    clericLevel = Number(button.dataset.clericLevel);
    renderInheritance();
  }));

  for (const item of classes) {
    const row = node('div', 'rating-input');
    const label = node('label');
    const unlocked = node('input');
    unlocked.type = 'checkbox';
    unlocked.id = `unlocked-${item.id}`;
    label.htmlFor = unlocked.id;
    label.append(unlocked, node('span', '', item.name));
    const input = node('input');
    input.type = 'number';
    input.min = '1'; input.max = '10'; input.step = '1';
    input.id = `rating-${item.id}`;
    unlocked.addEventListener('change', () => { input.disabled = !unlocked.checked; renderRating(); });
    input.addEventListener('input', renderRating);
    input.addEventListener('change', () => {
      input.value = String(Math.min(10, Math.max(1, Math.round(Number(input.value) || 1))));
      renderRating();
    });
    row.append(label, input);
    $('#rating-inputs').append(row);
  }
  function resetRating() {
    for (const item of classes) {
      const enabled = item.id === 'warrior' || item.id === 'cleric';
      $(`#unlocked-${item.id}`).checked = enabled;
      $(`#rating-${item.id}`).disabled = !enabled;
      $(`#rating-${item.id}`).value = item.id === 'warrior' ? 10 : item.id === 'cleric' ? 5 : 1;
    }
    renderRating();
  }
  function renderRating() {
    const values = [];
    let valid = true;
    for (const item of classes) {
      const enabled = $(`#unlocked-${item.id}`);
      const input = $(`#rating-${item.id}`);
      enabled.setAttribute('aria-label', `${t('ui.unlockClass')} ${item.name}`);
      input.setAttribute('aria-label', `${t('ui.ratingLevel')} ${item.name}`);
      input.setAttribute('aria-invalid', String(enabled.checked && (!input.value || !input.validity.valid)));
      if (enabled.checked) {
        if (!input.value || !input.validity.valid) valid = false;
        values.push(Number(input.value));
      }
    }
    const sum = values.reduce((total, current) => total + current, 0);
    $('#rating-value').value = values.length && valid ? new Intl.NumberFormat(language(), { maximumFractionDigits: 2 }).format(sum / values.length) : '—';
    $('#rating-calculation').textContent = values.length ? (valid ? `(${values.join(' + ')}) ÷ ${values.length}` : `${t('ui.level')} : 1–10`) : t('ui.emptyRating');
  }
  $('#reset-rating').addEventListener('click', resetRating);

  function setMenu(open) {
    $('#mobile-menu').hidden = !open;
    $('.menu-toggle').setAttribute('aria-expanded', String(open));
    $('.menu-toggle').setAttribute('aria-label', t(open ? 'ui.menuClose' : 'ui.menuOpen'));
  }
  $('.menu-toggle').addEventListener('click', () => setMenu($('#mobile-menu').hidden));
  $$('#mobile-menu a').forEach((link) => link.addEventListener('click', () => setMenu(false)));
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !$('#mobile-menu').hidden) { setMenu(false); $('.menu-toggle').focus(); }
  });
  window.addEventListener('arkya:languagechange', () => {
    renderBook(); renderInheritance(); renderRating(); setMenu(!$('#mobile-menu').hidden);
  });
  renderBook(); renderInheritance(); resetRating(); setMenu(false);
})();
