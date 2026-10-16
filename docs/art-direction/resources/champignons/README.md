# Champignons — collection DA

**Statut : référence DA validée par l'utilisateur le 15 octobre 2026.**
L'approbation couvre les **12 variantes** : quatre formes/tailles et trois
matières. Cette collection rejoint les références approuvées pour la végétation,
avec les arbres, buissons, herbes et fleurs, dans la même direction artistique peinte.

## Silhouettes

| Forme | Hauteur | Triangles | Composition |
|---|---:|---:|---|
| Colonie de boutons | 0,18 m | 28 000 | Cinq chapeaux dodus, tailles et inclinaisons décalées, arc ouvert |
| Amanite bombée | 0,40 m | 11 968 | Un grand chapeau charnu incliné et un jeune compagnon |
| Chanterelles en éventail | 0,60 m | 16 800 | Trois coupes ondulées, pieds courbés et lamelles en relief |
| Parasol ancien | 0,95 m | 18 336 | Chapeau large asymétrique à bosse centrale, deux jeunes pousses |

Les silhouettes ont des compositions distinctes. Les chapeaux sont des volumes
fermés avec un rebord roulé et adouci, une sous-face modelée et des lamelles en
relief. Les pieds s'incurvent et s'évasent au sol. Amanite et grand parasol
possèdent une collerette plissée. Aucun socle, aucune carte transparente.

## Matières

- **Corail crème** : rouge corail chaleureux, quelques marques crème irrégulières,
  pieds ivoire et ombres terre rose.
- **Miel des sous-bois** : brun noisette et ocre doré, marques atténuées,
  dessous et pieds crème.
- **Mystique boréal** : violet, ombres pétrole, rebord et marques turquoise à
  émission légère. Le modelé et la couleur portent la matière.

Les marques ont une distribution irrégulière et une orientation différente sur
chaque chapeau. Les dégradés et grandes variations peintes sont privilégiés ;
aucun grain photographique ni microbruit ne remplace les volumes.

## Fichiers et usage

Chaque forme existe dans les trois matières : **12 GLB + 12 fichiers Blender**,
avec un aperçu de 1 100 × 1 100 pixels. Le [manifeste](manifest.json) donne les
chemins, dimensions et nombres de triangles. La
[comparaison des tailles](comparaison-tailles.png) utilise une échelle commune.

Les fichiers sont en mètres, avec un pivot centré en XY au niveau du sol.
Chaque asset comporte un maillage et un matériau opaque. Les images de couleur,
rugosité et émission sont peintes procéduralement dans un atlas de **2 048 ×
2 048 pixels**, partagé par les quatre formes d'une même palette. Les images
sont incorporées au GLB et empaquetées dans le Blender. Les maillages, UV,
matériaux et sources d'atlas restent éditables.

Le budget maximum est de 30 000 triangles par modèle. Les variantes de matière
d'une même forme conservent exactement sa géométrie. La modélisation, les atlas
et les compositions sont originaux ; aucun modèle externe n'est utilisé.

## Reproduction et vérification

- [Générateur Blender](scripts/generate_mushrooms.py) :
  `--shape all --material all --render` ; ajouter `--gpu` uniquement si le GPU
  est disponible. Aperçu rapide : `--shape amanite --material corail --render --draft`.
- [Comparaison](scripts/render_comparison.py) : les quatre modèles à taille réelle.
- [Contrôles](scripts/validate_mushrooms.py) : fichiers, textures incorporées,
  surfaces fermées, faces valides, UV, dimensions, pivots et géométrie identique
  entre palettes. Résultats : [validation.json](validation.json).

Le contrôle technique a réussi pour les 12 modèles. Les UV sont également
identiques entre palettes. Les sous-faces et leurs lamelles ont été examinées
dans [une vue en contre-plongée](controle-sous-face.png), sans sol de studio
pour permettre leur inspection.

Ces ressources sont conservées dans la bibliothèque DA, hors du client Unity.
Les colliders, LOD et l'intégration au moteur restent une étape distincte.
