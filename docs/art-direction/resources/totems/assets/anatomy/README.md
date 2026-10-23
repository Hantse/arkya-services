# Sources anatomiques des familiers

Ces sources préparent le loup V10 et le renard V2 avant finition et animation.
Le loup adapte « 3d wolf » par NewDLC sous CC0 1.0 ; voir
[sa provenance](../../sources/newdlc/README.md). Le renard conserve
« Fox » par Bake3D sous CC BY 4.0 ; voir [sa provenance](../../sources/bake3d/README.md).

La cible V7, conservée en V10, est le **loup blanc debout en bas à droite** de la première
[référence utilisateur](../../references/README.md). La tête est basse et avancée,
la nuque rejoint le garrot, les yeux sont petits et les oreilles triangulaires.
En V7, le crâne et le museau repartent de la base V4 avec un affinement progressif unique ; les retouches
cumulées du museau V5/V6 ne sont pas reprises. La carrure V6 sert de base au
nouveau raccord à cette tête. La V8 finit localement le front, la pommette
supérieure et la commissure, avec une transition douce vers la gorge.
La V9 affine ensuite le raccord du chanfrein et les flancs du museau, sans rallonge,
puis recule et aplanit les joues antérieures. La truffe reste volumique.
La V10 renforce ensuite la cage thoracique, le poitrail, le collet et le haut des
cuisses. Elle récupère la base du museau et le soutien mandibulaire V8, garde
les joues reculées V9 et relève légèrement toute la terminaison du museau.
La truffe devient plus transversale ; la bouche légèrement entrouverte laisse voir les petites dents du modèle source. Les yeux, oreilles,
appuis et os sont conservés.
Les surfaces restent lissées et peintes.

- `wolf.blend` : anatomie V10, hauteur neutre 0,866 m, 29 508 triangles de pelage.
- `fox.blend` : anatomie V2, hauteur neutre 0,650 m, 28 800 triangles de pelage.
- `*-rig.json` : os, dimensions, repères anatomiques et contacts plantaires.

Les objets `Armature` et `TotemFur` ont des transformations identitaires : mètres,
avant +X, haut +Z. Chaque squelette compte 29 os avant les marqueurs de contact.
Les poids sont normalisés avec quatre influences maximum. Les UV et textures
sources incorporées sont conservés (1 024 px loup, 2 048 px renard) ; les pelages
finis sont produits séparément en 2 048 px.

Le loup utilise trois attributs de points : `SourcePosition` décrit la géométrie
de repos finale en mètres ; `PaintPosition` conserve les coordonnées de peinture
V6 pour que les pigments suivent la nouvelle tête ; `EyeMask` isole les yeux.
Son JSON porte `artRevision: 10` et `coatProfile: wolf-v10`.

La reconstruction V7 modifiait les os `Neck`, `Head`, `Ear.L` et `Ear.R`.
La V10 conserve le squelette, les pieds et le bas des membres, la queue, les yeux,
les oreilles, les UV et les poids de V9. Le corps est volontairement étoffé.
`headRefinement` compare V6/V10 ; `surfaceRefinement` compare V7/V8 ;
`muzzleRefinement` décrit V8/V9 ; `bodyRefinement` décrit V9/V10.
Le [contrôle source V10](../../wolf-v10-source-verification.json) compare les
régions protégées, attributs et poids à V9 et ne détecte aucun triangle retourné
ou dégénéré. Le [contrôle de conservation](../../wolf-v10-preservation.json)
vérifie aussi les fichiers tiers et du renard. Les fichiers du renard et les originaux NewDLC restent inchangés.

Le champ `locomotionConfig.limbs` alimente `scripts/totem_locomotion.py` ;
`sole_rest` est le centre mesuré de chaque surface plantaire. Les animations,
palettes et effets magiques sont assemblés par `scripts/build_totems_v2.py`.

Depuis la racine du dépôt, reconstruire uniquement le loup :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python-exit-code 1 --python docs/art-direction/resources/totems/scripts/build_anatomy.py -- wolf
```

`build_anatomy.py` délègue au générateur `build_wolf_anatomy.py`, V10 par défaut.
L'appel direct sans `--output-dir` produit `assets/anatomy/draft-wolf-v10/`.
Les options `--revision 4`, `--revision 5`, `--revision 6`, `--revision 7`, `--revision 8` et `--revision 9` reproduisent les
étapes historiques dans leurs propres brouillons. Seul un `--output-dir`
explicite vers `assets/anatomy` remplace les sources de production lors d'un appel direct.

Révision livrée : `20261016T150222819563Z-totems`. Les rapports de peau à 120 Hz, d'exports et
d'import Unity sont décrits dans le [guide de la collection](../../README.md).
Ils ne constituent pas une approbation artistique.
