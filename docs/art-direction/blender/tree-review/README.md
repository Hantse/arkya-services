# Arbre des Anciens — inspection 3D

La V2 a été retenue comme base de travail le 15 octobre 2026. Sa copie de
référence est classée dans [Direction artistique → Arbre de base](../../arbre-de-base/README.md),
avec la prochaine piste de texture plus mystique. Ce dossier conserve l'exploration
et le viewer local.

La version V2 est un nouvel arbre indépendant, reconstruit après rejet de
l'arbre de sous-bois extrait de la scène V3. Les scènes de direction artistique
et le premier export restent inchangés.

- `arkya-ancient-tree-v2.glb` : nouvel arbre affiché dans le viewer.
- `arkya-ancient-tree-v2.blend` : source Blender avec studio de présentation.
- `arkya-ancient-tree-v2-front.png` et `-back.png` : vues de contrôle.
- `arkya-ancient-tree.glb` et `.blend` : première extraction conservée.

La V2 utilise des racines et charpentières fusionnées, 62 réseaux de rameaux
portant réellement les feuilles et une couronne plus large. Les contreforts
se prolongent dans le tronc ; les branches centrales remplissent le haut.

## Viewer local

Depuis ce dossier, lancer `node serve-tree.mjs`, puis ouvrir l'URL affichée.
Le serveur choisit un port libre et reste limité à `127.0.0.1`.
Le viewer utilise un bundle local de `@google/model-viewer` afin de conserver
les textures PBR du GLB. Le viewer CAD installé simplifie ces matériaux.

Commandes : glisser avec le bouton gauche pour tourner, molette pour zoomer,
bouton droit pour déplacer la vue.

Le viewer montre l'asset sous son éclairage de présentation. L'éclairage et
l'atmosphère de la scène Blender complète ne font pas partie de cet export.

## Portée de l'asset

V2 : environ 9,44 × 9,66 × 10,13 m ; 429 204 triangles, 3 meshes,
18 600 feuilles. L'écorce utilise deux textures de 2048 px, les feuilles des
couleurs par sommet. Une occlusion douce du ciel est calculée pour chaque
feuille et intégrée aux couleurs pour garder du volume dans le viewer.
C'est une étude détaillée pour valider la direction
artistique ; la préparation des LOD et du budget MMO n'est pas effectuée.
Voir `arkya-ancient-tree-v2.json` pour les mesures détaillées.

## Régénération et contrôles

Exécuter `build_tree_v2.py` dans Blender en mode arrière-plan. Le script charge
`build_tree_structure_v2.py` et `build_tree_foliage_v2.py`. Ajouter `-- --draft`
pour des rendus rapides sans bake ni export GLB. Le générateur est déterministe.

Contrôles : construction Blender 4.5.14, continuité du bois, deux angles rendus
dans Cycles, matériaux incorporés au GLB et inspection dans le navigateur.
Aucun asset Unity n'est modifié.
