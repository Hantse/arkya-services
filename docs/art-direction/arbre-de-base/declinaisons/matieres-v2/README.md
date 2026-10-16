# Matières V2 — traitement peint

Passe du 15 octobre 2026 demandée après validation des quatre formes. La
première passe de textures était jugée trop brute. L'objectif est un rendu
stylisé plus doux, inspiré de la lisibilité des matières d'Overwatch et de
Palworld, tout en conservant la géométrie propre à Arkya.

## Intention

- Écorce : quelques sillons larges, dégradés peints, relief léger.
- Feuillage : palette resserrée, grandes transitions de couleur et ombres
  adoucies ; conserver la séparation entre l'intérieur et l'extérieur de la
  couronne.
- Mystique : bois prune et indigo, accents turquoise rares et espacés.
- Brûlé : charbon bleuté, cendres douces et petites fentes ambrées.
- Moussu : bois miel encore visible, larges îlots de mousse sauge.

Les veines fines, le grain moucheté et les reliefs granuleux de la première
passe sont remplacés par de plus grandes formes peintes. Les trois matières
sont comparées sur la géométrie de base, sans modifier ses positions, UV ou
topologie.

## Fichiers

| Matière | Aperçus | Modèles |
|---|---|---|
| Mystique | [Arbre](01-mystique/01-mystique-preview.png) · [Tronc](01-mystique/01-mystique-tronc.png) | [GLB](01-mystique/01-mystique.glb) · [Blender](01-mystique/01-mystique.blend) |
| Brûlé | [Arbre](02-brule/02-brule-preview.png) · [Tronc](02-brule/02-brule-tronc.png) | [GLB](02-brule/02-brule.glb) · [Blender](02-brule/02-brule.blend) |
| Ancien moussu | [Arbre](03-moussu/03-moussu-preview.png) · [Tronc](03-moussu/03-moussu-tronc.png) | [GLB](03-moussu/03-moussu.glb) · [Blender](03-moussu/03-moussu.blend) |

Chaque sous-dossier contient le modèle Blender, le GLB autonome, les textures
PNG 2K, un aperçu de l'arbre, une vue rapprochée du tronc et les métadonnées.
Le [générateur](../scripts/generate_materials_v2.py) conserve les paramètres
de la passe. La première version reste disponible dans `../matieres/`.

Dans le [viewer de la collection](../viewer/index.html), le sélecteur
**Peint / Première passe** compare les deux traitements depuis le même
point de vue. Démarrer le serveur avec `node serve.mjs` depuis le dossier
parent pour ouvrir le viewer.

## Vérifications

Les trois fichiers Blender ont été rouverts et les GLB contrôlés : positions,
topologie et UV identiques à la base, trois meshes, textures et couleurs de
sommets incorporées, aucune dépendance externe. Les bakes sont calculés à
4096 px puis réduits à 2048 px ; le fond teinté évite les points noirs sur les
minuscules îlots UV. Les six aperçus ont été inspectés.

Les [résultats de validation](validation.json) et les
[empreintes des formes préservées](formes-preservees.json) sont conservés.
Le test du sélecteur dans le viewer confirme que le point de vue reste le
même entre la version peinte et la première passe.
Les trois matières finales ont été affichées et inspectées dans le viewer
local. Un [aperçu du viewer](viewer-apercu.jpg) est conservé.
