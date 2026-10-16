# Sources anatomiques des familiers

Ces sources remplacent l'anatomie procédurale rejetée. Elles dérivent de
« Fox » par Bake3D sous CC BY 4.0 ; voir [la provenance](../../sources/bake3d/README.md).

- `fox.blend` : silhouette du renard source, taille de 0,65 m.
- `wolf.blend` : révision 3, crâne élargi et abaissé, yeux réduits, museau et
  mâchoire plus épais, oreilles courtes à bases écartées, encolure ample et
  queue fine tombante ; taille de 1,10 m. Le renard conserve sa révision 2.
- `*-rig.json` : os, segments, dimensions, repères anatomiques et contacts
  plantaires en coordonnées du squelette.

Les deux fichiers sont en mètres, avant +X, haut +Z ; `Armature` et `TotemFur`
ont des transformations objet identitaires. Chaque maillage compte 28 800
triangles. La texture 2048 px est incorporée et ses UV sont conservés.
Le squelette possède 29 os avant l'ajout des marqueurs de contact par l'animateur.

Le loup conserve aussi l'attribut de points `SourcePosition` (`FLOAT_VECTOR`) :
coordonnées de la source avant sculpture et mise à l'échelle, interpolées lors
de la réduction du maillage. La peinture du loup l'utilise pour placer la selle,
le masque et les transitions de pelage indépendamment des nouveaux volumes.
Son JSON porte `artRevision: 3`.

Les coudes, poignets, genoux, jarrets, orteils et oreilles ont été replacés
dans les volumes du maillage. Les poids des membres sont recalculés le long de
leurs segments. Le bas des pattes n'hérite pas de poids du torse ; les
influences sont limitées à quatre et normalisées après réduction du maillage.

Le champ `locomotionConfig.limbs` des JSON peut être passé directement au
module `scripts/totem_locomotion.py`. `sole_rest` est le centre de la surface
plantaire mesurée ; la très faible différence de hauteur entre les quatre
semelles est conservée pour permettre son contrôle pendant le bake.

Ces fichiers ne contiennent pas les animations finales, les palettes ou les
effets magiques. Leur assemblage est réalisé par `scripts/build_totems_v2.py`.
Régénération du loup seul, sans réécrire le renard :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python-exit-code 1 --python docs/art-direction/resources/totems/scripts/build_anatomy.py -- wolf
```

Passer `fox` pour régénérer uniquement le renard, ou `wolf fox` pour les deux.
