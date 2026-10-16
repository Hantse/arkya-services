# Arbres — formes et matières

Exploration du 15 octobre 2026 à partir de l'[arbre de base retenu](../README.md).
Les quatre silhouettes ont été validées le 15 octobre 2026. Les matières
restent en exploration. La base conservée dans le dossier parent reste inchangée.

## Viewer 3D

Lancer `node serve.mjs` depuis ce dossier, puis ouvrir l'adresse locale affichée.
Le viewer propose la base et les sept déclinaisons, avec rotation, zoom,
recentrage et dimensions. Il utilise une copie locale de `@google/model-viewer`
4.4.0 ; les modèles et textures sont servis localement.

L'onglet **Formes** compare les silhouettes avec la matière d'origine.
L'onglet **Matières** compare trois ambiances sur la géométrie de base identique.
La [passe V3 affinée](matieres-v3/README.md) poursuit le traitement peint de V2.
Le sélecteur **Affiné V3 / Peint V2** compare les matières sans changer de point
de vue. La [passe peinte V2](matieres-v2/README.md) et la
[première passe](matieres/) restent conservées.
Chaque modèle est cadré automatiquement ; l'image ci-dessous permet de comparer
leurs tailles réelles ensemble.

## Quatre formes

![Silhouettes à la même échelle](formes/comparaison-formes-meme-echelle.png)

| Variante | Hauteur | Largeur | Intention | Fichiers |
|---|---:|---:|---|---|
| Jeune | 5,50 m | 4,21 m | Petit tronc fin, couronne compacte | [GLB](formes/01-jeune/01-jeune.glb) · [Blender](formes/01-jeune/01-jeune.blend) |
| Étalé | 8,00 m | 13,87 m | Tronc court, grande couronne en ombrelle | [GLB](formes/02-etale/02-etale.glb) · [Blender](formes/02-etale/02-etale.blend) |
| Élancé | 14,00 m | 6,75 m | Fourches hautes, silhouette verticale | [GLB](formes/03-elance/03-elance.glb) · [Blender](formes/03-elance/03-elance.blend) |
| Vénérable | 16,00 m | 16,34 m | Tronc massif, grands contreforts, couronne courbe | [GLB](formes/04-venerable/04-venerable.glb) · [Blender](formes/04-venerable/04-venerable.blend) |

Les proportions du tronc, du houppier, des racines et des feuilles varient
séparément. Les liaisons entre branches et feuilles sont conservées. Les
couleurs et l'occlusion du feuillage de la base sont réutilisées.
Les mesures complètes sont dans [formes.json](formes/formes.json).

## Trois matières

| Matière | Aspect | Fichiers |
|---|---|---|
| Mystique | Bois prune peint, fibres et nœuds, accents turquoise | [Aperçu](matieres-v3/01-mystique/01-mystique-preview.png) · [Détail](matieres-v3/01-mystique/01-mystique-tronc.png) · [GLB](matieres-v3/01-mystique/01-mystique.glb) · [Blender](matieres-v3/01-mystique/01-mystique.blend) |
| Brûlé | Charbon bleuté, cendres douces, fentes ambrées | [Aperçu](matieres-v3/02-brule/02-brule-preview.png) · [Détail](matieres-v3/02-brule/02-brule-tronc.png) · [GLB](matieres-v3/02-brule/02-brule.glb) · [Blender](matieres-v3/02-brule/02-brule.blend) |
| Ancien moussu | Bois miel visible, grandes zones de mousse sauge | [Aperçu](matieres-v3/03-moussu/03-moussu-preview.png) · [Détail](matieres-v3/03-moussu/03-moussu-tronc.png) · [GLB](matieres-v3/03-moussu/03-moussu.glb) · [Blender](matieres-v3/03-moussu/03-moussu.blend) |

V3 utilise une [source d'écorce peinte générée avec imagegen](matieres-v3/source/painted-bark-provenance.md),
de **1254 × 1254 px natifs**, puis des cartes de bois calculées pour les UV en
**4096 × 4096 px** : couleur, normale, rugosité et émission. Le feuillage reçoit
une texture de lame de **256 × 256 px** et une occlusion recalculée avec
**12 rayons de ciel par feuille**, portée par les couleurs des sommets.
Les positions et la topologie des silhouettes validées sont conservées.
Le [dossier V3](matieres-v3/README.md) détaille les fichiers et l'état des vérifications.

## Sources et vérifications

- [Générateur de formes](scripts/generate_shapes.py).
- [Générateur de matières V3](scripts/generate_materials_v3.py) et
  [feuillage V3](scripts/foliage_v3.py).
- [Générateur de matières V2](scripts/generate_materials_v2.py) et
  [première passe conservée](scripts/generate_materials.py).
- [Validation des fichiers](scripts/validate_library.py) et [résultat](validation.json).
- [Catalogue du viewer](catalogue.json).

Les contrôles historiques ci-dessous concernent les formes et les premières
matières ; ils ne constituent pas une validation complète de V3. Le suivi de
cette passe est centralisé dans [ses vérifications](matieres-v3/README.md).

Les sept sources Blender précédentes ont été rouvertes avec Blender 4.5.14 ; leurs images
sont incorporées et aucune bibliothèque externe n'est liée. Les sept GLB ont
été contrôlés : trois meshes par fichier, positions finies, buffers valides,
textures incorporées et absence d'URI externe. Les matières conservent les
positions, la topologie et les UV de la base. Les rendus ont été inspectés.
Les sept déclinaisons ont également été sélectionnées dans le viewer local ;
les trois matières et les silhouettes élancée et étalée ont été inspectées à
l'écran. Une [capture du viewer](viewer-apercu.jpg) conserve sa présentation.
Les empreintes SHA-256 de la source Blender, du GLB et du générateur principal
de la base retenue sont identiques à celles relevées avant cette exploration.

Ces arbres détaillés conservent **429 204 triangles chacun**. Ils servent à
valider la direction artistique ; les LOD et les budgets de rendu pour une
forêt jouable restent à préparer. Aucun asset Unity n'est modifié.
