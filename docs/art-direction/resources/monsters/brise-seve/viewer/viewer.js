const $ = id => document.getElementById(id);
const model = $('monster');
const rootUrl = new URL('../', location.href);
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
const clipLabels = { Idle: 'Repos', Walk: 'Marche', ChargeAnticipation: 'Préparation', Charge: 'Charge', Attack: 'Attaque', Hit: 'Impact', Death: 'Mort' };
const clipDescriptions = {
  Idle: 'Respiration et mouvements de repos : observer le poids du corps et la tête.',
  Walk: 'Cycle de marche sur place : observer les appuis et le balancement des épaules.',
  ChargeAnticipation: 'Préparation de la charge : observer l’abaissement de la tête et la prise d’appui.',
  Charge: 'Cycle de charge sur place : observer l’impulsion et la tenue de la silhouette.',
  Attack: 'Attaque : observer l’élan, le geste et le retour à l’appui. Lecture unique.',
  Hit: 'Réaction à un impact : observer le recul du corps et la récupération. Lecture unique.',
  Death: 'Chute au sol : observer le transfert de poids et la pose finale. Lecture unique.'
};
const clipOrder = ['Idle', 'Walk', 'ChargeAnticipation', 'Charge', 'Attack', 'Hit', 'Death'];
const finiteClips = new Set(['ChargeAnticipation', 'Attack', 'Hit', 'Death']);
let manifest = null;
let loaded = false;
let playing = !reducedMotion.matches;
let selectedClip = '';
let loadTimeout;
let seeking = false;

function announce(message) { $('status').textContent = message; }
function number(value, digits = 2) { return new Intl.NumberFormat('fr-BE', { maximumFractionDigits: digits }).format(value); }
function seconds(value) { return Number.isFinite(value) ? value.toFixed(2).replace('.', ',') : '0,00'; }
function assetUrl(path) {
  const url = new URL(path, rootUrl);
  if (manifest?.revision) url.searchParams.set('v', manifest.revision);
  return url.href;
}

function setControls(enabled) {
  for (const id of ['reset-camera', 'rotate']) $(id).disabled = !enabled;
  const animated = enabled && (model.availableAnimations?.length || 0) > 0;
  for (const id of ['play', 'speed', 'time']) $(id).disabled = !animated;
  document.querySelectorAll('[data-animation]').forEach(button => { button.disabled = !animated; });
}

function updatePlaybackControl() {
  $('play').setAttribute('aria-pressed', String(playing));
  $('play').setAttribute('aria-label', playing ? 'Mettre l’animation en pause' : 'Lire l’animation');
  $('play-label').textContent = playing ? 'Pause' : 'Lecture';
  $('pause-icon').toggleAttribute('hidden', !playing);
  $('play-icon').toggleAttribute('hidden', playing);
}

function updateTimeline() {
  if (!loaded || !selectedClip) return;
  const duration = Math.max(0, model.duration || 0);
  const current = Math.max(0, model.currentTime || 0);
  $('time').max = String(duration || 1);
  if (!seeking) $('time').value = String(current);
  $('time-label').textContent = `${seconds(current)} / ${seconds(duration)} s`;
  $('time').setAttribute('aria-valuetext', `${seconds(current)} secondes sur ${seconds(duration)}`);
}

function applyPlayback() {
  if (!loaded || !selectedClip) return;
  model.timeScale = Number($('speed').value);
  if (playing) model.play({ repetitions: finiteClips.has(selectedClip) ? 1 : Infinity });
  else model.pause();
  updatePlaybackControl();
}

