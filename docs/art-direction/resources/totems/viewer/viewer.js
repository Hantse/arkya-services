const $ = id => document.getElementById(id);
const model = $('totem');
const rootUrl = new URL('../', location.href);
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
const animationNames = { Idle: 'Repos', Walk: 'Marche', Run: 'Course' };
const coatDescriptions = {
  lunar: 'Des ombres ardoise, un pelage de lune et des courants de lumière froide.',
  ember: 'Un pelage chaud traversé de braises. La magie souligne les grandes mèches.',
  jade: 'Des nuances végétales et des lignes lumineuses au creux du pelage.',
  amethyst: 'Des violets profonds, des pointes lavande et une magie cristalline.',
  frost: 'Des blancs froids et des ombres bleutées, parcourus de reflets de givre.',
  solar: 'Des ombres dorées et des teintes de miel, réchauffées d’une lueur solaire.',
  rose: 'Des tons de rose et de poussière, relevés de fins courants lumineux.',
  abyss: 'Une fourrure sombre où se dessinent des flux de lumière profonde.',
  moss: 'Les couleurs du sous-bois, des ombres humides aux pointes de mousse.',
  copper: 'Du cuivre aux terres chaudes, avec des éclats magiques dans le pelage.',
  pearl: 'Des teintes de nacre et des ombres douces, traversées de lumière.',
  storm: 'Des tons d’orage et une magie vive qui suit le mouvement du corps.'
};
const speciesIcons = {
  wolf: '<path d="m8 17-2-13 10 7h8L34 4l-2 13-4 12-8 8-8-8Z M8 17l7 5-3 7m20-12-7 5 3 7M15 22l5 9 5-9m-8 9h6M20 31v6 M13 19l4 1m6 0 4-1"/>',
  fox: '<path d="M8 20 5 2l12 12h6L35 2l-3 18-12 17Z M8 20l8 4 4 13 4-13 8-4M5 2l8 18m22-18-8 18M11 21l5 3m8 0 5-3m-11 8h4"/>'
};
let manifest, selectedSpecies, selectedCoat, selectedAnimation = 'Idle';
let playing = !reducedMotion.matches, magicEnabled = true, loading = true, source = '';
let emissionDefaults = new Map();

function announce(message) { $('status').textContent = message; }
function species() { return manifest.species.find(item => item.id === selectedSpecies); }
function colorway() { return manifest.colorways.find(item => item.id === selectedCoat); }
function assetUrl(path) {
  const url = new URL(path, rootUrl);
  if (manifest?.revision) url.searchParams.set('v', manifest.revision);
  return url.href;
}
function number(value, digits = 2) { return new Intl.NumberFormat('fr-BE', { maximumFractionDigits: digits }).format(value); }

function choice(label, id, group, className, onSelect) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = className;
  button.textContent = label;
  button.dataset[group] = id;
  button.setAttribute('aria-pressed', 'false');
  button.addEventListener('click', () => onSelect(id));
  return button;
}

function buildControls() {
  $('species').replaceChildren(...manifest.species.map(item => {
    const button = choice(item.name, item.id, 'species', 'species-choice', id => {
      if (selectedSpecies === id) return;
      selectedSpecies = id;
      selectedCoat = species().defaultColorway;
      render();
    });
    const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    icon.setAttribute('viewBox', '0 0 40 40');
    icon.setAttribute('aria-hidden', 'true');
    icon.innerHTML = speciesIcons[item.id] || '';
    const name = document.createElement('span');
    name.textContent = item.name;
    button.replaceChildren(icon, name);
    return button;
  }));
  $('coats').replaceChildren(...manifest.colorways.map(item => {
    const button = choice('', item.id, 'coat', 'coat-choice', id => {
      if (selectedCoat === id) return;
      selectedCoat = id;
      render();
    });
    button.setAttribute('aria-label', `Pelage ${item.name}`);
    button.title = item.name;
    button.style.setProperty('--coat-main', item.colors[3]);
    button.style.setProperty('--coat-dark', item.colors[1]);
    button.style.setProperty('--coat-light', item.colors[5]);
    button.style.setProperty('--coat-magic', item.magicColor);
    const stone = document.createElement('span');
    stone.className = 'coat-stone';
    stone.setAttribute('aria-hidden', 'true');
    const name = document.createElement('span');
    name.textContent = item.name;
    button.append(stone, name);
    return button;
  }));
  $('animations').replaceChildren(...manifest.animations.map(item => {
    const button = choice(animationNames[item.id] || item.id, item.id, 'animation', '', id => {
      selectedAnimation = id;
      updateAnimation();
      announce(`${species().name} · ${colorway().name} · ${animationNames[id] || id}${playing ? '' : ' en pause'}.`);
    });
    button.disabled = true;
    return button;
  }));
  $('coat-count').textContent = `${manifest.colorways.length} variations`;
  updatePlaybackControl();
}

