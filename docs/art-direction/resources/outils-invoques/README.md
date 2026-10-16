# Outils invoqués — couleurs et sockets

Une hache, une pioche et une faucille de récolte, créées le 15 octobre 2026 dans la continuité
des [références artistiques d'Arkya](../README.md). **Proposition à examiner :
ces outils ne sont pas encore des références approuvées.**

Les grandes surfaces peintes, les ombres colorées et les arêtes adoucies
reprennent le traitement de l'arbre V3. Le métal, le bois et la prise en cuir
restent distincts. Le cœur, les signes lumineux et les fragments en suspension
se déclinent en **bleu azur, orange ambré et vert émeraude**, dans la continuité
des mares de mana. L'émission reste lisible sans bloom.

- **Hache** : tête asymétrique à large tranchant courbe.
- **Pioche** : tête courbe à bec effilé et panne courte, distincte de la hache.
- **Faucille** : lame en croissant recourbée et manche court pour une prise à une main.
- **Magie** : fragments flottants animés en boucle de quatre secondes.
- **Sockets** : zéro, une, deux ou trois cupules vides sur la face avant de la tête.

La collection compte **36 modèles : 3 outils × 3 couleurs × 4 configurations
de sockets**. Les deux modèles bleus initiaux sont conservés à l'identique,
comme variantes à zéro socket. Leurs empreintes sont dans [baseline-v1.json](baseline-v1.json).
Les 24 variantes de haches et de pioches ainsi que leurs textures sont également
préservées lors de l'ajout de la faucille : [empreintes avant ajout](baseline-before-sickle.json).

## Consultation

Depuis le dossier `resources`, lancer `node serve.mjs 51805`, puis ouvrir
[le viewer](http://127.0.0.1:51805/viewer/index.html), onglet **Outils invoqués**.
Choisir indépendamment l'outil, sa couleur et **0 / 1 / 2 / 3 sockets vides**.
La couleur et le nombre de sockets sont conservés lorsqu'on change d'outil ;
la case **Magie animée** permet de figer la boucle.
Les liens **GLB** et **Blender** donnent accès aux modèles et à leurs sources.

Les chemins, dimensions et budgets exacts sont conservés dans le
[manifeste](manifest.json). Les aperçus présentent les vrais modèles exportés.

| Outil azur, sans socket | Hauteur avec éclats, pose initiale | Triangles | Modèle | Source |
|---|---:|---:|---|---|
| Hache invoquée | 1,28 m | 7 768 | [GLB](assets/hache-azur/hache-azur.glb) | [Blender](assets/hache-azur/hache-azur.blend) |
| Pioche invoquée | 1,30 m | 7 692 | [GLB](assets/pioche-azur/pioche-azur.glb) | [Blender](assets/pioche-azur/pioche-azur.blend) |
| Faucille invoquée | 1,14 m | 8 092 | [GLB](assets/faucille-azur/faucille-azur.glb) | [Blender](assets/faucille-azur/faucille-azur.blend) |

Chaque modèle comporte cinq matériaux opaques et dix textures 2048 × 2048.
Les téléchargements des 36 variantes sont proposés par le viewer et listés
dans le manifeste. Pour un même outil et un même nombre de sockets, les trois
couleurs partagent leur géométrie, leurs points d'attache et leur animation.

| Outil | 0 socket | 1 socket | 2 sockets | 3 sockets |
|---|---:|---:|---:|---:|
| Hache, triangles | 7 768 | 8 404 | 9 040 | 9 676 |
| Pioche, triangles | 7 692 | 8 290 | 8 874 | 9 472 |
| Faucille, triangles | 8 092 | 8 728 | 9 364 | 10 000 |

![Hache, pioche et faucille à la même échelle](comparaison-tailles.png)

[Comparer les couleurs](comparaison-couleurs.png) · [Comparer les sockets](comparaison-sockets.png)

## Emplacements pour les cristaux

Le **cœur de mana décoratif préexistant ne compte pas comme socket**. Les
emplacements supplémentaires sont vides : rebord en bronze, paroi intérieure
et fond sombre en retrait. Ils sont placés sur la tête, à distance du tranchant
et de la prise en main. Aucun cristal d'amélioration n'est livré à l'intérieur.

Les marqueurs **Socket_01**, **Socket_02** et **Socket_03** sont des objets vides,
enfants directs de **Tool_Root**, présents seulement lorsque l'emplacement existe.
Leur origine est au centre du fond utile et leur axe local **+Z sort de la
cupule**. Ils ont une échelle unitaire et ne sont pas animés.

Le manifeste décrit chaque marqueur, ses positions et rotations dans les
conventions Blender et glTF, le diamètre intérieur et la profondeur utile,
en mètres. Pour attacher un cristal, placer sa base au marqueur et orienter
sa longueur selon le +Z local. L'orientation des marqueurs est normalisée dans
chaque format pour conserver ce contrat malgré le changement de verticale.
Le diamètre intérieur nominal est de **46,5 mm**, avec un fond légèrement
resserré à **42 mm** et une profondeur utile de **24,75 mm**. La base du cristal
doit tenir dans le fond ; le profil peut s'élargir en remontant dans le réceptacle.

Les variantes à un ou deux sockets activent les premiers emplacements de
la configuration à trois. Leurs positions restent stables. Les signes de mana
qui croiseraient une cupule sont retirés sur cette face. Les effets apportés
par les futurs cristaux restent à définir et à implémenter dans le gameplay
autoritaire ; cette livraison fournit les modèles et leurs points d'attache.

## Prise en main et animation

Chaque GLB possède une racine **Tool_Root**, placée au centre de la poignée.
Ce pivot est destiné à un futur point d'attache de la main ; il reste immobile
pendant l'animation. Dans Blender, le manche suit l'axe Z et la tête occupe
le plan XZ ; l'export glTF convertit la verticale en Y.

La boucle **SummonedToolIdle**, échantillonnée à 24 images par seconde,
occupe les images 1 à 97. Elle décrit la suspension des fragments une fois
l'outil invoqué. L'apparition, la disparition et les gestes de récolte ne font
pas partie de cette première proposition.

Les textures sont incorporées aux sources Blender et aux GLB. Les caméras,
éclairages et sols de présentation restent réservés aux sources Blender.
Ces fichiers sont conservés hors du client Unity ; l'attache au personnage,
les collisions, les LOD et l'adaptation des matériaux URP restent à intégrer.

## Reproduction et contrôle

Le [générateur](scripts/generate_tools.py) produit uniquement cette nouvelle
famille. Le [validateur](scripts/validate_tools.py) contrôle les fichiers GLB,
les textures incorporées, les sources Blender, la racine de prise et la
continuité de la boucle. Il contrôle les 36 combinaisons, les marqueurs et
l'accès au fond des cupules, puis compare la géométrie entre couleurs.
Il vérifie aussi les empreintes de l'arbre et des
mares de mana approuvés. Le résultat est conservé dans [validation.json](validation.json).

Les **36 exports**, leurs **54 emplacements vides cumulés** et leurs boucles
passent les contrôles détaillés dans le rapport. Le contrôle compare les douze
groupes outil/nombre de sockets entre couleurs pour la géométrie, les points
d'attache et l'animation. Il vérifie la conservation des 80 fichiers de référence,
des six fichiers des outils initiaux et des 86 fichiers de modèles et textures
antérieurs à la faucille.

Les résultats des contrôles du viewer, des choix de variante et des liens
sont conservés dans [viewer-verification.json](viewer-verification.json).
Les douze faucilles ont été chargées dans le viewer, avec vérification des
liens Blender/GLB pour chaque choix. [Aperçu de la faucille verte](viewer-faucille-verte.jpg).

Avec Blender 4.5 LTS, depuis ce dossier :

```text
blender --background --factory-startup --disable-autoexec --python scripts/generate_tools.py -- --render --comparison
blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python scripts/validate_tools.py
```

L'option `--gpu` du générateur active OptiX lorsqu'il est disponible.
`--shape hache --material orange --socket-count 2` cible une variante.
`--shape faucille --render` génère les douze déclinaisons de la faucille.
Les fichiers complets sont réutilisés ; `--force` permet de reconstruire une
nouvelle variante. Les deux modèles bleus d'origine restent protégés.
Les sources validées des autres familles ne sont pas régénérées.