function selectClip(name, shouldAnnounce = true) {
  if (!loaded || !model.availableAnimations.includes(name)) return;
  selectedClip = name;
  model.animationName = name;
  model.currentTime = 0;
  document.querySelectorAll('[data-animation]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.animation === name)));
  $('motion-description').textContent = clipDescriptions[name] || `Animation « ${name} » présente dans le modèle.`;
  applyPlayback();
  updateTimeline();
  if (shouldAnnounce) announce(`${clipLabels[name] || name} · ${name}${playing ? '' : ' · en pause'}.`);
}

function buildAnimations() {
  const names = [...model.availableAnimations].sort((a, b) => {
    const rank = name => clipOrder.includes(name) ? clipOrder.indexOf(name) : clipOrder.length;
    return rank(a) - rank(b);
  });
  $('clip-count').textContent = `${names.length} ${names.length > 1 ? 'animations' : 'animation'}`;
  $('animations').replaceChildren(...names.map(name => {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.animation = name;
    button.setAttribute('aria-pressed', 'false');
    const label = document.createElement('span');
    label.textContent = clipLabels[name] || name;
    const sourceName = document.createElement('code');
    sourceName.textContent = name;
    button.append(label, sourceName);
    button.addEventListener('click', () => selectClip(name));
    return button;
  }));
  if (!names.length) {
    const note = document.createElement('p');
    note.className = 'empty-clips';
    note.textContent = 'Ce fichier ne contient aucune animation.';
    $('animations').append(note);
    $('motion-description').textContent = 'La vue 3D reste disponible pour examiner le modèle.';
  }
  return names;
}

function renderStats() {
  const rows = [];
  const dimensions = manifest?.dimensions;
  const restDimensions = manifest?.restDimensions;
  const poseLabel = dimensions?.pose ? ` · ${dimensions.pose}` : '';
  if (dimensions?.heightM) rows.push([`Hauteur${poseLabel}`, `${number(dimensions.heightM)} m`]);
  if (dimensions?.lengthM) rows.push([`Longueur${poseLabel}`, `${number(dimensions.lengthM)} m`]);
  if (dimensions?.widthM) rows.push([`Largeur${poseLabel}`, `${number(dimensions.widthM)} m`]);
  if (restDimensions?.heightM) rows.push(['Hauteur · pose de repos', `${number(restDimensions.heightM, 3)} m`]);
  if (restDimensions?.lengthM) rows.push(['Longueur · pose de repos', `${number(restDimensions.lengthM, 3)} m`]);
  if (restDimensions?.widthM) rows.push(['Largeur · pose de repos', `${number(restDimensions.widthM, 3)} m`]);
  if (manifest?.rig?.bones) rows.push(['Squelette', `${number(manifest.rig.bones, 0)} os`]);
  if (manifest?.triangles) rows.push(['Triangles', number(manifest.triangles, 0)]);
  if (loaded) {
    rows.push(['Matériaux', number(model.model.materials.length, 0)]);
    rows.push(['Animations du GLB', model.availableAnimations.join(' · ') || 'Aucune']);
  }
  if (!rows.length) rows.push(['Détails', 'Disponibles au chargement du modèle']);
  $('model-stats').replaceChildren(...rows.flatMap(([label, value]) => {
    const term = document.createElement('dt'), detail = document.createElement('dd');
    term.textContent = label;
    detail.textContent = value;
    return [term, detail];
  }));
  $('dimensions').textContent = dimensions?.heightM && dimensions?.lengthM
    ? `${dimensions.pose ? `Pose initiale ${dimensions.pose} · ` : ''}${number(dimensions.lengthM)} m de long · ${number(dimensions.heightM)} m de haut`
    : '';
}

function resetCamera() {
  model.cameraOrbit = '35deg 76deg 100%';
  model.cameraTarget = 'auto auto auto';
  if (reducedMotion.matches) model.jumpCameraToGoal();
}

function fail(message) {
  clearTimeout(loadTimeout);
  loaded = false;
  setControls(false);
  $('loading').classList.add('error');
  $('loading-label').textContent = message;
  $('retry').hidden = false;
  if (typeof model.showPoster === 'function') model.showPoster();
  announce(`${message} Ouvrez cet atelier avec le serveur local des ressources, puis rechargez la page. Les fichiers restent téléchargeables.`);
}

$('play').addEventListener('click', () => { playing = !playing; applyPlayback(); });
$('speed').addEventListener('change', applyPlayback);
$('reset-camera').addEventListener('click', resetCamera);
$('rotate').addEventListener('click', () => {
  model.autoRotate = !model.autoRotate;
  $('rotate').setAttribute('aria-pressed', String(model.autoRotate));
});
$('compare').addEventListener('click', () => {
  const expanded = $('concept-panel').hidden;
  $('concept-panel').hidden = !expanded;
  $('stage').classList.toggle('comparing', expanded);
  $('compare').setAttribute('aria-expanded', String(expanded));
});
$('time').addEventListener('input', () => {
  if (!loaded) return;
  seeking = true;
  playing = false;
  model.pause();
  model.currentTime = Number($('time').value);
  updatePlaybackControl();
  updateTimeline();
});
$('time').addEventListener('change', () => { seeking = false; updateTimeline(); });
$('time').addEventListener('blur', () => { seeking = false; });
$('concept-image').addEventListener('error', () => {
  $('concept-image').hidden = true;
  $('concept-error').hidden = false;
});
reducedMotion.addEventListener('change', () => {
  if (!reducedMotion.matches) return;
  playing = false;
  model.autoRotate = false;
  $('rotate').setAttribute('aria-pressed', 'false');
  applyPlayback();
});
model.addEventListener('progress', event => {
  const progress = event.detail.totalProgress;
  $('progress-fill').style.width = progress >= 1 ? '0' : `${progress * 100}%`;
});
model.addEventListener('load', () => {
  clearTimeout(loadTimeout);
  loaded = true;
  const names = buildAnimations();
  setControls(true);
  selectClip(names.includes('Idle') ? 'Idle' : names[0], false);
  renderStats();
  model.dismissPoster();
  announce(`Brise-Sève chargé · ${names.length} ${names.length > 1 ? 'animations' : 'animation'} disponibles${playing && names.length ? ' · lecture en cours' : ''}.`);
});
model.addEventListener('error', () => fail('Le modèle 3D ne peut pas être affiché.'));
model.addEventListener('finished', () => {
  if (!finiteClips.has(selectedClip)) return;
  playing = false;
  updatePlaybackControl();
  updateTimeline();
  announce(`${clipLabels[selectedClip] || selectedClip} terminé. Lancez la lecture pour revoir l’animation.`);
});
setInterval(() => { if (!document.hidden) updateTimeline(); }, 100);
updatePlaybackControl();

async function initialize() {
  loadTimeout = setTimeout(() => fail('Le moteur 3D ne répond pas.'), 15000);
  await customElements.whenDefined('model-viewer');
  clearTimeout(loadTimeout);
  // Optional metadata never prevents the actual GLB from opening.
  try {
    const response = await fetch(new URL('manifest.json', rootUrl), { cache: 'no-store', signal: AbortSignal.timeout(5000) });
    if (response.ok) manifest = await response.json();
  } catch { /* The model can still be reviewed without manifest metadata. */ }
  const files = manifest?.files || {};
  for (const [type, fallback] of [['glb', 'models/brise-seve.glb'], ['blend', 'assets/brise-seve.blend'], ['fbx', 'assets/brise-seve.fbx']]) {
    $('download-' + type).href = assetUrl(files[type] || fallback);
  }
  if (files.concept) {
    $('concept-image').src = assetUrl(files.concept);
    $('concept-link').href = assetUrl(files.concept);
  }
  renderStats();
  loadTimeout = setTimeout(() => fail('Le chargement du modèle prend trop de temps.'), 45000);
  model.src = assetUrl(files.glb || 'models/brise-seve.glb');
}

initialize().catch(() => fail('L’atelier 3D ne peut pas être chargé.'));
