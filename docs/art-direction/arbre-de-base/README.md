# Arbre de base — direction artistique Arkya

Version retenue comme base de travail le **15 octobre 2026**, après inspection
du chêne V2 dans le viewer 3D. La silhouette, les ramifications et le feuillage
servent de référence pour les prochains arbres.

**[Quatre déclinaisons de forme et trois matières](declinaisons/README.md)**
sont disponibles dans le sous-dossier `declinaisons/`, avec un viewer de
comparaison. Les quatre formes ont été validées le 15 octobre 2026. Les matières explorent les aspects mystique, brûlé et ancien
moussu. Les fichiers conservés ici correspondent à la version appréciée dans
le viewer et restent inchangés.

La [passe V3 affinée](declinaisons/matieres-v3/README.md) conserve les silhouettes
validées et approfondit les matières : source d'écorce peinte générée avec
imagegen en 1254 px natifs, cartes du bois en 4K, texture de feuille en 256 px
et occlusion recalculée avec 12 rayons de ciel par feuille. Le viewer compare
**Affiné V3 / Peint V2**. La [passe peinte V2](declinaisons/matieres-v2/README.md)
et la [première passe](declinaisons/matieres/) restent archivées ; l'état des
vérifications V3 est documenté dans son dossier.

![Arbre de base, vue avant](arkya-ancient-tree-v2-front.png)

## Fichiers

- [Source Blender](arkya-ancient-tree-v2.blend) : arbre modifiable, textures
  incorporées et studio de présentation.
- [Modèle GLB](arkya-ancient-tree-v2.glb) : arbre autonome pour un viewer 3D.
- [Vue avant](arkya-ancient-tree-v2-front.png) et
  [vue arrière](arkya-ancient-tree-v2-back.png) : rendus Cycles.
- [Couleur de l'écorce](arkya-ancient-tree-v2-bark-color.png) et
  [normales de l'écorce](arkya-ancient-tree-v2-bark-normal.png) : textures 2048 px.
- [Mesures et informations](arkya-ancient-tree-v2.json).

## Construction

Tronc courbe, racines en contreforts et branches fusionnées ; couronne dense
avec quelques ouvertures. Les 18 600 feuilles sont attachées à 62 réseaux de
rameaux. Des couleurs par sommet et une occlusion douce donnent du volume
au feuillage dans le viewer.

Dimensions : environ **9,44 × 9,66 × 10,13 m**. Le modèle détaillé comporte
**429 204 triangles et 3 meshes**. Il constitue une référence de direction
artistique ; les LOD et l'optimisation pour le MMORPG restent à préparer.

Les trois générateurs sont conservés ensemble : `build_tree_v2.py`,
`build_tree_structure_v2.py` et `build_tree_foliage_v2.py`. Exécuter le premier
avec Blender 4.5.14 en arrière-plan pour régénérer les fichiers dans ce dossier.
Utiliser une copie de travail pour une prochaine variante afin de conserver
cette base retenue.

## Provenance et contrôle

Copie de la V2 finalisée dans `../blender/tree-review/`, avec vérification
SHA-256 des dix fichiers copiés. Les rendus avant/arrière et le GLB ont été
inspectés lors de la création. La source Blender a également été rouverte :
les deux textures sont incorporées et aucune dépendance externe n'a été
détectée. Le GLB contient ses images et son buffer sans URI externe.
L'exploration précédente et son viewer restent
disponibles dans ce dossier d'origine. Aucun asset Unity n'a été modifié.
