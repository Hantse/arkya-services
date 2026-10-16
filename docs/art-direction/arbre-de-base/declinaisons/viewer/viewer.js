const catalogueUrl = new URL('../catalogue.json', location.href);
const tree = document.getElementById('tree');
const status = document.getElementById('status');
const message = document.getElementById('catalogue-message');
const rotate = document.getElementById('rotate');
const refresh = document.getElementById('refresh');
const tabs = [...document.querySelectorAll('[role="tab"]')];
const materialComparison = document.getElementById('material-comparison');
const versionButtons = [...materialComparison.querySelectorAll('[data-version]')];
const labels = {base: 'Arbre de base', forme: 'Forme', matiere: 'Matière'};
const number = new Intl.NumberFormat('fr-FR', {maximumFractionDigits: 2});
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
let items = [], selected = null, category = 'forme', initialFallback = false, catalogueRequest = 0;
let materialVersion = 'painted', modelLoading = false, pendingView = null;

function assetUrl(path) {
  if (typeof path !== 'string' || !path.trim()) return null;
  const url = new URL(path, catalogueUrl);
  return url.origin === location.origin ? url.href : null;
}

function metres(value) {
  return Number.isFinite(Number(value)) && Number(value) > 0 ? `${number.format(Number(value))} m` : '—';
}

function presentation(item) {
  if (selected?.id === item.id && item.category === 'matiere' && materialVersion === 'previous' && item.previousResolvedUrl) {
    return {url: item.previousResolvedUrl, preview: item.previousPreview || item.preview, description: item.previousDescription || item.description};
  }
  return {url: item.resolvedUrl, preview: item.preview, description: item.description};
}

function materialVersionLabel(version) {
  return version === 'previous' ? selected?.previousLabel || 'Première passe' : selected?.versionLabel || 'Peint';
}

function renderComparison() {
  materialComparison.hidden = !(selected?.category === 'matiere' && selected.previousResolvedUrl);
  for (const button of versionButtons) {
    button.textContent = materialVersionLabel(button.dataset.version);
    button.setAttribute('aria-pressed', String(button.dataset.version === materialVersion));
    button.disabled = modelLoading || (button.dataset.version === 'previous' && selected?.previousAvailable === false);
  }
}

function renderSelectedDescription() {
  if (!selected) return;
  const current = presentation(selected);
  document.getElementById('selected-description').textContent = current.description || 'Explorez la silhouette, les branches et le feuillage sous tous les angles.';
  tree.alt = `${selected.label}. ${current.description || 'Arbre stylisé des Terres des Anciens.'}`;
}

function fallbackThumbnail() {
  const box = document.createElement('span');
  box.innerHTML = '<svg viewBox="0 0 52 64" aria-hidden="true"><path d="M24 60V39c-9 4-20-2-17-11-6-7 0-17 9-16C19 1 35 1 37 12c11-1 17 12 9 18 3 9-7 15-16 10v20l8 3H16z"/></svg>';
  return box.firstElementChild;
}

function choice(item) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'choice';
  button.dataset.id = item.id;
  button.setAttribute('aria-pressed', String(selected?.id === item.id));
  button.disabled = item.available === false;
  const thumbnail = document.createElement('span');
  thumbnail.className = 'choice-thumbnail';
  const current = presentation(item);
  const preview = assetUrl(current.preview);
  if (preview) {
    const img = document.createElement('img');
    img.src = preview;
    img.alt = '';
    img.loading = 'lazy';
    img.addEventListener('error', () => thumbnail.replaceChildren(fallbackThumbnail()), {once: true});
    thumbnail.append(img);
  } else thumbnail.append(fallbackThumbnail());
  const copy = document.createElement('span');
  copy.className = 'choice-copy';
  const title = document.createElement('span');
  title.className = 'choice-label';
  title.textContent = item.label;
  const description = document.createElement('span');
  description.className = 'choice-description';
  description.textContent = item.category === 'base' ? 'La silhouette de référence' : current.description || '';
  const dimensions = document.createElement('span');
  dimensions.className = 'choice-size';
  if (item.available === false) {
    dimensions.textContent = 'Bientôt disponible';
    dimensions.classList.add('choice-unavailable');
  } else {
    dimensions.textContent = [metres(item.height) !== '—' ? `H. ${metres(item.height)}` : '', metres(item.width) !== '—' ? `L. ${metres(item.width)}` : ''].filter(Boolean).join(' · ');
  }
  copy.append(title, description, dimensions);
  button.append(thumbnail, copy);
  button.addEventListener('click', () => select(item));
  return button;
}

