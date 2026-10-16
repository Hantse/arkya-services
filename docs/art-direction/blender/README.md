# Arkya — Études 3D dans Blender

15 octobre 2026. Exploration des propositions **Cités d’Éther** et **Terres des Anciens**, avec scènes Blender modifiables et rendus calculés par Cycles.

La **V3** approfondit les proportions, les costumes et les matières des personnages, ainsi que la composition et les détails des décors. Les bases humaines sont **Snow** et **Rain**, de Blender Studio / Blender Foundation, adaptées pour Arkya. Leur attribution et leur licence CC BY 4.0 sont documentées dans [CREDITS.md](CREDITS.md). Les versions V1/V2 restent conservées.

Ces images restent des **études stylisées**. Les proportions et équipements évoluent, mais le modelé, les textures, la variété et les finitions demandent encore du travail pour rejoindre les concepts illustrés. Elles ne représentent ni une qualité finale comparable à WoW ou Overwatch, ni un résultat validé dans Unity.

## V3 — Proportions, équipement et lieux habités

Le [dossier V3](V3.md) présente les scènes et portraits, les changements et les commandes de reproduction. **Les quatre rendus sont terminés et les deux scènes ont été rouvertes et vérifiées**, avec leurs textures embarquées ; voir le [rapport](v3-validation.json).

| Proposition | Scène modifiable | Rendu de scène | Portrait |
|---|---|---|---|
| Cités d’Éther | [Blender V3](02-cites-ether-v3.blend) | [Vue V3](02-cites-ether-v3-render.png) | [Personnage V3](02-cites-ether-character-v3.png) |
| Terres des Anciens | [Blender V3](03-terres-anciens-v3.blend) | [Vue V3](03-terres-anciens-v3-render.png) | [Personnage V3](03-terres-anciens-character-v3.png) |

Éther reçoit une armure articulée plus ample, des protections de jambes et une cape brodée ; Anciens développe un équipement de rôdeuse avec cuir ajusté, laçage, coutures, sacoches et bottes construites. Les deux personnages conservent leurs vrais maillages de visage et de mains, avec une tête légèrement réduite. Les décors renforcent les points d’arrivée, les éléments de vie quotidienne, les différences de matière et la séparation des plans.

La V2 ci-dessous conserve la première amélioration des personnages et des décors pour comparaison.

## V2 — Cités d’Éther

![Cités d’Éther — étude V2](02-cites-ether-v2-render.png)

La cour ivoire s’inscrit désormais dans une ville : porte voûtée, escalier, remparts, façades latérales et palais en profondeur. Les cyprès et buissons possèdent des branches et feuilles individuelles. Les joints de maçonnerie, moulures, différences de rugosité et reflets du cristal précisent les matières. Le personnage utilise la base détaillée **Snow**, adaptée pour cette proposition.

- [Scène V2 modifiable](02-cites-ether-v2.blend)
- [Portrait du personnage](02-cites-ether-character-v2.png)
- [Script de développement du décor](scripts/refine_cites_ether.py)

## V2 — Terres des Anciens

![Terres des Anciens — étude V2](03-terres-anciens-v2-render.png)

Les troncs et racines forment des volumes continus, avec une écorce plus travaillée, des feuillages ramifiés, un sous-bois plus dense et une forêt qui se prolonge derrière le sanctuaire. La lumière et une atmosphère légère séparent les plans. Le personnage utilise la base détaillée **Rain**, adaptée pour cette proposition.

- [Scène V2 modifiable](03-terres-anciens-v2.blend)
- [Portrait du personnage](03-terres-anciens-character-v2.png)
- [Script de développement du décor](scripts/refine_terres_anciens.py)

## Utilisation et portée

Ouvrir les fichiers `.blend` dans Blender et utiliser `Camera_Refined` pour les vues de scène V2. Les portraits permettent d’examiner les personnages séparément du décor. Les poses utilisées sont figées et les déformations ont été converties en maillages statiques pour ces études ; cette étape ne livre pas des personnages animables prêts à intégrer au jeu.

Les images sont des rendus de géométrie, matériaux et lumières dans **Blender Cycles**. Les illustrations précédentes servent de références artistiques ; elles ne sont pas plaquées comme fonds pour remplacer la géométrie. Aucun asset ni code Unity n’est modifié.

