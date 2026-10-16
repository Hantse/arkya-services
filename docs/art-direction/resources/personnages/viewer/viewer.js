import { STORAGE_KEY, validateAppearance, parseAppearance, changeRace, randomAppearance } from './appearance.js';

const $ = id => document.getElementById(id);
const model = $('character');
const rootUrl = new URL('../', location.href);
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
let manifest, appearance, activeSource = '', activeScale = '', loading = true, exporting = false, portrait = false, loadRevision = 0;
const paletteFields = { skin: 'skin', hair: 'hairColor', eyes: 'eyeColor', outfit: 'outfitColor' };
const raceIcons = {
  human: '<path d="M9 11C7 2 25 2 23 11v11c0 5-5 9-7 9s-7-4-7-9Z M9 17c-5-4-5 5 0 5m14-5c5-4 5 5 0 5M11 15h3m4 0h3m-8 10h6m-3-10v6h2M7 36c0-6 18-6 18 0"/>',
  elf: '<path d="M9 11C8 1 24 1 23 11v11c0 5-5 10-7 10s-7-5-7-10Z M9 16 1 10l3 12 5 2m14-8 8-6-3 12-5 2M11 15l3 1m4 0 3-1m-8 10h6m-3-9v6h2M7 37c0-6 18-6 18 0"/>'
};

function announce(message) { $('status').textContent = message; }
function race() { return manifest.races.find(item => item.id === appearance.race); }
function paletteEntry(palette) { return manifest.palettes[palette].find(item => item.id === appearance[paletteFields[palette]]); }

function createChoice(label, value, field, className = '') {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = className;
  button.dataset.field = field;
  button.dataset.value = value;
  button.textContent = label;
  button.setAttribute('aria-pressed', 'false');
  button.addEventListener('click', () => {
    if (appearance[field] === value) return;
    appearance = field === 'race' ? changeRace(appearance, value, manifest) : { ...appearance, [field]: value };
    render();
  });
  return button;
}

function buildControls() {
  $('races').replaceChildren(...manifest.races.map(item => {
    const button = createChoice(item.label, item.id, 'race', 'race-choice');
    const label = document.createElement('span');
    label.textContent = item.label;
    const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    icon.setAttribute('viewBox', '0 0 32 40');
    icon.setAttribute('aria-hidden', 'true');
    icon.innerHTML = raceIcons[item.id] || '';
    button.replaceChildren(icon, label);
    return button;
  }));
  $('silhouettes').replaceChildren(...manifest.silhouettes.map(item => createChoice(item.label, item.id, 'silhouette')));
  $('hair-styles').replaceChildren(...manifest.hairStyles.map(item => createChoice(item.label, item.id, 'hairStyle')));
  for (const [name, field] of Object.entries(paletteFields)) {
    $(name).replaceChildren(...manifest.palettes[name].map(item => {
      const button = createChoice('', item.id, field, 'swatch');
      button.title = item.label;
      button.setAttribute('aria-label', item.label);
      button.style.setProperty('--swatch', item.hex);
      return button;
    }));
  }
  for (const id of ['height', 'randomize', 'save', 'restore', 'reset', 'export-json']) $(id).disabled = false;
}

