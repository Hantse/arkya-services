import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { validateAppearance, parseAppearance, changeRace, randomAppearance } from './appearance.js';

const manifest = JSON.parse(await readFile(new URL('../manifest.json', import.meta.url), 'utf8'));
const valid = manifest.defaults;

test('versioned local presets restore only known complete values', () => {
  assert.deepEqual(parseAppearance(JSON.stringify(valid), manifest), valid);
  for (const value of [null, [], {}, { ...valid, version: 2 }, { ...valid, race: '../other' }, { ...valid, hairStyle: 'unknown' }, { ...valid, eyeColor: 'unknown' }, { ...valid, skin: '#ffffff' }, { ...valid, heightCm: '175' }, { ...valid, heightCm: 175.5 }, { ...valid, heightCm: 159 }, { ...valid, heightCm: 196 }, { ...valid, race: 'elf', heightCm: 160 }, { ...valid, extra: true }]) {
    assert.equal(validateAppearance(value, manifest), null);
  }
  assert.equal(parseAppearance('not-json', manifest), null);
  const missing = { ...valid }; delete missing.outfitColor;
  assert.equal(validateAppearance(missing, manifest), null);
});

test('race change preserves relative size and all cosmetic choices', () => {
  const tallerHuman = { ...valid, heightCm: 190, skin: 'ebony', hairColor: 'white' };
  const elf = changeRace(tallerHuman, 'elf', manifest);
  assert.equal(elf.heightCm, 200);
  assert.equal(elf.skin, 'ebony');
  assert.equal(elf.hairColor, 'white');
  assert.deepEqual(changeRace(elf, 'human', manifest), tallerHuman);
  assert.ok(validateAppearance(elf, manifest));
});

test('every race, silhouette, and hair style has a local GLB path', () => {
  for (const race of manifest.races) for (const silhouette of manifest.silhouettes) for (const style of manifest.hairStyles) {
    assert.match(race.models[silhouette.id][style.id], /^models\/(human|elf)-[ab]-(short|long|braid)\.glb$/);
  }
});

test('random choices always respect the race contract, including range endpoints', () => {
  for (const sample of [0, .2, .49, .7, .999999]) assert.ok(validateAppearance(randomAppearance(manifest, () => sample), manifest));
  for (let i = 0; i < 100; i++) assert.ok(validateAppearance(randomAppearance(manifest), manifest));
});
