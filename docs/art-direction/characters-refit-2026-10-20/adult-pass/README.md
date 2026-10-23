# Visages adultes, cheveux, matières et deux lames

**Vérifications techniques et revue visuelle ciblée terminées.** Le dernier
lot triangulé, avec prises/lames à 0,94 radian, est importé dans Unity. Les
preuves natives du 20 octobre 2026 à 22 h 16 UTC confirment les corrections
visuelles et le dégagement des armes. La revue de création en Play a réussi ;
les captures desktop finales datent de 22 h 17–18 UTC et l’éditeur a été
restauré. La validation artistique par l’utilisateur reste distincte de ces
contrôles.

Cette reprise répond au retour « encore trop enfantin, surtout le visage ».
Les [six références](../../resources/references/personnages-stylises-2026-10-20/README.md)
restent la cible : visage de jeune adulte, cheveux sculptés magenta, or chaud,
jade verni, étoffe ivoire et deux lames turquoise en croissant.

## Changements

- Visages : orbites et paupières reprises, iris et pupilles reconstruits,
  pommettes hautes, joue basse affinée, nez, mâchoire et menton redessinés.
- Cheveux : plusieurs mèches sculptées, variations de pigment et liaisons
  resserrées entre les segments et les bagues de la queue haute.
- Matières : reflets URP distincts pour l’or et l’émail jade, reflet rosé
  contenu dans les cheveux, peau et tissu moins brillants. L’éclairage
  latéral du studio rend les plans du visage plus lisibles.
- Deux lames turquoise avec âme d’or continue ajourée, biseaux et poignées
  en cuir. Les doigts entourent les poignées et les armes suivent les mains.
- Pose Idle : bras dégagés du costume pour laisser de la place aux armes.
  Les prises sont ajustées aux poses natives échantillonnées.
- Corrections locales : peau sous la jupe B, fermeture du crâne arrière A,
  raccords du col et remappage du matériau `Blade`.

Les quatre meshes et les 26 os restent le contrat d’intégration. Les sources,
scripts et budgets détaillés figurent dans le [dossier modèles](../../resources/personnages/game/README.md).
Les copies Unity sont sous `Unity3D/Arkya/Assets/Arkya/Characters/Models/` ;
les matières, `PaintedCharacter.shader`, `CharacterCreationBuilder.cs` et le
studio de création portent la présentation native. Les outils de revue
`ReviewReferenceRefit.cs` et `ReviewCreationInPlay.cs` restent dans
`AgentScripts/Characters/`, hors d’`Assets`.

## Vérifications réalisées

| Contrôle | Résultat et portée |
|---|---|
| [Copie des FBX](source-copy-report.json) | Quatre exports triangulés : SHA-256 source/cible concordants et quatre `.meta` conservés lors de la copie finale de 22 h 15 UTC. |
| [Contrat des sources](../../resources/personnages/game/reforge-contract-validation.json) | Quatre bases : matrices de repos et parents conservés, peinture et matières personnalisables vérifiées. |
| [Réimport FBX](../../resources/personnages/game/fbx-roundtrip-validation.json) | Quatre bases : 26 os, quatre meshes, quatre influences au plus et poids normalisés. |
| [Lames et prises](weapon-study/final/weapons-and-grips-validation.json) | Pièces fermées, coordonnées finies, poids vers la main correspondante et topologie des mains conservée. |
| [48 contrôles directs](verification/reference-direct-checks.json) | Dernier lot : 48 réussites, zéro échec à 22 h 16 UTC. Invocation directe des méthodes synchrones des fixtures ; ce rapport n’est pas une exécution du Unity Test Runner et n’inclut pas ses coroutines ni son cycle de vie. |
| [Revue de création en Play](desktop/creation-live-review.json) | 152 contrôles pendant la revue, puis quatre contrôles de restauration, tous réussis. Deux formats desktop : 1600 × 900 et 1280 × 720. Contrôles d’apparence et cadrage sur 24 combinaisons, rotation, portrait, défilement et retour UI. |
| [31 captures natives](after/reference-refit-capture.json) | Quatre bases, portraits et profils, prises, turnaround couleur/argile et dix poses Human B. Lot final de 22 h 16 UTC : aucun matériau manquant ni erreur de shader. |
| [Console du dernier import](verification/console-final-import.json) | Aucun nouvel avertissement depuis l’import triangulé ; aucune compilation en échec. |
| [Intersections armes/tissu](weapon-study/final/native-clearance-validation.json) | Export natif final de 22 h 16 UTC : zéro intersection dans les deux sens sur 12 poses échantillonnées avec prises à 0,94 radian. La peau est exclue de cette mesure. |
| [Revue visuelle indépendante](fidelity-review.md) | Les quatre réserves visuelles et la dernière réserve technique sont levées sur le lot final. Aucun nouveau défaut bloquant dans la revue ciblée. |
| [Restauration de l’éditeur](verification/editor-restoration.json) | Session de revue fermée, 18 racines restaurées et StarterIsland réactivée. L’[état final](verification/editor-state-final.json) confirme Play arrêté, aucune compilation ou mise à jour en cours et la scène non modifiée. |
| [Console finale](verification/console-final-status.json) | Aucune erreur ni avertissement dans l’état final ; aucune compilation en échec. |

Le contrôle Play appelle les vrais événements UI. Le callback d’exploration
est intercepté pour cette revue : aucune île n’y est chargée ; le rapport
atteste le retour UI, sans valider déplacement ni réseau.

Les contrôles d’intersection couvrent deux instants de chacun des six états
Idle, Walk, Run, Jump, SwimIdle et Swim. Ils ne certifient pas le cycle entier,
les transitions ni toutes les collisions du corps. Les rendus natifs ne
remplacent pas la validation artistique de l’utilisateur.

## Preuves visuelles

Le [portrait Human B](after/human-b-portrait.png), la
[vue entière](after/human-b-body.png) et le
[turnaround](after/human-b-turnaround-color.png) proviennent des prefabs
importés dans Unity. Le rapport de capture identifie leur lot et leurs limites.

L’interface finale est visible en [silhouette](desktop/creation-body-1600x900-221759189.png)
et en [portrait](desktop/creation-portrait-1600x900-221812083.png), dans deux
captures brutes de la Game View en 1600 × 900. Le rapport Play final est
clos avec 156 entrées réussies et zéro échec, dont les quatre contrôles de
restauration réalisés après la revue.

`material-study/` et `material-refinement/` montrent l’étude du shader sur
l’ancienne géométrie ; `before/` conserve le résultat précédent.
`iterations/native-01/` archive le premier lot natif avec ses défauts corrigés.
Ces images intermédiaires ne remplacent pas les preuves courantes d’`after/`.
Cette reprise utilise uniquement des captures desktop.

## Budgets et limites

Avec la queue segmentée, le lot final triangulé compte 203 506 triangles
sur A, 208 975 sur Human B et 208 985 sur Elf B. Le corps utilise douze
matières. Les vues natives A utilisent la coiffure courte, soit 167 330
triangles visibles ; les [budgets des sources](../../resources/personnages/game/README.md)
détaillent chaque coiffure.

Cette passe ne livre pas les LOD ni l’optimisation nécessaires aux foules
d’un MMORPG. Les mains fermées et les deux lames constituent une présentation
fixe : aucun système d’équipement, état de combat ou contrat réseau n’est
ajouté. Le rig ne comporte pas de commande faciale, de doigts animés
individuellement ni de simulation des tissus.