function renderChoices() {
  document.getElementById('base-choice').replaceChildren(...items.filter(item => item.category === 'base').map(choice));
  document.getElementById('variant-list').replaceChildren(...items.filter(item => item.category === category).map(choice));
  document.getElementById('item-count').textContent = items.length ? `${items.length} arbres` : '';
  for (const kind of ['forme', 'matiere']) document.getElementById(`${kind}-count`).textContent = items.filter(item => item.category === kind).length || '';
  for (const tab of tabs) {
    const active = tab.dataset.category === category;
    tab.setAttribute('aria-selected', String(active));
    tab.tabIndex = active ? 0 : -1;
  }
  document.getElementById('variant-list').setAttribute('aria-labelledby', `tab-${category}`);
  document.getElementById('compare-sizes').hidden = category !== 'forme';
  renderComparison();
}

function resetCamera() {
  pendingView = null;
  rotate.checked = false;
  tree.autoRotate = false;
  tree.cameraTarget = 'auto auto auto';
  tree.cameraOrbit = '35deg 72deg 110%';
  tree.fieldOfView = '35deg';
  tree.jumpCameraToGoal?.();
}

function select(item, allowFallback = false) {
  if (!item?.resolvedUrl || item.available === false) return;
  initialFallback = allowFallback;
  selected = item;
  materialVersion = 'painted';
  pendingView = null;
  const changingSource = tree.src !== item.resolvedUrl;
  modelLoading = changingSource || !tree.loaded;
  if (item.category !== 'base') category = item.category;
  document.getElementById('selected-category').textContent = labels[item.category];
  document.getElementById('selected-name').textContent = item.label;
  renderSelectedDescription();
  for (const key of ['height', 'width', 'depth']) document.getElementById(key).textContent = metres(item[key]);
  status.textContent = modelLoading ? `Chargement de ${item.label}…` : `${item.label} · Prêt à explorer`;
  document.querySelector('.load-progress').classList.toggle('complete', !modelLoading);
  document.getElementById('load-fill').style.width = '0%';
  resetCamera();
  if (changingSource) tree.src = item.resolvedUrl;
  renderChoices();
}

function switchMaterialVersion(version) {
  if (modelLoading || !selected?.previousResolvedUrl || selected.category !== 'matiere' || version === materialVersion) return;
  if (version === 'previous' && selected.previousAvailable === false) return;
  const orbit = tree.getCameraOrbit();
  const target = tree.getCameraTarget();
  materialVersion = version;
  const url = presentation(selected).url;
  pendingView = {
    url,
    orbit: `${orbit.theta}rad ${orbit.phi}rad ${orbit.radius}m`,
    target: `${target.x}m ${target.y}m ${target.z}m`,
    fieldOfView: `${tree.getFieldOfView()}deg`,
    turntable: tree.turntableRotation,
    autoRotate: rotate.checked
  };
  initialFallback = false;
  modelLoading = true;
  tree.autoRotate = false;
  renderSelectedDescription();
  renderChoices();
  document.querySelector('.load-progress').classList.remove('complete');
  document.getElementById('load-fill').style.width = '0%';
  status.textContent = `Chargement de ${selected.label} · ${materialVersionLabel(version)}…`;
  tree.src = url;
}

async function availability(item) {
  try {
    const response = await fetch(item.resolvedUrl, {method: 'HEAD', cache: 'no-store'});
    return response.ok ? true : [404, 410].includes(response.status) ? false : null;
  } catch { return null; }
}

