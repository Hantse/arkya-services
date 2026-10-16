# Buissons — référence DA validée

Collection créée le 15 octobre 2026 dans le langage visuel de l’arbre V3
approuvé : feuillage sculpté, surfaces peintes, rameaux et ouvertures lisibles.
**Validé par l'utilisateur le 15 octobre 2026** pour les quatre formes/tailles
et les trois matières (12 variantes). Cette collection rejoint les références
DA approuvées pour les prochains objets de végétation.

| Forme | Hauteur | Triangles | Construction |
|---|---:|---:|---|
| Coussin bas | 0,45 m | 21 220 | Nappes basses, ramifications rampantes |
| Buisson rond | 0,85 m | 29 708 | Masse arrondie asymétrique, petits bouquets superposés |
| Buisson étalé | 1,25 m | 36 074 | Large éventail et bras latéraux décentrés |
| Buisson dressé | 1,80 m | 36 904 | Plusieurs tiges, feuillage étagé et sommet décalé |

Chaque forme possède trois textures : **vert sylvestre**, **automnal**
(ambre et roux), **mystique** (prune et turquoise contenus). Les géométries
et UV sont identiques entre les trois matières d’une même forme.

Les 12 `.glb` sont autonomes, avec textures intégrées. Les 12 `.blend`
conservent le maillage éditable et les textures incorporées. Chaque feuille
est une forme fermée incurvée avec épaisseur, pli central et bord véritable ;
aucune sphère ni carte transparente ne remplace le feuillage. Les palettes
utilisent des atlas couleur et rugosité 2K, plus une écorce peinte 512 px.
Les atlas sont partagés entre les quatre formes d’une même palette.

Le pivot est centré en XY au sol, unités en mètres ; Blender utilise Z vertical,
les GLB Y vertical. Deux matériaux opaques par modèle. Les fichiers de studio
sont conservés dans les sources Blender mais exclus des exports GLB.

- [Manifest des 12 variantes](manifest.json)
- [Comparaison des quatre tailles](comparaison-tailles.png)
- [Validation technique](validation.json)
- [Générateur des modèles et textures](scripts/generate_bushes.py)
- [Générateur de comparaison](scripts/render_comparison.py)
- [Contrôle des exports](scripts/validate_bushes.py)

Les géométries et atlas peints sont créés procéduralement dans Blender pour
Arkya. La référence visuelle est [l’arbre V3](../references/arbre-peint-v3/README.md).
Aucun modèle externe n’a été incorporé. La silhouette, les paires de feuilles
et les étages du feuillage sont construits par rameaux ; les dégradés et
nervures sont peints dans les atlas, sans bruit photographique.

Contrôles réussis sur les 12 variantes : ouverture des sources Blender,
textures incorporées dans les deux formats, pivots au sol, dimensions,
UV finis et géométrie identique entre les trois textures de chaque forme.
Les aperçus ont été rendus en 1 100 × 1 100 pixels, Cycles 40 échantillons.
La comparaison utilise les dimensions réelles communes, sans redimensionner
chaque objet indépendamment.

Régénération depuis Blender en mode arrière-plan :
`--python scripts/generate_bushes.py -- --shape all --material all --render --gpu`.
Omettre `--gpu` pour utiliser le processeur. `--repaint` reconstruit les atlas.
Cette livraison est une ressource DA ; colliders, LOD et intégration Unity
restent hors du périmètre.
