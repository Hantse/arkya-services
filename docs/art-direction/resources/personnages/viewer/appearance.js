export const STORAGE_KEY = 'arkya.character-appearance.v1';
const keys = ['version', 'race', 'silhouette', 'heightCm', 'skin', 'hairStyle', 'hairColor', 'eyeColor', 'outfitColor'];

export function validateAppearance(value, manifest) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  if (Object.keys(value).length !== keys.length || keys.some(key => !Object.hasOwn(value, key))) return null;
  if (value.version !== 1) return null;
  const race = manifest.races.find(item => item.id === value.race);
  if (!race || !Number.isInteger(value.heightCm) || value.heightCm < race.height.min || value.heightCm > race.height.max) return null;
  const choices = {
    silhouette: manifest.silhouettes, hairStyle: manifest.hairStyles,
    skin: manifest.palettes.skin, hairColor: manifest.palettes.hair,
    eyeColor: manifest.palettes.eyes, outfitColor: manifest.palettes.outfit
  };
  if (Object.entries(choices).some(([key, list]) => !list.some(item => item.id === value[key]))) return null;
  return Object.fromEntries(keys.map(key => [key, value[key]]));
}

export function parseAppearance(raw, manifest) {
  try { return validateAppearance(JSON.parse(raw), manifest); } catch { return null; }
}

export function changeRace(appearance, raceId, manifest) {
  const currentRace = manifest.races.find(item => item.id === appearance.race);
  const nextRace = manifest.races.find(item => item.id === raceId);
  if (!currentRace || !nextRace) throw new Error('Unknown race');
  const offset = appearance.heightCm - currentRace.height.default;
  return { ...appearance, race: raceId, heightCm: Math.max(nextRace.height.min, Math.min(nextRace.height.max, nextRace.height.default + offset)) };
}

export function randomAppearance(manifest, random = Math.random) {
  const choose = list => list[Math.floor(random() * list.length)];
  const race = choose(manifest.races);
  return {
    version: 1, race: race.id, silhouette: choose(manifest.silhouettes).id,
    heightCm: race.height.min + Math.floor(random() * (race.height.max - race.height.min + 1)),
    skin: choose(manifest.palettes.skin).id, hairStyle: choose(manifest.hairStyles).id,
    hairColor: choose(manifest.palettes.hair).id, eyeColor: choose(manifest.palettes.eyes).id,
    outfitColor: choose(manifest.palettes.outfit).id
  };
}
