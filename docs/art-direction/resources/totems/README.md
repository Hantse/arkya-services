# Familiers totems — loup et renard

Le loup V10 poursuit la cible précisée par l'utilisateur : **le loup blanc debout en bas à droite de la première référence**. La cage thoracique, le poitrail et la continuité collet–nuque gagnent en masse ; le haut des cuisses accompagne cette carrure. La gueule récupère le soutien de la base et de la mandibule V8, tout en gardant les joues reculées de V9. Le bout du museau est légèrement relevé dans son ensemble, avec une truffe plus transversale. La bouche est légèrement entrouverte et laisse voir les petites dents du modèle source. Les petits yeux, les oreilles, les appuis et le squelette sont conservés. Les surfaces restent lissées et peintes selon la DA Arkya. Le renard V2 est conservé. Cette collection reste **à examiner artistiquement**. La [liaison à une âme animale dès le départ](../../../GAMEPLAY_TOTEMS.md) est une direction de gameplay confirmée ; les assets livrés concernent sa présentation.

## Examiner les modèles

Ouvrir [l'atelier des totems](http://127.0.0.1:51805/totems/viewer/index.html). Depuis la racine du dépôt :

```powershell
node docs/art-direction/resources/serve.mjs 51805
```

Choisir l'animal, son pelage, puis Repos, Marche ou Course. Le mode **Ralenti × 0,5**, la pause et la caméra libre permettent d'examiner les appuis, la déformation et les courants lumineux. La bande de palette montre les douze teintes de l'apparence sélectionnée. Les téléchargements donnent le GLB courant et les sources Blender/FBX de l'espèce.

## Volumes, pelages et provenance

Le **loup V10** adapte **« 3d wolf » par NewDLC**, sous **CC0 1.0**. La [provenance NewDLC](sources/newdlc/README.md) conserve le fichier original `dog2.blend`, ses textures, la licence et leurs empreintes. Son anatomie canine fournit le crâne fermé, le museau et la mâchoire. L'adaptation Arkya enlève les mèches frontales en cartes séparées, compacte les oreilles, ramène la mâchoire vers une position de repos et abaisse la queue. Les volumes du visage viennent de cette source de loup ; la V3 issue du renard reste une étape historique rejetée.

La V7 reconstruit le crâne et le museau à partir de la base canine V4, avec une seule réduction progressive de la section terminale. Elle part de la carrure V6 puis recompose le raccord de la tête au haut du thorax. Le port bas modifie les os `Neck`, `Head` et `Ear.L/R` ; le squelette des membres, les pieds et le bas du corps conservent leurs positions à la précision numérique du contrôle. La normalisation V4 est conservée. La V8 ajoute uniquement une finition locale du front, de la pommette et de la bouche, sans modifier le port de tête, la forme générale du museau, les yeux, les oreilles ni le rig de V7. La V9 affine les plans du museau et les joues antérieures, avec des transitions larges autour des orbites et de la commissure. La carrure, le corps, les appuis, les yeux, les oreilles et le squelette V8 sont conservés. La V10 rééquilibre ensuite le corps : thorax plus large et profond, poitrail et collet continus, haut des cuisses renforcé. La largeur additionnelle est modérée séparément de la profondeur. Sa gueule reprend une grande partie du soutien V8 et conserve les plans arrière des joues V9 ; le museau entier est légèrement relevé, sans rallonge. Dans `assets/anatomy/wolf-rig.json`, `headRefinement` compare V6/V10 ; les étapes `surfaceRefinement` (V7/V8) et `muzzleRefinement` (V8/V9) restent historiques, tandis que `bodyRefinement` décrit V9/V10.

La peinture prolonge les joues claires en diagonale sur les côtés du museau, conserve le chanfrein et la truffe sombres, puis adoucit la transition sous le menton. La V8 supprime le trait de sourcil résiduel de la texture source, sans repeindre les yeux. La V9 étire les joues claires vers la collerette ; une borne dans `SourcePosition` maintient ces ajouts sous les oreilles et le front. L'attribut `PaintPosition` conserve les coordonnées V6 des pigments d'origine ; `SourcePosition` décrit la géométrie finale. Ces transitions accompagnent la géométrie ; les originaux NewDLC restent inchangés.

Le **renard V2** conserve la base texturée **« Fox » par Bake3D**, sous licence **CC BY 4.0**, sa silhouette, ses grandes oreilles, son museau fin, sa queue ample, ses UV et les détails de son visage. La [provenance Bake3D](sources/bake3d/README.md) documente cette source et les adaptations Arkya. Son attribution doit accompagner les dérivés et apparaître dans les crédits du jeu.

Les deux provenances restent distinctes : CC0 NewDLC pour le loup V10, CC BY Bake3D pour le renard V2. Arkya conserve les crédits des deux auteurs sans suggérer leur approbation du projet. Le rig de présentation, les poids, les palettes, les marques magiques et les animations sont adaptés à chaque espèce ; ces modèles restent des dérivés de sources tierces.

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
| Loup V10 | 1,860 × 0,469 × 0,857 m | 31 090 | 39 os, 4 influences maximum |
| Renard V2 | 0,798 × 0,244 × 0,644 m | 30 382 | 39 os, 4 influences maximum |

La longueur comprend la queue et la hauteur les oreilles ; la légère flexion de la pose animée explique la différence avec les hauteurs neutres de 0,866 m et 0,65 m. Chaque animal utilise trois meshes skinnés et trois matériaux : `TotemFur`, `TotemMagic` et `TotemSpark`. Le pelage compte 29 508 triangles pour le loup V10 et 28 800 pour le renard V2 ; chaque espèce ajoute 1 102 triangles de marques et 480 de points lumineux. Les douze pelages d'une espèce partagent la même géométrie et les mêmes poids. Les dimensions visuelles ne définissent pas les collisions.

## Locomotion et intégration

Les clips **Idle**, **Walk** et **Run** restent **sur place**. Idle anime la respiration ; Walk suit une marche à quatre temps ; Run utilise un galop. Les trajectoires distinguent l'appui et le retour de la patte. Un solveur IK replace les segments des membres et conserve les contacts pendant l'appui, avec des marqueurs `Contact.Fore.L/R` et `Contact.Hind.L/R` exportés dans le rig.

Les durées et vitesses virtuelles en mètres par seconde sont disponibles dans `manifest.species[].locomotion.clips[]` et les rapports de locomotion, champ `expected_speed_mps`. Le déplacement du personnage et la vitesse de lecture devront être raccordés à ces valeurs pour conserver les appuis dans le monde. Le viewer affiche la vitesse attendue du clip ; son ralenti modifie seulement la lecture. Les appuis sur terrain irrégulier et les LOD de production restent à préparer.

| Espèce | Idle | Walk | Run |
| --- | --- | --- | --- |
| Loup V10 | 4,0 s ; 0 m/s | 1,0 s ; 0,424 m/s | 0,7 s ; 1,514 m/s |
| Renard V2 | 4,0 s ; 0 m/s | 1,0 s ; 0,187 m/s | 0,6 s ; 0,669 m/s |

Les vitesses du tableau sont arrondies ; utiliser les valeurs JSON pour le raccord au déplacement.

## Régénération

Les scripts utilisent Blender 4.5. L'ancien point d'entrée `scripts/generate_totems.py` est **obsolète** et conservé comme relais de compatibilité vers `build_totems_v2.py` ; ce dernier produit le loup V10 et le renard V2 à partir de leurs sources anatomiques actuelles. `build_anatomy.py` délègue le loup à `build_wolf_anatomy.py`, qui utilise V10 par défaut, et conserve le générateur Bake3D pour le renard. Les commandes actuelles, depuis la racine du dépôt, sont :

```powershell
# Reconstruire les sources anatomiques neutres.
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/totems/scripts/build_anatomy.py' -- wolf fox

# Construire palettes, marques, clips et exports de chaque espèce.
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/totems/scripts/build_totems_v2.py' -- --species wolf
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/totems/scripts/build_totems_v2.py' -- --species fox
```

`--lookdev` produit un aperçu de la finition ; `--draft` limite la génération à l'apparence par défaut. `--skip-renders` régénère les assets en conservant les aperçus existants ; ne l’utiliser que si ces aperçus correspondent déjà à la géométrie courante. Les PNG sont enregistrés dans un fichier temporaire puis remplacés pour éviter les conflits avec un aperçu Windows ouvert. La génération complète produit les douze variantes et actualise les métadonnées de l'espèce dans le manifeste.

L'appel direct à `build_wolf_anatomy.py` sans `--output-dir` écrit dans `assets/anatomy/draft-wolf-v10/`. Les options `--revision 4`, `--revision 5`, `--revision 6`, `--revision 7`, `--revision 8` et `--revision 9` reconstruisent les anatomies précédentes dans leurs propres dossiers de brouillon. Elles ne remplacent les sources de production que si leur dossier est explicitement passé avec `--output-dir`.

## Vérifications reproductibles

```powershell
python docs/art-direction/resources/totems/scripts/verify_exports.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/totems/scripts/verify_fbx_roundtrip.py'
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python-exit-code 1 --python 'docs/art-direction/resources/totems/scripts/verify_locomotion_skin.py' -- --species all
```

Le [contrôle GLB indépendant](export-verification.json) lit les buffers et vérifie géométrie, indices, UV, poids, textures incorporées et boucles. Le [réimport FBX](fbx-roundtrip-verification.json) mesure la déformation du maillage après ouverture dans des scènes Blender vides. Les rapports `*-validation.json` décrivent les meshes et les variantes réellement exportées.

Les rapports `*-locomotion.json` mesurent les contacts, la portée IK et les raccords de boucle. Le contrôle de peau [loup](wolf-skin-verification.json) / [renard](fox-skin-verification.json) réévalue les sommets de `TotemFur` à 120 Hz : sol plan Z=0, déplacement réel de chaque pied et continuité à la boucle. Ses seuils sont de 2 mm pour la pénétration et de 1 mm pour le raccord. Il consigne les empreintes des sources et du code utilisés. Il ne teste ni les meshes magiques, ni le terrain irrégulier, ni l'import Unity. Il complète les contrôles après export ; un contrôle du squelette seul ne suffit pas à valider une allure.

Le [rapport ciblé du viewer V10](viewer-v10-verification.json) décrit les essais sur quatre pelages du loup et les trois clips sur ordinateur, ainsi que les commandes et liens de téléchargement. Le [rapport complet du viewer](viewer-verification.json) reste l'historique V5 ; ses 24 chargements et ses contrôles mobiles ne constituent pas une nouvelle passe sur V10. Ces vérifications techniques ne valent pas approbation artistique.

La livraison V10 `20261016T150222819563Z-totems` est associée aux rapports [GLB](export-verification.json), [réimport FBX](fbx-roundtrip-verification.json) et [peau à 120 Hz](wolf-skin-verification.json), avec des contrôles distincts après import Unity. Le [contrôle de conservation V10](wolf-v10-preservation.json) compare les sources V9/V10 : les pieds, les os et les régions protégées sont conservés, tandis que la carrure et la gueule changent volontairement. Les fichiers du renard et les originaux NewDLC restent inchangés. Le [contrôle source V10](wolf-v10-source-verification.json) mesure la conservation du bas des membres, de la queue, des yeux, oreilles, UV, poids et coordonnées `PaintPosition`, ainsi que l'absence de triangles retournés ou dégénérés. Les retouches V9/V10 sont consignées dans `bodyRefinement`.

## Périmètre de jeu

L'intégration est isolée sous `Unity3D/Arkya/Assets/Arkya/Totems/`, séparée des packs Scinty et du projet historique `My project/`. Le choix du totem à la création, son association persistante au personnage, son suivi dans le monde, ses capacités et sa réplication réseau restent à implémenter. Aucune couleur ne confère de bonus de gameplay.
