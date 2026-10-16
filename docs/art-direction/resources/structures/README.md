# Structures modulaires — premier kit

Quinze pièces de construction dans la continuité de la [DA peinte d'Arkya](../README.md) : cinq formes en **chaume, bois et pierre**. Cette collection est une **proposition à examiner**, pas encore une référence artistique approuvée.

« Patch » a été interprété comme *thatch*, donc paille / chaume sur ossature de bois. Les trois matières ont des assemblages propres : bottes de chaume et liens, planches sciées et poutres, maçonnerie à joints colorés. Les volumes sont adoucis et les textures peintes restent lisibles à distance.

## Dimensions

Toutes les mesures sont en mètres, échelle 1. Dans le viewer et le GLB, Y est la verticale.

| Pièce | Largeur | Hauteur | Profondeur | Ouverture libre |
|---|---:|---:|---:|---|
| Fondation | 1 | 0,50 | 1 | — |
| Mur plein | 1 | 1 | 0,15 | — |
| Mur avec fenêtre | 1 | 1 | 0,15 | 0,50 × 0,50 ; allège à 0,30 |
| Mur avec porte | 1 | 2 | 0,15 | 0,72 × 1,85 ; depuis le sol |
| Plafond | 1 | 0,10 | 1 | — |

La porte occupe deux niveaux de grille pour permettre un passage à hauteur humaine. Un mur plein et un mur avec fenêtre peuvent se superposer pour rejoindre sa hauteur. Les trous sont traversants et leurs tableaux possèdent une épaisseur réelle ; aucune image transparente ne simule une ouverture. Le kit fournit les encadrements, sans battant de porte ni vitre.

## Consultation et fichiers

Ouvrir [le viewer](http://127.0.0.1:51805/viewer/index.html), onglet **Structures**, puis choisir la forme et la matière. Les boutons **GLB** et **Blender** donnent accès à chaque modèle. Pour relancer le serveur, exécuter `node serve.mjs 51805` depuis le dossier `resources`.

Les fichiers sont dans `assets/<forme>-<matière>/` : source `.blend` éditable, export `.glb` avec textures incorporées, aperçu PNG du vrai modèle et fiche JSON. Les chemins, dimensions, budgets de triangles et points d'accrochage figurent dans le [manifeste](manifest.json).

| Triangles par pièce | Chaume | Bois | Pierre |
|---|---:|---:|---:|
| Fondation | 16 880 | 8 648 | 9 400 |
| Mur plein | 5 800 | 3 572 | 3 572 |
| Mur avec fenêtre | 8 896 | 5 264 | 5 076 |
| Mur avec porte | 8 388 | 4 512 | 4 888 |
| Plafond | 3 932 | 2 256 | 1 880 |

Les matériaux opaques utilisent les cartes peintes natives de 1 254 × 1 254 px, incorporées aux exports et aux sources. Chaque pièce utilise au plus trois matériaux ; aucun normal map ni shader spécifique n'est requis dans le viewer.

### Bois de construction V2 — 16 octobre 2026

Les cinq modules bois et les charpentes des cinq modules chaume reprennent le
[bois peint V2 du refuge](sources/worked-timber-painted-v2-provenance.md), plus
calme et moins orangé. Les planches ont une teinte multiplicative
`(1, 0.97, 0.91)`, la charpente `(0.78, 0.76, 0.71)` ; leur rugosité est `0.96`.
La texture native est incorporée sans modification aux GLB et aux sources
Blender. Le bitmap V1 reste archivé dans `sources/sawn-wood-painted.png`.

Le générateur conserve le sens local du fil sur chaque pièce et remplace les
UV étirés face par face par une densité de **1,2 m transversalement × 2,4 m
dans le sens du fil**. Ces matériaux autorisent des UV répétés ; les surfaces
de chaume, corde, pierre et mortier conservent leurs UV dans l'atlas 0–1.
Les mesures, pivots, accroches, ouvertures et volumes ne changent pas.

Les [contrôles de migration](wood-v2-validation.json) comparent les triangles
avant et après, indépendamment de l'ordre des sommets et des découpes UV :
géométrie et budgets identiques pour les quinze modules. Les **20 fichiers
des cinq modules pierre** restent identiques par SHA-256. Les dix aperçus
bois/chaume et la comparaison commune ci-dessous ont été rendus à nouveau.

![Les quinze structures à la même échelle](comparaison-tailles.png)

De haut en bas : pierre, bois, chaume. De gauche à droite : fondation, mur plein, mur avec fenêtre, mur avec porte et plafond.

## Contrat de placement

Chaque pièce possède une racine à échelle unitaire et un pivot au centre de sa base. Les extrémités restent dans leur gabarit nominal ; les détails décoratifs n'en dépassent pas. Le sol de présentation, la caméra et les lumières ne sont pas exportés.

La racine exportée porte la métadonnée `arkya_pivot = bottom_center`. Ce nom évite la clé `pivot`, que certains lecteurs glTF interprètent comme un vecteur de transformation.

Les six repères vides `Snap_Left`, `Snap_Right`, `Snap_Bottom`, `Snap_Top`, `Snap_Front` et `Snap_Back` décrivent les points d'accrochage. Le manifeste conserve leurs positions et normales dans le repère local glTF, Y vertical, ainsi que le gabarit nominal. Dans Blender, la conversion de ces positions est `(X, -Z, Y)`. Les normales sont des métadonnées ; elles ne sont pas déduites de la rotation des repères. Ces points préparent le placement sur grille ; ils ne constituent pas encore une logique de construction.

Pour une fondation posée à Y = 0, placer les murs à Y = 0,50. Deux niveaux de murs de 1 m atteignent Y = 2,50 ; le plafond commence alors à Y = 2,50. Les parois ont leur épaisseur centrée sur le plan du mur. Le traitement des angles, les règles de support et les collisions seront à intégrer au système de build.

## Sources et vérifications

Les textures de bois et de chaume ont été créées avec l'outil intégré imagegen.
Le bois V2 réutilise la génération existante du refuge, sans nouvel appel ni
retouche. Les bitmaps originaux et prompts exacts sont conservés dans
[sources/](sources/). La pierre reprend la [source peinte existante](../sources/stone-painted-provenance.md), copiée sans modification. Les modèles et les UV sont construits dans Blender ; les textures de production sont dans `textures/`.

Le [générateur](scripts/generate_structures.py) reproduit cette collection. Le [validateur](scripts/validate_structures.py) contrôle les quinze combinaisons, les mesures réelles, les UV et textures incorporées, les repères, les ouvertures et l'intégrité des références approuvées. Les résultats sont enregistrés dans [validation.json](validation.json).

La validation V2 a réussi pour les **15 GLB et les 15 sources Blender**. Elle
vérifie aussi le sampler Repeat des seuls matériaux bois, leurs teintes et
leur rugosité, ainsi que l'empreinte du PNG natif embarqué. Le contrôle strict
des UV 0–1 reste appliqué aux autres matériaux.

Depuis la racine du dépôt, avec Blender 4.5 LTS installé :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/structures/scripts/generate_structures.py' -- --shape all --material all --render --gpu --comparison
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python 'docs/art-direction/resources/structures/scripts/validate_structures.py' -- --blend
node docs/art-direction/resources/scripts/build-catalogue.mjs
```

Pour ne régénérer que les dix modules contenant du bois, exécuter le générateur
avec `--shape all --material bois --render --gpu`, puis avec
`--shape all --material chaume --render --gpu --comparison`. Le même validateur
contrôle ensuite les quinze variantes, y compris les cinq pierres conservées.

L'option `--gpu` suppose un GPU compatible OptiX ; l'omettre pour rendre sur CPU. Le contrôle géométrique GLB peut aussi être lancé avec Python et NumPy, sans Blender : `python docs/art-direction/resources/structures/scripts/validate_structures.py`. La vérification des sources `.blend` nécessite la commande Blender ci-dessus.

Les quinze pièces sont désormais aussi [intégrées au projet Unity actif](../../unity-test/structures/README.md), avec prefabs, BoxColliders, six accroches typées par pièce et catalogue. Les sources artistiques de ce dossier restent conservées hors Unity. Les LOD, la pose interactive, le coût, la résistance et la persistance des constructions restent à développer ; les décisions de gameplay appartiendront à la simulation serveur autoritaire.
