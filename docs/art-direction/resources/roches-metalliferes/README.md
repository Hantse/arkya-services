# Roches métallifères

**Statut : validé par l'utilisateur le 15 octobre 2026**, pour les quatre
formes/tailles et les trois matières (12 variantes).

Une famille de roches stylisées peintes, avec des inclusions métalliques modélisées dans le relief. Les formes et les textures prolongent la direction retenue pour les arbres V3 : volumes lisibles, arêtes adoucies et motifs peints de taille moyenne.

| Forme | Hauteur | Construction |
| --- | ---: | --- |
| Nodule | 0,7 m | Volume compact et poche de minerai |
| Bloc fendu | 1,3 m | Deux masses séparées et inclusions près de la fracture |
| Filon oblique | 2,2 m | Dalle inclinée et poches de minerai suivant la diagonale |
| Affleurement | 3,2 m | Plusieurs gradins avec zones métallifères réparties |

Chaque forme existe en **fer acier**, **cuivre patiné** et **or stylisé**. Les trois matières partagent exactement la même géométrie pour faciliter la comparaison. Les plaques de métal sont irrégulières et partiellement enfouies ; les petites veines suivent la surface. Le métal utilise le modèle PBR métallique avec une rugosité satinée.

## Fichiers

- `assets/<forme>/<forme>-<matiere>.glb` : modèle autonome, textures intégrées, UV et normales.
- `assets/<forme>/<forme>-<matiere>.blend` : modèle avec textures packées, éclairage et caméra de contrôle.
- `assets/<forme>/<forme>-<matiere>-preview.png` : aperçu Cycles 1 100 × 1 100, 32 échantillons.
- `textures/<forme>-<matiere>/` : quatre PNG 2K — basecolor, normal, roughness, metallic. Dans le GLB, roughness et metallic sont réunis dans une texture standard glTF.
- `manifest.json` : dimensions réelles, budget de triangles et chemins relatifs.
- `comparaison-tailles.png` : les quatre formes en fer à la même échelle.
- `validation.json` : résultats du contrôle des douze livrables.

Le pivot est centré en X/Y ; le pied repose à Z = 0 dans Blender. Les GLB sont exportés avec l’axe Y vertical selon glTF. Les scènes de contrôle ne sont pas incluses dans les GLB.

Les douze fichiers ont passé `validate_ore_rocks.py` : géométrie et UV identiques entre les trois matières d’une même forme, textures 2K intégrées, cartes PBR présentes, dimensions et pivots vérifiés. Budgets par forme : **5 026**, **8 948**, **9 584** et **13 326 triangles**, respectivement. Les aperçus et la comparaison des tailles ont été inspectés.

## Régénération

Exécuter Blender 4.5 en arrière-plan avec le script `generate_ore_rocks.py` et les arguments après `--` :

```text
--shape all --prepare
--shape bloc-fendu --material cuivre --gpu
--shape all --material all --gpu
--compare --gpu
```

`--prepare` ne fait que construire les géométries sur CPU. `--gpu` autorise explicitement les bakes et les rendus Cycles OptiX. Le script `validate_ore_rocks.py` se lance également dans Blender en arrière-plan après génération complète.

## Source et limites

La géométrie est créée par le générateur Blender de ce dossier. L’albédo de structure utilise la source peinte partagée `../sources/stone-painted.png`, générée pour cette exploration puis projetée et bakée dans Blender. Les couleurs métalliques et la patine sont des matériaux originaux procéduraux, bakés avec la pierre dans les UV du modèle.

Ces modèles sont des études d’assets 3D consultables et réutilisables. Ils ne comprennent pas encore les LOD, collisions, paramètres d’extraction, préfabs Unity ou validation en jeu.
