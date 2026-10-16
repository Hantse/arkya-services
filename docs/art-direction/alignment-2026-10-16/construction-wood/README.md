# Bois de construction — extension du 16 octobre 2026

Le bois peint V2 est étendu à toutes les constructions en bois actuellement
présentes dans le projet actif : kit modulaire, maisons du refuge, clôture,
panneaux et ponton. Cette passe prolonge la [correction du pignon](../refuge-wood/README.md).

## Couverture

Le [rapport de migration](validation.json) relève **10 GLB importés, 10 prefabs
mis à jour et 102 renderers de construction harmonisés dans l'île**. Aucun de
ces renderers ne conserve les anciens matériaux de bois. La première exécution
a sauvegardé `StarterIsland.unity`.

| Ensemble | Couverture |
| --- | --- |
| Kit modulaire | Cinq modules bois et charpente des cinq modules chaume |
| Trois maisons | 30 modules, 21 poutres et poteaux, six pignons de 20 planches |
| Clôture du refuge | Sept poteaux et six traverses |
| Signalétique | Quatre poteaux et quatre panneaux |
| Ponton | Quatorze éléments de platelage et dix pieux |

Les cinq modules pierre conservent leurs fichiers sources. La scène
`StructureKitReview` hérite des nouveaux prefabs. `ArtDirectionComparison`
contient les familles naturelles approuvées et aucune construction à corriger.
Les troncs, souches et arbres gardent leur écorce ; le bois brûlé du foyer garde
sa matière de charbon.

## Matières et UV communs

La texture et les deux matériaux sont désormais partagés sous
`Unity3D/Arkya/Assets/Arkya/Building/Art/` :

- `Textures/PaintedTimberV2.png` : PNG natif **1254 × 1254**, sans nouvelle génération.
- `Materials/Painted boards V2.mat` : teinte `(1, 0.97, 0.91)`.
- `Materials/Painted beams V2.mat` : teinte `(0.78, 0.76, 0.71)`.

Ces trois assets ont été déplacés depuis le refuge avec leurs GUID conservés.
Les matériaux sont mats, avec une rugosité de 0,96, soit une smoothness Unity de
0,04. La [provenance imagegen originale](../refuge-wood/texture-provenance.md)
reste disponible. Le PNG natif porte l'empreinte SHA-256
`30b4da22c7fed4c0f3475f3ba9f9cb7fa97586a2026b2c3f57a6f45300ab90a8`.

Les UV suivent **1,2 m transversalement × 2,4 m dans le sens du fil**. Cette
densité remplace le placage géant et les UV 0–1 étirés avec chaque poutre. Le
kit à échelle unitaire utilise directement les meshes GLB corrigés ; les
instances agrandies de l'île utilisent des meshes dérivés adaptés à leur
échelle. Les positions et le nombre de triangles des modules restent
inchangés. Les pignons modelés lors de la première correction sont conservés.

## Comparaisons Unity

Treize paires de captures couvrent les trois usages hors maisons et les dix
modules du kit. Chaque paire conserve son cadrage et ses réglages de lumière.

| Vue | Avant | Après |
| --- | --- | --- |
| Clôture | [Avant](before/garden-rail.png) | [Après](after/garden-rail.png) |
| Panneau | [Avant](before/direction-board.png) | [Après](after/direction-board.png) |
| Ponton | [Avant](before/jetty-planks.png) | [Après](after/jetty-planks.png) |
| Fondation bois | [Avant](before/kit-wood-foundation.png) | [Après](after/kit-wood-foundation.png) |
| Mur plein bois | [Avant](before/kit-wood-solidwall.png) | [Après](after/kit-wood-solidwall.png) |
| Fenêtre bois | [Avant](before/kit-wood-windowwall.png) | [Après](after/kit-wood-windowwall.png) |
| Porte bois | [Avant](before/kit-wood-doorwall.png) | [Après](after/kit-wood-doorwall.png) |
| Plafond bois | [Avant](before/kit-wood-ceiling.png) | [Après](after/kit-wood-ceiling.png) |
| Fondation chaume | [Avant](before/kit-thatch-foundation.png) | [Après](after/kit-thatch-foundation.png) |
| Mur plein chaume | [Avant](before/kit-thatch-solidwall.png) | [Après](after/kit-thatch-solidwall.png) |
| Fenêtre chaume | [Avant](before/kit-thatch-windowwall.png) | [Après](after/kit-thatch-windowwall.png) |
| Porte chaume | [Avant](before/kit-thatch-doorwall.png) | [Après](after/kit-thatch-doorwall.png) |
| Plafond chaume | [Avant](before/kit-thatch-ceiling.png) | [Après](after/kit-thatch-ceiling.png) |

![Ponton après harmonisation](after/jetty-planks.png)

