# Familiers totems — loup et renard

Le loup V3 reprend la tête, les oreilles, la silhouette et le pelage pour mieux distinguer les deux espèces. Le renard V2 est conservé. Cette collection reste **à examiner artistiquement**. La [liaison à une âme animale dès le départ](../../../GAMEPLAY_TOTEMS.md) est une direction de gameplay confirmée ; les assets livrés concernent sa présentation.

## Examiner les modèles

Ouvrir [l'atelier des totems](http://127.0.0.1:51805/totems/viewer/index.html). Depuis la racine du dépôt :

```powershell
node docs/art-direction/resources/serve.mjs 51805
```

Choisir l'animal, son pelage, puis Repos, Marche ou Course. Le mode **Ralenti × 0,5**, la pause et la caméra libre permettent d'examiner les appuis, la déformation et les courants lumineux. La bande de palette montre les douze teintes de l'apparence sélectionnée. Les téléchargements donnent le GLB courant et les sources Blender/FBX de l'espèce.

## Volumes, pelages et provenance

La base texturée provient de **« Fox » par Bake3D**, sous licence [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Le renard V2 conserve sa silhouette, ses grandes oreilles, son museau fin, sa queue ample, ses UV et les détails de son visage.

Le loup V3 adapte cette base avec une tête plus large, un museau épais, un regard resserré et de petites oreilles triangulaires espacées. Le thorax et l'encolure sont plus forts, le dos allongé et la queue basse et fuselée. Le pelage peint atténue les démarcations en « chaussettes » sur les pattes. Ces différences portent sur les volumes et les proportions autant que sur les couleurs. Les articulations, les poids et les appuis IK suivent l'anatomie de chaque espèce.

La [provenance complète](sources/bake3d/README.md) conserve le fichier original, la licence et les modifications. La [page officielle Bake3D](https://bake3d.ai/free-3d-models) autorise les adaptations et leur redistribution avec attribution. Le crédit doit accompagner les dérivés et apparaître dans les crédits du jeu ; aucune approbation d'Arkya par Bake3D n'est suggérée.

Les deux animaux disposent chacun de **douze variantes de pelage**, soit **24 apparences**. Chaque palette comporte **douze teintes de référence** ; les dégradés peints et l'éclairage ajoutent des nuances intermédiaires. Les marques magiques suivent la surface et ses poids de déformation. Des points lumineux parcourent leurs courbes pendant les animations.

| Pelage | Identifiant |
| --- | --- |
| Lune astrale | `lunar` |
| Braise rousse | `ember` |
| Jade spirituel | `jade` |
| Améthyste | `amethyst` |
| Givre boréal | `frost` |
| Or solaire | `solar` |
| Rose d'aube | `rose` |
| Abysses | `abyss` |
| Mousse ancienne | `moss` |
| Cuivre enchanté | `copper` |
| Perle onirique | `pearl` |
| Tempête | `storm` |

## Fichiers et budgets

- [Manifeste](manifest.json) : espèces, palettes, chemins, dimensions et métadonnées de locomotion.
- `models/` : 24 GLB autonomes contenant texture, squelette, poids et clips.
- [Loup Blender](assets/wolf.blend) et [renard Blender](assets/fox.blend) : sources éditables avec leur apparence de référence et les animations.
- [Loup FBX](assets/wolf.fbx) et [renard FBX](assets/fox.fbx) : exports riggés destinés à Unity.
- `textures/` : 24 textures de pelage de 2 048 × 2 048 pixels.
- [Sources anatomiques](assets/anatomy/README.md) : maillages neutres, squelette et repères mesurés, avant palettes, magie et animation.
- `wolf-locomotion.json` et `fox-locomotion.json` : trajectoires des quatre pattes, phases d'appui, vitesses attendues et contrôles du solveur.
- `scripts/` : construction de l'anatomie, finition, animation et vérifications.
- [Guide de l'atelier](viewer/README.md) et [intégration Unity](../../unity-test/totems/README.md).

Les dimensions et budgets des modèles livrés sont inscrits dans le manifeste :

| Modèle | Longueur × largeur × hauteur, pose Idle | Triangles | Rig |
| --- | --- | ---: | --- |
| Loup V3 | 1,791 × 0,580 × 1,085 m | 30 382 | 39 os, 4 influences maximum |
| Renard | 0,798 × 0,244 × 0,644 m | 30 382 | 39 os, 4 influences maximum |

La longueur comprend la queue et la hauteur les oreilles ; la légère flexion de la pose animée explique la différence avec les hauteurs neutres de 1,10 m et 0,65 m. Chaque animal utilise trois meshes skinnés et trois matériaux : `TotemFur`, `TotemMagic` et `TotemSpark`. Le pelage compte 28 800 triangles, les marques 1 102 et les points lumineux 480. Les douze pelages partagent la même géométrie et les mêmes poids. Les dimensions visuelles ne définissent pas les collisions.

## Locomotion et intégration

Les clips **Idle**, **Walk** et **Run** restent **sur place**. Idle anime la respiration ; Walk suit une marche à quatre temps ; Run utilise un galop. Les trajectoires distinguent l'appui et le retour de la patte. Un solveur IK replace les segments des membres et conserve les contacts pendant l'appui, avec des marqueurs `Contact.Fore.L/R` et `Contact.Hind.L/R` exportés dans le rig.

Les durées et vitesses virtuelles en mètres par seconde sont disponibles dans `manifest.species[].locomotion.clips[]` et les rapports de locomotion, champ `expected_speed_mps`. Le déplacement du personnage et la vitesse de lecture devront être raccordés à ces valeurs pour conserver les appuis dans le monde. Le viewer affiche la vitesse attendue du clip ; son ralenti modifie seulement la lecture. Les appuis sur terrain irrégulier et les LOD de production restent à préparer.

| Espèce | Idle | Walk | Run |
| --- | --- | --- | --- |
| Loup V3 | 4,0 s ; 0 m/s | 1,0 s ; 0,441 m/s | 0,7 s ; 1,575 m/s |
| Renard | 4,0 s ; 0 m/s | 1,0 s ; 0,187 m/s | 0,6 s ; 0,669 m/s |

Les vitesses du tableau sont arrondies ; utiliser les valeurs JSON pour le raccord au déplacement.

## Régénération

Les scripts utilisent Blender 4.5. L'ancien point d'entrée `scripts/generate_totems.py` est **obsolète** et conservé comme relais de compatibilité vers `build_totems_v2.py` ; ce dernier produit le loup V3 et le renard V2 à partir de leurs sources anatomiques actuelles. Les commandes actuelles sont :

```powershell
# Reconstruire les sources anatomiques neutres.
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/totems/scripts/build_anatomy.py' -- wolf fox

# Construire palettes, marques, clips et exports de chaque espèce.
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/totems/scripts/build_totems_v2.py' -- --species wolf
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/totems/scripts/build_totems_v2.py' -- --species fox
```

`--lookdev` produit un aperçu de la finition ; `--draft` limite la génération à l'apparence par défaut. La génération complète produit les douze variantes et actualise les métadonnées de l'espèce dans le manifeste.

## Vérifications reproductibles

```powershell
python docs/art-direction/resources/totems/scripts/verify_exports.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/totems/scripts/verify_fbx_roundtrip.py'
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python-exit-code 1 --python 'docs/art-direction/resources/totems/scripts/verify_locomotion_skin.py' -- --species all
```

Le [contrôle GLB indépendant](export-verification.json) lit les buffers et vérifie géométrie, indices, UV, poids, textures incorporées et boucles. Le [réimport FBX](fbx-roundtrip-verification.json) mesure la déformation du maillage après ouverture dans des scènes Blender vides. Les rapports `*-validation.json` décrivent les meshes et les variantes réellement exportées.

Les rapports `*-locomotion.json` mesurent les contacts, la portée IK et les raccords de boucle. Le contrôle de peau [loup](wolf-skin-verification.json) / [renard](fox-skin-verification.json) réévalue les sommets de `TotemFur` à 120 Hz : sol plan Z=0, déplacement réel de chaque pied et continuité à la boucle. Ses seuils sont de 2 mm pour la pénétration et de 1 mm pour le raccord. Il consigne les empreintes des sources et du code utilisés. Il ne teste ni les meshes magiques, ni le terrain irrégulier, ni l'import Unity. Il complète les contrôles après export ; un contrôle du squelette seul ne suffit pas à valider une allure.

Le [rapport du viewer](viewer-verification.json) consigne les essais de chargement et des commandes. Ces vérifications techniques ne valent pas approbation artistique.

## Périmètre de jeu

L'intégration est isolée sous `Unity3D/Arkya/Assets/Arkya/Totems/`, séparée des packs Scinty et du projet historique `My project/`. Le choix du totem à la création, son association persistante au personnage, son suivi dans le monde, ses capacités et sa réplication réseau restent à implémenter. Aucune couleur ne confère de bonus de gameplay.