Une intégration au jeu demandera notamment de définir les rigs et animations, les LOD, les collisions, la navigation, les lightmaps et les budgets de rendu. Les images ne valident aucune performance de MMORPG.

## Reproduire les rendus V2

Version utilisée : **Blender 4.5.14 LTS**, installé dans `D:/Tools/BlenderPortable/`, hors du dépôt. Le [script V2](scripts/render_refined.py) repart des scènes V1 conservées, applique les scripts de développement des décors, puis ajoute les personnages adaptés enregistrés dans `assets/snow-ether.blend` et `assets/rain-ancients.blend`. Ces fichiers de personnages et les scènes V1 sont nécessaires à la reconstruction ; leurs sources sont indiquées dans les [crédits](CREDITS.md).

Depuis PowerShell à la racine du dépôt :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --python 'docs/art-direction/blender/scripts/render_refined.py' -- --scene ether

& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --python 'docs/art-direction/blender/scripts/render_refined.py' -- --scene ancients

& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --python 'docs/art-direction/blender/scripts/render_refined.py' -- --scene ether --portrait

& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --python 'docs/art-direction/blender/scripts/render_refined.py' -- --scene ancients --portrait
```

Les paramètres finaux sont **2 160 × 1 440** pour les scènes et **1 200 × 1 600** pour les portraits, avec **128 échantillons Cycles et débruitage**. Le script utilise OptiX sur le GPU compatible disponible. Il enregistre les scènes V2, les PNG et des fichiers JSON de métadonnées. Une relance remplace les livrables V2 correspondants.

Ajouter `--draft` produit un aperçu à 24 échantillons dans `D:/Tools/BlenderPortable/arkya-previews/` : 1 080 × 720 pour une scène, ou 720 × 960 pour un portrait. Ajouter `--reuse` reprend une scène V2 déjà enregistrée pour son rendu principal. Les scènes et images V1 restent conservées.

## V1 — Première étude conservée

La V1 explorait l’implantation, les palettes et les grands volumes avec des personnages très simplifiés. Elle reste disponible pour comparer les étapes.

| Proposition | Scène | Vues conservées | Construction |
|---|---|---|---|
| Cités d’Éther | [Blender V1](02-cites-ether.blend) | [Principal](02-cites-ether-render.png) · [Ensemble](02-cites-ether-overview.png) · [Chemin](02-cites-ether-path.png) | [Script](scripts/build_cites_ether.py) |
| Terres des Anciens | [Blender V1](03-terres-anciens.blend) | [Principal](03-terres-anciens-render.png) · [Ensemble](03-terres-anciens-overview.png) · [Chemin](03-terres-anciens-path.png) | [Script](scripts/build_terres_anciens.py) |

Le [script V1](scripts/render_scenes.py) conserve les options `--scene ether|ancients`, `--output`, `--draft`, `--reuse chemin.blend` et `--cpu`. Les rendus finaux V1 utilisaient 1 800 × 1 200 et 128 échantillons.

## Vérification

**V3 :** quatre rendus contrôlés visuellement et deux scènes rouvertes avec les scripts automatiques désactivés ; dimensions, cadrages et textures embarquées vérifiés. Le [rapport](v3-validation.json) et [V3.md](V3.md) consignent les résultats. Les validations V1/V2 ci-dessous concernent uniquement leurs fichiers respectifs.

**V1 :** les six PNG ont été contrôlés visuellement et vérifiés à 1 800 × 1 200 ; les deux scènes ont été rouvertes dans Blender 4.5.14 LTS avec leurs caméras. Les métadonnées de ces rendus sont conservées dans les fichiers `*-render-info.json`.

**V2 :** les quatre PNG ont été rendus avec Cycles à 128 échantillons et contrôlés visuellement : deux scènes à 2 160 × 1 440 et deux portraits à 1 200 × 1 600. Les deux fichiers `.blend` ont ensuite été rouverts indépendamment avec les scripts automatiques désactivés. La caméra `Camera_Refined` est active, les anciens mannequins sont absents et toutes les textures utilisées sont embarquées (25 images pour Éther, 3 images dont des UDIM pour Anciens). Les personnages sont statiques, sans armature. Le contrôle est consigné dans [v2-validation.json](v2-validation.json). Aucun asset ou code Unity n’a été modifié ; aucune validation Unity n’est revendiquée.
