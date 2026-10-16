# Matières V3 — écorce et feuillage affinés

Passe du 15 octobre 2026, après le retour « c'est mieux, mais faut un peu
meilleur comme qualité ». La direction peinte de V2 est conservée. Cette
passe travaille la lecture du bois et la profondeur du feuillage sans changer
les silhouettes validées.

## Écorce peinte

La [texture source](source/painted-bark.png) apporte des fibres courbes, des
nœuds et des transitions peintes qui remplacent les simples rubans de V2.
Elle a été générée avec l'outil intégré imagegen, en 1254 × 1254 px natifs.
Le [prompt et la provenance](source/painted-bark-provenance.md) sont conservés.

Cette image sert de matière dans Blender. Les textures finales calculées pour
les UV du bois sont distinctes de l'image source. Le modèle, les rendus et les
exports GLB restent des livrables 3D réels.
Les coordonnées de peinture suivent les 70 courbes qui ont construit le
tronc, les branches et les racines, avec un fondu aux fourches. Cela évite les
fibres transversales que produisait une projection verticale uniforme.

Les trois ambiances sont conservées : prune et turquoise pour le mystique,
charbon et ambre pour le brûlé, bois miel et mousse sauge pour le moussu.
Les détails sont évalués dans le viewer avec le même éclairage que V2.

## Feuillage et cartes

Le feuillage reçoit une occlusion calculée sur la géométrie réelle : douze
rayons par feuille, soit 223 200 rayons. Les couleurs varient par masses de
couronne, avec des creux plus froids et des parties exposées plus claires.
Une [petite texture de feuille](source/leaf-painted-detail.png), calculée dans
Blender en 256 × 256 px, ajoute une nervure et un dégradé discrets.

Chaque matière comporte quatre cartes de bois en 4096 × 4096 px : couleur,
normale, rugosité et émission. Elles sont calculées sur les UV de bois
existants, à partir de la source peinte native de 1254 px ; la taille de
l'atlas ne représente pas la résolution de génération de cette source.
La matière moussue n'a pas d'émission visible.

Les trois meshes conservent leurs positions et leur topologie. Le feuillage
reçoit un canal UV pour sa texture, et ses nouvelles couleurs avec occlusion
sont exportées dans `COLOR_0`. Les GLB et les sources Blender incorporent leurs
textures. Aucun fichier du client Unity n'est modifié.

## Fichiers et comparaison

| Matière | Aperçus | Modèles |
|---|---|---|
| Mystique | [Arbre](01-mystique/01-mystique-preview.png) · [Tronc](01-mystique/01-mystique-tronc.png) | [GLB](01-mystique/01-mystique.glb) · [Blender](01-mystique/01-mystique.blend) |
| Brûlé | [Arbre](02-brule/02-brule-preview.png) · [Tronc](02-brule/02-brule-tronc.png) | [GLB](02-brule/02-brule.glb) · [Blender](02-brule/02-brule.blend) |
| Ancien moussu | [Arbre](03-moussu/03-moussu-preview.png) · [Tronc](03-moussu/03-moussu-tronc.png) | [GLB](03-moussu/03-moussu.glb) · [Blender](03-moussu/03-moussu.blend) |

Le viewer de la collection compare **Affiné V3 / Peint V2** au même point de
vue. Démarrer `node serve.mjs` depuis le dossier parent et ouvrir l'adresse
locale affichée. V1 et V2 restent dans leurs dossiers d'origine.

Sources : [générateur de matières](../scripts/generate_materials_v3.py),
[traitement du feuillage](../scripts/foliage_v3.py) et
[contrôles des fichiers](../scripts/validate_library.py).

Les [empreintes des onze fichiers de géométrie approuvés](formes-preservees.json)
confirment que la base, les quatre formes et leur scène de comparaison sont
inchangées.

## Vérification du 15 octobre 2026

Les [trois exports ont passé les contrôles](validation.json) : sources Blender
rouvertes, trois meshes et 429 204 triangles par modèle, positions finies,
textures incorporées et aucune dépendance externe. Les JSON de chaque modèle
confirment aussi des positions, une topologie et des UV de bois identiques à
la base. Les valeurs exportées de `COLOR_0` sont contrôlées pour éviter une
perte des couleurs et de l'occlusion du feuillage.

Les trois matières ont été inspectées dans le viewer local sous le même
éclairage neutre. La rotation de Mystique et la comparaison V2/V3 conservent le
point de vue. Captures du viewer : [Brûlé](viewer-brule.jpg),
[Ancien moussu](viewer-moussu.jpg) et [dernière vue](viewer-final.jpg).

Ces assets servent à juger la direction artistique. Leur niveau de détail
reste celui de la base ; les LOD pour une forêt jouable ne font pas partie de
cette passe de matières.