function render() {
  const selectedRace = race();
  document.querySelectorAll('[data-field]').forEach(button => button.setAttribute('aria-pressed', String(appearance[button.dataset.field] === button.dataset.value)));
  $('race-title').textContent = selectedRace.label;
  $('race-description').textContent = selectedRace.description;
  $('height').min = selectedRace.height.min;
  $('height').max = selectedRace.height.max;
  $('height').value = appearance.heightCm;
  $('height').setAttribute('aria-valuetext', `${appearance.heightCm} centimètres`);
  $('height').style.setProperty('--range-fill', `${100 * (appearance.heightCm - selectedRace.height.min) / (selectedRace.height.max - selectedRace.height.min)}%`);
  $('height-output').replaceChildren(document.createTextNode(`${appearance.heightCm} `), Object.assign(document.createElement('span'), { textContent: 'cm' }));
  $('stage-height').textContent = `${appearance.heightCm} cm`;
  $('height-min').textContent = `${selectedRace.height.min} cm`;
  $('height-max').textContent = `${selectedRace.height.max} cm`;
  for (const palette of Object.keys(paletteFields)) $(`${palette}-label`).textContent = paletteEntry(palette).label;
  model.alt = `${selectedRace.label}, silhouette ${manifest.silhouettes.find(item => item.id === appearance.silhouette).label.toLowerCase()}, ${appearance.heightCm} centimètres, cheveux ${paletteEntry('hair').label.toLowerCase()}. Aperçu 3D personnalisable.`;
  const source = new URL(selectedRace.models[appearance.silhouette][appearance.hairStyle], rootUrl).href;
  const ratio = appearance.heightCm / selectedRace.baseHeightCm;
  const scale = `${ratio} ${ratio} ${ratio}`;
  if (source !== activeSource || scale !== activeScale) {
    activeSource = source;
    activeScale = scale;
    loadModel(source, scale);
  } else if (!loading) {
    applyAppearance();
    announce(`${selectedRace.label} · ${appearance.heightCm} cm · Apparence modifiée.`);
  }
}

async function loadModel(source, scale) {
  const revision = ++loadRevision;
  loading = true;
  $('export-glb').disabled = true;
  $('loading').classList.remove('error');
  $('loading-label').textContent = 'Le personnage prend forme…';
  model.showPoster();
  announce('Chargement du personnage…');
  try {
    // This bundled viewer calls its AR updater on post-load scale changes.
    // Set scale during the unloaded phase so it is applied by normal scene setup.
    if (model.scale !== scale) {
      model.src = null;
      await model.updateComplete;
      if (revision !== loadRevision) return;
    }
    model.src = source;
    model.scale = scale;
  } catch (error) {
    if (revision !== loadRevision) return;
    console.error(error);
    announce('Le modèle ne peut pas être chargé. Essayez une autre coiffure ou rechargez la page.');
  }
}

function applyAppearance() {
  if (!model.model) return;
  for (const palette of Object.keys(paletteFields)) {
    const material = model.model.materials.find(item => item.name === manifest.materials[palette]);
    if (!material) throw new Error(`Missing material: ${manifest.materials[palette]}`);
    material.pbrMetallicRoughness.setBaseColorFactor(paletteEntry(palette).hex);
  }
  updateCamera(false);
}

function updateCamera(resetOrbit = true) {
  const height = appearance?.heightCm / 100 || 1.75;
  model.cameraTarget = portrait ? `0m ${height * .91}m 0m` : '0m 0.95m 0m';
  if (resetOrbit) model.cameraOrbit = portrait ? '0deg 88deg 1.1m' : '0deg 88deg 4.2m';
  $('full-view').setAttribute('aria-pressed', String(!portrait));
  $('portrait-view').setAttribute('aria-pressed', String(portrait));
  if (reducedMotion.matches) model.jumpCameraToGoal();
}

function exportBlob(blob, fileName) {
  const href = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = href;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(href), 2000);
}

function restoreSaved(automatic = false) {
  let saved;
  try { saved = localStorage.getItem(STORAGE_KEY); }
  catch { if (!automatic) announce('Le navigateur bloque le stockage local. Vous pouvez exporter votre apparence en JSON.'); return false; }
  if (!saved) { if (!automatic) announce('Aucune apparence enregistrée dans ce navigateur.'); return false; }
  const restored = parseAppearance(saved, manifest);
  if (!restored) { announce('L’apparence enregistrée est incompatible. Personnalisez le modèle puis enregistrez-le à nouveau.'); return false; }
  appearance = restored;
  if (!automatic) render();
  announce('Apparence enregistrée restaurée.');
  return true;
}