![Mur plein bois après harmonisation](after/kit-wood-solidwall.png)

## Reproduction et génération future

`Assets/Arkya/Building/Editor/ConstructionWoodArt.cs` centralise les matériaux
et l'application des UV métriques. Les outils `ImportStructures`,
`UpgradeStarterIslandEnvironment`, `RefineRefugeRoofs` et `RefineRefugeWood`
l'utilisent afin de conserver ce traitement lors des imports et reprises.

Après avoir redimensionné une construction dans l'éditeur, le menu
**Arkya > Art > Align construction wood in active scene** recalcule sa densité
de texture. Cette opération marque la scène modifiée ; sauvegarder ensuite.

Le [générateur Blender du kit](../../resources/structures/scripts/generate_structures.py)
produit désormais le bois V2, ses UV métriques et ses matériaux embarqués dans
les GLB et `.blend`. Les prefabs Unity utilisent les deux matériaux URP
communs ; les exports artistiques gardent leurs textures embarquées pour le
viewer et la réimportation.

Depuis `StarterIsland` en Edit Mode, les outils suivants appliquent la
migration et produisent les captures :

```powershell
$arkyaCli = 'C:/Program Files/Unity Hub/resources/unity.exe'
$arkyaProject = 'D:/Repositories/arkya/Unity3D/Arkya'

& $arkyaCli command run_script --file AgentScripts/World/HarmonizeConstructionWood.cs --entry HarmonizeConstructionWood.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/World/CaptureConstructionWood.cs --entry CaptureConstructionWood.Capture --args '["after"]' --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/World/CaptureConstructionWood.cs --entry CaptureConstructionWood.CaptureKit --args '["after"]' --project-path $arkyaProject --caller plugin --skill unity-cli --json
```

La migration vérifie les empreintes avant remplacement des GLB, conserve les
GUID et sauvegarde la scène. Les captures restaurent leur état temporaire de
caméra, lumière et rendu. Le paramètre `before` permet de constituer une
référence avant une migration ultérieure.

## Vérification

Les [contrôles des sources](../../resources/structures/validation.json) ont
réussi pour **15 GLB et 15 sources Blender** : dimensions, pivots, ouvertures,
accroches, UV, images embarquées, teintes et rugosité. Les UV répétés sont
autorisés uniquement pour le bois ; le contrôle 0–1 reste appliqué aux autres
matières. Le [rapport de conservation](../../resources/structures/wood-v2-validation.json)
confirme une géométrie et des budgets identiques pour les quinze modules, et
**20 fichiers inchangés par SHA-256** pour les cinq modules pierre.

- [Unity 6000.6.6f1](console.json) : compilation terminée sans échec, aucune
  erreur console. Le compteur final conserve un avertissement.
- [15 modules validés dans Unity](structure-validation.json) : références,
  dimensions, colliders, passages des portes/fenêtres, points d'assemblage,
  matériaux et textures ; les modules pierre refusent les matériaux bois.
  Les 80 fichiers des références arbre/mana restent identiques.
- [Contrôle des meshes de l'île](mesh-validation.json) : positions et normales
  conservées sur 155 880 triangles, matériaux et UV nonbois conservés sur
  16 824 triangles, densité métrique vérifiée sur 12 672 faces planes.
- [Seconde migration](idempotence.json) : zéro modèle réimporté, zéro prefab
  modifié, zéro renderer réaffecté ; scène sauvegardée.
- [Intégrité Unity](integrity.json) : 40 fichiers protégés inchangés, dont les
  `.meta` des modèles/prefabs et les cinq modules pierre ; les 17 assets communs
  et outils compilés disposent tous de leur `.meta`.

La couverture de 102 renderers et l'absence d'anciens matériaux sont déjà
consignées dans le rapport de migration. Les performances d'un build ne sont
pas mesurées par cette revue.

## Fichiers concernés

- `Assets/Arkya/Building/Art/` : texture et deux matériaux déplacés avec GUID
  conservés, treize meshes adaptés aux dimensions des instances de l'île.
- `Assets/Arkya/Building/Editor/ConstructionWoodArt.cs` : politique commune de
  matériau et d'échelle UV, disponible aux outils d'édition.
- Dix GLB et dix prefabs de `Assets/Arkya/Building/Structures/`, ainsi que
  `Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity`.
- `AgentScripts/World/HarmonizeConstructionWood.cs`, `CaptureConstructionWood.cs`,
  `UpgradeStarterIslandEnvironment.cs`, `RefineRefugeRoofs.cs`, `RefineRefugeWood.cs` ;
  `AgentScripts/Structures/ImportStructures.cs` et `ValidateStructures.cs`.
- Sources Blender, GLB, générateur, validateur, manifeste et aperçus du kit
  sous `docs/art-direction/resources/structures/`.