async function loadCatalogue() {
  const request = ++catalogueRequest;
  refresh.disabled = true;
  message.textContent = items.length ? 'Actualisation de la collection…' : 'Chargement de la collection…';
  try {
    const response = await fetch(catalogueUrl, {cache: 'no-store'});
    if (!response.ok) throw new Error('Catalogue unavailable');
    const data = await response.json();
    if (!Array.isArray(data.items)) throw new Error('Invalid catalogue');
    const seen = new Set();
    const next = data.items.filter(item => {
      if (!item || typeof item.id !== 'string' || seen.has(item.id) || !labels[item.category] || typeof item.label !== 'string' || !assetUrl(item.url)) return false;
      seen.add(item.id);
      return true;
    }).map(item => ({...item, resolvedUrl: assetUrl(item.url), previousResolvedUrl: item.category === 'matiere' ? assetUrl(item.previousUrl) : null}));
    if (!next.length) throw new Error('Empty catalogue');
    await Promise.all(next.map(async item => {
      [item.available, item.previousAvailable] = await Promise.all([
        availability(item),
        item.previousResolvedUrl ? availability({resolvedUrl: item.previousResolvedUrl}) : null
      ]);
    }));
    if (request !== catalogueRequest) return;
    items = next;
    message.textContent = items.some(item => item.available === false) ? 'Certaines déclinaisons arrivent bientôt. Actualisez pour les découvrir.' : '';
    const previous = selected && items.find(item => item.id === selected.id && item.available !== false);
    const preferred = items.find(item => item.id === (data.defaultId || 'mystique') && item.available === true);
    const base = items.find(item => item.category === 'base' && item.available !== false);
    const target = previous || preferred || base || items.find(item => item.available !== false);
    if (target) {
      if (!previous || presentation(previous).url !== tree.src) select(target, !previous);
      else { selected = previous; renderSelectedDescription(); renderChoices(); }
    } else {
      renderChoices();
      status.textContent = 'Les arbres sont en préparation. Actualisez la collection dans un instant.';
    }
  } catch {
    if (request !== catalogueRequest) return;
    message.textContent = 'La collection n’est pas accessible. Réessayez avec « Actualiser la collection ».';
    if (!selected) status.textContent = 'La collection n’a pas pu être chargée.';
  } finally {
    if (request === catalogueRequest) refresh.disabled = false;
  }
}

tabs.forEach((tab, index) => {
  tab.addEventListener('click', () => { category = tab.dataset.category; renderChoices(); });
  tab.addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const target = tabs[event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length];
    target.click();
    target.focus();
  });
});
rotate.addEventListener('change', () => {
  if (pendingView) pendingView.autoRotate = rotate.checked;
  tree.autoRotate = pendingView ? false : rotate.checked;
});
versionButtons.forEach(button => button.addEventListener('click', () => switchMaterialVersion(button.dataset.version)));
reducedMotion.addEventListener('change', event => {
  if (event.matches) {
    rotate.checked = false;
    tree.autoRotate = false;
    if (pendingView) pendingView.autoRotate = false;
  }
});
document.getElementById('reset').addEventListener('click', resetCamera);
refresh.addEventListener('click', loadCatalogue);
tree.addEventListener('progress', event => {
  const progress = Math.max(0, Math.min(1, event.detail.totalProgress || 0));
  document.getElementById('load-fill').style.width = `${progress * 100}%`;
});
tree.addEventListener('load', async () => {
  const view = pendingView;
  if (view && tree.src === view.url) {
    tree.cameraOrbit = view.orbit;
    tree.cameraTarget = view.target;
    tree.fieldOfView = view.fieldOfView;
    await tree.updateComplete;
    if (tree.src !== view.url) return;
    if (pendingView === view) {
      tree.jumpCameraToGoal();
      tree.resetTurntableRotation(view.turntable);
      tree.autoRotate = view.autoRotate;
      rotate.checked = view.autoRotate;
      pendingView = null;
    }
  }
  modelLoading = false;
  initialFallback = false;
  renderComparison();
  document.querySelector('.load-progress').classList.add('complete');
  const versionLabel = selected?.previousResolvedUrl ? ` · ${materialVersionLabel(materialVersion)}` : '';
  status.textContent = selected ? `${selected.label}${versionLabel} · Prêt à explorer` : 'Prêt à explorer';
});
tree.addEventListener('error', () => {
  modelLoading = false;
  pendingView = null;
  tree.autoRotate = rotate.checked;
  renderComparison();
  document.querySelector('.load-progress').classList.add('complete');
  const base = items.find(item => item.category === 'base' && item.available !== false);
  if (initialFallback && base && selected?.id !== base.id) { select(base); return; }
  status.textContent = 'Cet arbre n’a pas pu être chargé. Choisissez-en un autre ou actualisez la collection.';
});

await customElements.whenDefined('model-viewer');
loadCatalogue();