function render() {
  const animal = species(), coat = colorway();
  document.querySelectorAll('[data-species]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.species === selectedSpecies)));
  document.querySelectorAll('[data-coat]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.coat === selectedCoat)));
  document.documentElement.style.setProperty('--magic', coat.magicColor);
  $('stage-species').textContent = animal.name;
  $('stage-coat').textContent = coat.name;
  $('coat-title').textContent = coat.name;
  $('coat-index').textContent = `${String(manifest.colorways.indexOf(coat) + 1).padStart(2, '0')} / ${manifest.colorways.length}`;
  $('coat-description').textContent = coat.description || coatDescriptions[coat.id] || 'Un pelage peint, des volumes doux et des flux magiques qui épousent le corps.';
  $('palette-count').textContent = coat.colors.length;
  $('palette').replaceChildren(...coat.colors.map((color, index) => {
    const chip = document.createElement('span');
    chip.style.setProperty('--color', color);
    chip.title = `Teinte ${index + 1} : ${color.toUpperCase()}`;
    chip.setAttribute('role', 'img');
    chip.setAttribute('aria-label', chip.title);
    return chip;
  }));
  model.alt = `${animal.name} mystique, pelage ${coat.name}. Modèle peint et animé en trois dimensions.`;
  renderStats();
  renderDownloads();
  renderProvenance();
  const nextSource = assetUrl(animal.models[selectedCoat]);
  if (source !== nextSource) {
    const speciesChanged = !source || !source.includes(`/${selectedSpecies}-`);
    source = nextSource;
    loading = true;
    setModelControls(false);
    $('loading').classList.remove('error');
    $('loading-label').textContent = `${animal.name} · ${coat.name}`;
    model.showPoster();
    if (speciesChanged) resetCamera();
    model.src = source;
    announce(`Chargement : ${animal.name}, pelage ${coat.name}…`);
  }
}

function renderStats() {
  const animal = species();
  const dimensions = animal.dimensions;
  const rows = [];
  if (dimensions) {
    if (dimensions.lengthM) rows.push(['Longueur', `${number(dimensions.lengthM)} m`]);
    if (dimensions.heightM) rows.push(['Hauteur totale', `${number(dimensions.heightM)} m`]);
    if (dimensions.widthM) rows.push(['Largeur', `${number(dimensions.widthM)} m`]);
    $('stage-measure').textContent = dimensions.lengthM && dimensions.heightM ? `${number(dimensions.lengthM)} m de long · ${number(dimensions.heightM)} m de haut` : '';
  } else $('stage-measure').textContent = '';
  if (animal.rig?.bones) rows.push(['Squelette', `${number(animal.rig.bones, 0)} os`]);
  if (animal.triangles) rows.push(['Triangles', number(animal.triangles, 0)]);
  rows.push(['Animations', manifest.animations.map(item => animationNames[item.id] || item.id).join(' · ')]);
  rows.push(['Pelages', `${manifest.colorways.length} × ${colorway().colors.length} teintes`]);
  $('model-stats').replaceChildren(...rows.flatMap(([label, value]) => {
    const term = document.createElement('dt'), detail = document.createElement('dd');
    term.textContent = label;
    detail.textContent = value;
    return [term, detail];
  }));
}

function renderDownloads() {
  const animal = species();
  const files = [['GLB', animal.models[selectedCoat]], ['Blender', animal.blend], ['FBX', animal.fbx]].filter(([, path]) => path);
  $('downloads').replaceChildren(...files.map(([label, path]) => {
    const link = document.createElement('a');
    link.href = assetUrl(path);
    link.download = path.split('/').pop();
    link.textContent = label;
    link.setAttribute('aria-label', `Télécharger ${animal.name}${label === 'GLB' ? ` ${colorway().name}` : ''} en ${label}`);
    const arrow = document.createElement('span');
    arrow.textContent = '↓';
    arrow.setAttribute('aria-hidden', 'true');
    link.append(arrow);
    return link;
  }));
}

function renderProvenance() {
  const animal = species();
  const provenance = animal.provenance || {
    title: 'Fox', author: 'Bake3D', license: 'CC BY 4.0', notice: 'sources/bake3d/README.md'
  };
  $('provenance').textContent = `${animal.name} : ${provenance.author} · ${provenance.license} · Adaptation Arkya`;
  $('provenance').href = new URL(provenance.notice || provenance.url || 'README.md', rootUrl).href;
  $('source-credit').textContent = animal.sourceCredit || `${provenance.title} — ${provenance.author}, ${provenance.license}. Adaptation Arkya.`;
}

function setModelControls(enabled) {
  for (const id of ['reset-camera', 'rotate', 'play']) $(id).disabled = !enabled;
  $('magic').disabled = !enabled || emissionDefaults.size === 0;
  document.querySelectorAll('[data-animation]').forEach(button => {
    button.disabled = !enabled || !model.availableAnimations.includes(button.dataset.animation);
  });
}

function updatePlaybackControl() {
  $('play').setAttribute('aria-pressed', String(playing));
  $('play').setAttribute('aria-label', playing ? 'Mettre l’animation en pause' : 'Lire l’animation');
  $('play-label').textContent = playing ? 'Pause' : 'Lecture';
  $('play-icon').toggleAttribute('hidden', playing);
  $('pause-icon').toggleAttribute('hidden', !playing);
}

function updateAnimation() {
  if (loading) return;
  document.querySelectorAll('[data-animation]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.animation === selectedAnimation)));
  model.animationName = selectedAnimation;
  model.timeScale = Number($('playback-rate').value);
  if (playing) model.play({ repetitions: Infinity });
  else model.pause();
  updatePlaybackControl();
  const motion = species().locomotion?.clips?.find(clip => clip.name === selectedAnimation);
  $('motion-note').textContent = motion?.expected_speed_mps > 0
    ? `Cycle sur place · déplacement associé : ${number(motion.expected_speed_mps)} m/s`
    : 'Respiration et petits mouvements au repos.';
}

function applyMagic() {
  for (const [material, factor] of emissionDefaults) material.setEmissiveFactor(magicEnabled ? factor : [0, 0, 0]);
  $('magic').setAttribute('aria-checked', String(magicEnabled));
}

function resetCamera() {
  // Both quadrupeds face +X; this view keeps the muzzle and the flank visible.
  model.cameraOrbit = '35deg 78deg 95%';
  model.cameraTarget = 'auto auto auto';
  if (reducedMotion.matches) model.jumpCameraToGoal();
}

$('play').addEventListener('click', () => { playing = !playing; updateAnimation(); });
$('playback-rate').addEventListener('change', updateAnimation);
$('magic').addEventListener('click', () => { magicEnabled = !magicEnabled; applyMagic(); });
$('reset-camera').addEventListener('click', resetCamera);
$('rotate').addEventListener('click', () => { model.autoRotate = !model.autoRotate; $('rotate').setAttribute('aria-pressed', String(model.autoRotate)); });
reducedMotion.addEventListener('change', () => {
  if (!reducedMotion.matches) return;
  playing = false;
  model.autoRotate = false;
  $('rotate').setAttribute('aria-pressed', 'false');
  updateAnimation();
});
model.addEventListener('progress', event => {
  const progress = event.detail.totalProgress;
  $('progress-fill').style.width = progress >= 1 ? '0' : `${progress * 100}%`;
});
model.addEventListener('load', event => {
  if (!manifest || model.src !== source || (event.detail?.url && event.detail.url !== source)) return;
  try {
    emissionDefaults = new Map(model.model.materials
      .filter(material => /^Totem(Magic|Spark)(\.|$)/.test(material.name))
      .map(material => [material, [...material.emissiveFactor]]));
    applyMagic();
    loading = false;
    if (!model.availableAnimations.includes(selectedAnimation)) selectedAnimation = model.availableAnimations[0] || '';
    setModelControls(true);
    updateAnimation();
    model.dismissPoster();
    const missing = manifest.animations.filter(item => !model.availableAnimations.includes(item.id));
    announce(missing.length ? `${species().name} · ${colorway().name} · Animation indisponible : ${missing.map(item => item.id).join(', ')}.` : `${species().name} · ${colorway().name} · ${colorway().colors.length} teintes · ${model.availableAnimations.length} animations.`);
  } catch (error) { showError(error); }
});
model.addEventListener('error', () => showError(new Error('Model load failed')));

function showError(error) {
  console.error(error);
  loading = true;
  setModelControls(false);
  $('loading').classList.add('error');
  $('loading-label').textContent = 'Le modèle ne peut pas être affiché';
  model.showPoster();
  announce('Le modèle est indisponible. Choisissez un autre pelage ou rechargez cet atelier avec le serveur local des ressources.');
}

try {
  const response = await fetch(assetUrl('manifest.json'), { cache: 'no-store' });
  if (!response.ok) throw new Error(`Manifest HTTP ${response.status}`);
  manifest = await response.json();
  if (manifest.schemaVersion !== 1 || !manifest.species?.length || !manifest.colorways?.length || !manifest.animations?.length) throw new Error('Unsupported totem manifest');
  for (const coat of manifest.colorways) {
    if (!Array.isArray(coat.colors) || coat.colors.length < 10 || coat.colors.length > 12) throw new Error(`Invalid palette: ${coat.id}`);
  }
  selectedSpecies = manifest.species[0].id;
  selectedCoat = species().defaultColorway;
  await customElements.whenDefined('model-viewer');
  buildControls();
  render();
} catch (error) {
  console.error(error);
  $('loading').classList.add('error');
  $('loading-label').textContent = 'La collection ne peut pas être chargée';
  announce('Ouvrez cet atelier avec le serveur local des ressources, puis rechargez la page.');
}
