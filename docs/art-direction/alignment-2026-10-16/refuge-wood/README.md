# Bois du refuge — correction du 16 octobre 2026

Cette correction répond au pignon signalé par l'utilisateur : les nœuds énormes
et le veinage étiré donnaient au bâtiment l'aspect d'un panneau plaqué. Elle est
limitée aux **trois maisons du refuge** dans `StarterIsland`.

## Cause et correction

Le pignon précédent était un seul panneau de 6,26 m de large. Il utilisait le
motif de bois scié des modules sur des tuiles de 3 × 1,5 m, sans séparation en
planches. Les UV normalisés de chaque face de poutre étaient ensuite étirés
avec les modules agrandis. Les poutres ajoutées directement à l'île utilisaient
également une texture d'écorce sur un cube aux UV 0–1.

Le nouveau bois peint est plus calme, mat et moins orangé. Ses UV suivent une
échelle de **1,2 m transversalement × 2,4 m dans le sens du fil**, y compris sur
les poutres longues. Les six pignons sont recomposés en **20 planches chacun**,
avec arêtes biseautées et décalages de texture par planche.

La finition des joints utilise un jour de 15 mm, un biseau de 12 mm et une
languette reculée derrière chacun des 19 joints. Elle évite de lire le ciel
entre les planches. Le mesh final contient **636 triangles par pignon**,
sans objet ni matériau supplémentaire pour les joints.

Les maisons partagent deux matériaux, un pour les planches et un pour les
poutres, ainsi que sept meshes aux UV corrigés. **51 renderers** existants
reçoivent cette correction, en plus des six pignons. Le nombre de triangles et
la géométrie des modules existants ne changent pas : seuls les pignons gagnent
de la géométrie. Les GLB et prefabs sources du kit de construction restent
conservés ; les overrides appartiennent à la scène du refuge.

La texture `PaintedTimberV2.png`, en **1254 × 1254 pixels**, est une génération
imagegen copiée à sa résolution native. Son [prompt et sa provenance](texture-provenance.md)
sont conservés avec cette revue.

## Comparaison

Les vues utilisent la même caméra, un FOV de 35°, une image de 1600 × 1000,
l'heure 0,4 et un ciel clair.

| Vue | Avant | Après |
| --- | --- | --- |
| Pignon | [Avant](before/gable.png) | [Après](after/gable.png) |
| Façade complète | [Avant](before/facade.png) | [Après](after/facade.png) |

![Pignon avant correction](before/gable.png)

![Pignon après correction](after/gable.png)

## Reproduction

Dans Unity 6000.6.6f1, ouvrir `StarterIsland` en Edit Mode. Exécuter la correction
**après** `RefineRefugeRoofs` ou `UpgradeStarterIslandEnvironment` si ces outils
ont été utilisés : ils recréent la version précédente des pignons et matières.

```powershell
$arkyaCli = 'C:/Program Files/Unity Hub/resources/unity.exe'
$arkyaProject = 'D:/Repositories/arkya/Unity3D/Arkya'

& $arkyaCli command run_script --file AgentScripts/World/RefineRefugeWood.cs --entry RefineRefugeWood.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/World/CaptureRefugeWood.cs --entry CaptureRefugeWood.Capture --args '["after"]' --project-path $arkyaProject --caller plugin --skill unity-cli --json
```

`RefineRefugeWood` met à jour les assets dédiés en conservant leurs GUID, puis
marque la scène modifiée sans la sauvegarder. `CaptureRefugeWood` accepte
`before` ou `after` et restaure caméra, FOV, aspect, météo, heure, profil de rendu
et RenderTexture dans son bloc `finally`. Il ne sauvegarde ni scène ni réglages.

## Vérification

Le [rapport de correction](validation.json) conserve les quantités de meshes,
renderers et matériaux ainsi que les contrôles de géométrie.

- Outils compilés et exécutés dans Unity 6000.6.6f1 sans diagnostic d'erreur.
- Trois maisons, 57 renderers de bois et six pignons de 636 triangles contrôlés.
- 38 rayons vérifient la fermeture des 19 joints depuis les deux faces.
- Captures finales inspectées à caméra et heure identiques aux vues avant.
- `StarterIsland.unity` sauvegardée ; scène sans modification en attente.
- Tous les nouveaux assets disposent de leur `.meta`.
- [63 fichiers source et de référence comparés par SHA-256](integrity.json) :
  aucun changement, dont les GLB du kit, la référence arbre et l'ancien matériau partagé.
- [Console Unity](console.json) : aucune erreur, compilation terminée sans échec.
  Les 20 avertissements présents avant la dernière validation restent présents.

## Fichiers de production

Sous `Unity3D/Arkya/Assets/Arkya/World/StarterIsland/` :

- `Textures/PaintedTimberV2.png` et son `.meta`.
- `Materials/Environment/Refuge painted boards V2.mat` et
  `Refuge painted beams V2.mat`, avec leurs `.meta`.
- Les sept meshes `Meshes/RefugeWood/` et leurs `.meta`.
- `Meshes/RefugeGableInfill.asset`, mis à jour en conservant son GUID.
- `Scenes/StarterIsland.unity`, pour les références de meshes et matériaux.

Les outils sont conservés dans `Unity3D/Arkya/AgentScripts/World/RefineRefugeWood.cs`
et `CaptureRefugeWood.cs`.

La correction reste une proposition artistique à examiner. Les captures ne
constituent pas une mesure des performances d'un build.
