# Touffes d’herbe — Arkya

**Référence DA validée par l'utilisateur le 15 octobre 2026**, pour les quatre
formes/tailles et les trois matières (12 variantes). Cette collection rejoint
les références approuvées pour les prochains objets de végétation.

Quatre compositions originales pour prolonger la direction artistique peinte de
[l’arbre V3](../references/arbre-peint-v3/README.md). Les brins sont des rubans
volumétriques fermés : leur courbure, leur effilement et la nervure centrale sont
dans la géométrie. Aucune carte transparente, aucun plan croisé, aucun socle.

| Forme | Hauteur | Composition |
|---|---:|---|
| Touffe rase | 0,22 m | Coussin étalé de 44 brins courts |
| Touffe éventail | 0,45 m | Gerbe ouverte de 42 brins, centre décalé |
| Touffe courbée | 0,70 m | 44 brins en trois vagues inclinées |
| Hautes herbes | 1,05 m | 56 brins à plusieurs étages |

Chaque forme existe en **prairie verte**, **herbe dorée** et **mystique**,
soit **12 modèles**. La matière mystique relie des racines prune à des pointes
turquoise ; l’émission est limitée aux extrémités et reste discrète.
Les trois matières conservent exactement la même géométrie et les mêmes UV.

Les modèles comptent **10 668 à 14 224 triangles**, avec un seul maillage et un
seul matériau par touffe. Les racines appartiennent aux brins eux-mêmes.

Les surfaces utilisent un atlas peint 2K partagé par matière : couleur, rugosité
et émission. Les seize zones de l’atlas apportent des glacis longitudinaux,
de petites variations de teinte et des nervures douces. La crête géométrique
porte la lumière ; la texture n’ajoute pas de bruit fin. Les atlas sont créés
par le générateur Blender/numpy et incorporés aux GLB et fichiers Blender.

## Fichiers

- [Manifeste](manifest.json) : chemins, dimensions réelles et budgets.
- [Comparaison à la même échelle](comparaison-tailles.png).
- [Générateur](scripts/generate_grass.py) : géométrie, atlas et exports.
- [Comparaison Blender](comparaison-tailles.blend) et [générateur de la planche](scripts/render_comparison.py).
- [Contrôles](scripts/validate_grass.py) et [rapport](validation.json).

Chaque dossier `assets/<forme>-<matière>/` contient le GLB, le Blender éditable,
l’aperçu PNG et une notice JSON. Unités en mètres, pivot centré au sol ; Z vertical
dans Blender et Y vertical dans le GLB. L’éclairage, la caméra et le sol du studio
restent dans Blender et ne sont pas exportés avec la touffe.

## Reproduire

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python scripts/generate_grass.py -- --shape all --material all --render
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python scripts/render_comparison.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python scripts/validate_grass.py
```

La génération est déterministe. `--rebake` reconstruit les atlas ; `--gpu`
active OptiX pour les aperçus seulement. Le mode CPU fonctionne sans cette option.
Ces fichiers sont des ressources DA hors Unity ; LOD, collision et animation
du vent relèvent de l’intégration ultérieure.

## Contrôles effectués

Les 12 GLB et 12 fichiers Blender ont passé les contrôles : géométrie fermée
sans arêtes ouvertes ni faces dégénérées, données finies, UV, matériaux opaques,
textures 2048² intégrées, aucune dépendance externe et pivot au sol.
Les empreintes de géométrie sont identiques entre les trois matières d’une forme.
Les dimensions du manifeste correspondent aux maillages. Les douze aperçus
1100² et la planche comparative 2100 × 1050 sont fournis.