$('appearance-form').addEventListener('submit', event => event.preventDefault());
$('height').addEventListener('input', event => { appearance = { ...appearance, heightCm: Number(event.target.value) }; render(); });
$('randomize').addEventListener('click', () => { appearance = randomAppearance(manifest); render(); });
$('reset').addEventListener('click', () => { appearance = { ...manifest.defaults }; portrait = false; model.autoRotate = false; $('rotate').setAttribute('aria-pressed', 'false'); render(); updateCamera(); });
$('restore').addEventListener('click', () => restoreSaved());
$('save').addEventListener('click', () => {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(appearance)); announce('Apparence enregistrée dans ce navigateur.'); }
  catch { announce('Enregistrement local indisponible. Exportez votre apparence en JSON pour la conserver.'); }
});
$('export-json').addEventListener('click', () => {
  exportBlob(new Blob([`${JSON.stringify(appearance, null, 2)}\n`], { type: 'application/json' }), `arkya-${appearance.race}-apparence.json`);
  announce('Apparence exportée en JSON.');
});
$('export-glb').addEventListener('click', async () => {
  if (loading || exporting) return;
  exporting = true;
  $('export-glb').disabled = true;
  // Keep the exported model and its filename tied to the same appearance.
  const exportedRace = appearance.race;
  $('appearance-form').inert = true;
  for (const id of ['randomize', 'reset', 'restore']) $(id).disabled = true;
  announce('Préparation du modèle personnalisé…');
  try {
    applyAppearance();
    await model.updateComplete;
    const blob = await model.exportScene({ binary: true, onlyVisible: true, embedImages: true });
    exportBlob(blob, `arkya-${exportedRace}-personnalise.glb`);
    announce('Modèle personnalisé exporté : taille, couleurs et coiffure incluses.');
  } catch (error) {
    console.error(error);
    announce('L’export du modèle a échoué. Réessayez après le chargement ou conservez l’apparence en JSON.');
  } finally {
    exporting = false;
    $('appearance-form').inert = false;
    for (const id of ['randomize', 'reset', 'restore']) $(id).disabled = false;
    $('export-glb').disabled = loading;
  }
});
$('full-view').addEventListener('click', () => { portrait = false; updateCamera(); });
$('portrait-view').addEventListener('click', () => { portrait = true; updateCamera(); });
$('rotate').addEventListener('click', () => { model.autoRotate = !model.autoRotate; $('rotate').setAttribute('aria-pressed', String(model.autoRotate)); });
reducedMotion.addEventListener('change', () => { if (reducedMotion.matches) { model.autoRotate = false; $('rotate').setAttribute('aria-pressed', 'false'); model.jumpCameraToGoal(); } });
model.addEventListener('progress', event => { $('progress-fill').style.width = `${event.detail.totalProgress * 100}%`; if (event.detail.totalProgress === 1) $('progress-fill').style.width = '0'; });
model.addEventListener('load', event => {
  if (!manifest || model.src !== activeSource || event.detail.url !== activeSource) return;
  try {
    applyAppearance();
    loading = false;
    $('export-glb').disabled = typeof model.exportScene !== 'function';
    model.dismissPoster();
    announce(`${race().label} · ${appearance.heightCm} cm · Aperçu prêt.`);
  } catch (error) {
    console.error(error);
    loading = true;
    $('export-glb').disabled = true;
    announce('Ce modèle ne contient pas toutes les matières personnalisables. Rechargez la page après vérification de la collection.');
  }
});
model.addEventListener('error', () => {
  loading = true;
  $('export-glb').disabled = true;
  $('loading').classList.add('error');
  $('loading-label').textContent = 'Modèle indisponible';
  model.showPoster();
  announce('Le modèle ne peut pas être chargé. Essayez une autre coiffure ou rechargez la page.');
});

try {
  const response = await fetch(new URL('manifest.json', rootUrl));
  if (!response.ok) throw new Error(`Manifest HTTP ${response.status}`);
  manifest = await response.json();
  appearance = validateAppearance(manifest.defaults, manifest);
  if (manifest.version !== 1 || !appearance) throw new Error('Unsupported manifest');
  await customElements.whenDefined('model-viewer');
  restoreSaved(true);
  buildControls();
  render();
  updateCamera();
} catch (error) {
  console.error(error);
  $('loading').classList.add('error');
  $('loading-label').textContent = 'L’atelier est indisponible';
  announce('La collection n’a pas pu être chargée. Ouvrez cet atelier avec le serveur local des ressources, puis rechargez la page.');
}
