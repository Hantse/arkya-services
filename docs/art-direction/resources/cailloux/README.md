# Cailloux — famille de ressources Arkya

**Statut : validé par l'utilisateur le 15 octobre 2026**, pour les quatre
formes/tailles et les trois matières (12 variantes).

Collection du 15 octobre 2026, dans la direction peinte des
[arbres V3](../../arbre-de-base/declinaisons/matieres-v3/README.md).
Quatre silhouettes sont déclinées avec trois matières sur une géométrie identique.
Les volumes associent des plans larges, des arêtes usées et des épaules asymétriques.
Les **12 GLB, 12 sources Blender et 12 aperçus** sont disponibles dans le
[manifest](manifest.json). La [comparaison des tailles](comparaison-tailles.png)
présente les quatre formes à la même échelle, en pierre gris bleu.

## Formes et matières

| Forme | Hauteur | Largeur × profondeur | Triangles |
|---|---:|---:|---:|
| Galet compact | 0,45 m | 0,74 × 0,56 m | 8 200 |
| Bloc oblique | 0,90 m | 0,91 × 0,65 m | 7 820 |
| Rocher large | 1,60 m | 2,84 × 1,74 m | 8 500 |
| Affleurement | 2,70 m | 2,97 × 1,94 m | 8 500 |

Les trois matières sont **pierre gris bleu**, **grès chaud** et **pierre moussue**.
Le [manifest](manifest.json) répertorie les chemins relatifs des GLB, sources
Blender, aperçus et mesures. Chaque asset a un seul mesh, un pivot centré en X/Y
et son point le plus bas à Z=0. Les GLB utilisent l'axe Y vertical.

## Texture et construction

La [source peinte commune](../sources/stone-painted.png), générée à 1254 × 1254 px
natifs, fournit les strates, les fissures choisies et les transitions de couleur.
Elle est projetée sur les volumes puis calculée sur leurs UV en trois cartes
de **2048 × 2048 px** : couleur, normale et rugosité. Une occlusion locale douce
est intégrée à la couleur. La matière moussue ajoute de grandes zones de mousse
sauge sur les surfaces favorables, sans grain fin supplémentaire.
Le contraste courant de la source est réduit de 25 % autour du ton médian,
avec davantage de profondeur conservée dans les fissures les plus sombres.

Les textures sont incorporées dans chaque GLB et chaque fichier Blender.
Les fichiers `textures/` restent disponibles pour inspection. Les UV, la forme
et le nombre de triangles d'une silhouette restent identiques entre ses matières.

## Régénération

[Générateur autonome](scripts/generate_cailloux.py), pour Blender 4.5.14 :

```text
blender --background --python scripts/generate_cailloux.py -- --geometry-only
blender --background --python scripts/generate_cailloux.py -- --shape 03-rocher-large --material 01-gris-bleu --render --gpu
blender --background --python scripts/generate_cailloux.py -- --render --gpu
blender --background --python scripts/generate_cailloux.py -- --comparison-only --gpu
blender --background --python scripts/validate_cailloux.py
```

Sans `--gpu`, les calculs utilisent le CPU. Sans `--render`, les fichiers sont
exportés sans aperçu. Les géométries intermédiaires sont conservées dans `geometry/`.

## Vérification

Les douze fichiers Blender ont été rouverts séparément et les douze GLB analysés
par le [validateur](scripts/validate_cailloux.py), avec un
[rapport détaillé](validation.json) : une seule composante connexe par pierre,
maillage fermé sans arête non-manifold, moins de 10 000 triangles, pivot au sol,
textures incorporées et trois cartes PBR présentes. Les empreintes de géométrie
et d'UV sont exactement identiques entre les trois matières d'une même forme.
Les douze aperçus et la comparaison ont été inspectés visuellement.

Les budgets de collision et les éventuels LOD restent à définir pour leur
intégration ; aucun asset Unity n'est modifié par cette étude.
