# Fleurs — Arkya

**Référence DA validée par l'utilisateur le 15 octobre 2026.**
L'approbation couvre les **12 variantes** : quatre formes/tailles et trois
matières, dans la continuité des références de végétation déjà validées :
[arbre V3](../references/arbre-peint-v3/README.md),
[buissons](../buissons/README.md) et [touffes d’herbe](../herbes/README.md).
La collection rejoint les références approuvées pour les prochains objets de végétation.

| Forme | Hauteur | Composition |
|---|---:|---|
| Rosette basse | 0,20 m | Fleur en coupe, bouton latéral et feuilles basses |
| Bouquet de prairie | 0,45 m | Quatre corolles de tailles et orientations différentes, un bouton |
| Hampe de clochettes | 0,70 m | Tige arquée, quatre cloches pendantes ouvertes et deux boutons terminaux |
| Grand lis étoilé | 1,00 m | Grande corolle à six pétales recourbés, étamines et deux fleurs satellites |

Chaque forme existe en **corail abricot**, **ivoire doré** et **mystique** :
**12 modèles**, avec la même géométrie et les mêmes UV dans les trois matières.
Les feuilles passent du vert sylvestre au vert pétrole pour la variante mystique.
Les cœurs abricot ou miel se distinguent des pétales ; la lueur mystique reste
limitée au cœur et à une petite partie des pointes.

Les corolles, pétales, feuilles, boutons et tiges sont de vrais volumes fermés.
Les pétales présentent une coupe, une épaisseur, des bords relevés et des
inflexions asymétriques ; les clochettes sont des entonnoirs ouverts à lèvre
festonnée. Aucun plan transparent, aucune carte croisée, aucun pot ni socle.
Les fleurs ne sont pas des copies simplement redimensionnées : leurs ports,
ramifications et structures de floraison changent.

## Surfaces et fichiers

Un atlas **2048 × 2048 partagé par matière** contient les zones de pétales,
feuillage, tiges, boutons et cœurs. Les cartes de couleur, rugosité et émission
sont produites par le générateur Blender/numpy, puis incorporées aux `.glb`
et aux `.blend`. Les glacis larges, la gorge colorée des pétales et les nervures
discrètes donnent une surface peinte sans bruit fin. Toutes les surfaces sont
opaques ; un seul maillage et un seul matériau par modèle.

- [Manifeste](manifest.json) : variantes, chemins, dimensions et triangles.
- [Comparaison à la même échelle](comparaison-tailles.png) et [source Blender](comparaison-tailles.blend).
- [Générateur de modèles et textures](scripts/generate_flowers.py).
- [Générateur de la comparaison](scripts/render_comparison.py).
- [Contrôle des exports](scripts/validate_flowers.py) et [rapport](validation.json).

Chaque dossier `assets/<forme>-<matière>/` contient le GLB, la source Blender,
l’aperçu PNG 1100² et une notice JSON. Unités en mètres, pivot centré au sol ;
Z vertical dans Blender et Y vertical dans GLB. La collection `Arkya_Flowers`
contient uniquement le modèle ; le sol, les lumières et la caméra du studio
ne font pas partie du GLB.

## Reproduire

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python scripts/generate_flowers.py -- --shape all --material all --render
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python scripts/render_comparison.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python scripts/validate_flowers.py
```

Génération déterministe ; `--repaint` recrée les atlas et `--gpu` active OptiX
pour les aperçus seulement. Aperçus finaux : Cycles, 32 échantillons avec
débruitage, 1100² ; planche : 2200 × 1100. Le mode `--draft` sert uniquement
au contrôle rapide d’une proposition.

Ces ressources DA restent hors du client Unity. L’intégration, les LOD,
les collisions et l’animation du vent sont des étapes distinctes.

## Contrôles effectués

Les **12 GLB et 12 sources Blender** ont passé les vérifications : données
finies, UV présents, surfaces opaques, images 2048² incorporées, pivots au sol,
dimensions conformes et aucune dépendance externe. Les composants sont
fermés, sans arête ouverte ni triangle dégénéré. Les empreintes de géométrie
et d’UV sont identiques entre les trois matières d’une forme.

| Forme | Triangles |
|---|---:|
| Rosette basse | 6 784 |
| Bouquet de prairie | 20 968 |
| Hampe de clochettes | 13 670 |
| Grand lis étoilé | 17 548 |

Les douze aperçus 1100² ont été produits ; les quatre silhouettes figurent
à la même échelle dans la planche comparative.
