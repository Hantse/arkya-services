# Souches — surfaces peintes et volumes sculptés

**Référence DA validée par l'utilisateur le 15 octobre 2026.**
L'approbation couvre les **12 variantes** : quatre formes/tailles et trois
matières, dans la continuité de la
[référence arbre V3 validée](../references/arbre-peint-v3/README.md).
La collection rejoint les références approuvées pour le bois et les objets d'environnement.

## Formes et matières

| Forme | Hauteur | Construction |
|---|---:|---|
| Souche basse | 0,35 m | Large coupe, six contreforts bas et racines intégrées au tronc. |
| Souche tordue | 0,65 m | Tronc courbé, coupe oblique et départ de branche latéral. |
| Souche creuse | 0,95 m | Ouverture réelle, paroi épaisse et cavité profonde avec fond fermé. |
| Vieille souche brisée | 1,35 m | Pans de bois irréguliers, grande entaille et moignon secondaire. |

Chaque forme existe en trois matières : **ancien moussu**, **mystique** et
**bois brûlé**, soit **12 modèles**. Les géométries et les UV sont identiques
entre les trois variantes d'une même forme.

Le traitement reprend les rubans d'écorce de l'arbre V3 : creux bruns colorés,
fibres larges et nappes de mousse localisées. Les sections utilisent une
peinture dédiée : cernes décentrés, contours légèrement irréguliers et
fissures effilées. Les accents cyan et les braises suivent quelques creux
sans masquer l'écorce. Les racines appartiennent à la surface du tronc;
les moignons secondaires sont des volumes fermés qui pénètrent le tronc.

![Comparaison à la même échelle](comparaison-tailles.png)

## Livrables

- [Catalogue détaillé](manifest.json), avec dimensions réelles et triangles.
- [Comparaison Blender](comparaison-tailles.blend).
- Dossier `assets/` : GLB autonome, Blender éditable, aperçu 1100 × 1100 et
  notice JSON par variante.
- Dossier `textures/` : cartes **2048 × 2048** de couleur, rugosité et émission
  pour l'écorce et le bois de coupe, incorporées dans chaque GLB et Blender.
- [Générateur déterministe](scripts/generate_stumps.py),
  [générateur de comparaison](scripts/render_comparison.py) et
  [contrôles](scripts/validate_stumps.py).

Origine centrée au sol, échelle métrique, matériaux opaques et deux matériaux
par modèle. Budget inférieur à **35 000 triangles** par variante. Les
surfaces creuses ont une vraie épaisseur; aucune transparence ni carte plane.
Le sol, les lumières et la caméra du studio restent exclus des exports GLB.

## Provenance

L'écorce dérive de la [source peinte V3](../references/arbre-peint-v3/source/painted-bark.png)
et de son [prompt conservé](../references/arbre-peint-v3/source/painted-bark-provenance.md).
Cette source mesure 1254 × 1254 pixels; elle est rééchantillonnée pour les
cartes 2K et recolorée avec les palettes V3. Les sections de bois sont
peintes par le générateur avec des motifs conçus pour cette collection.
La source et les modèles validés sont utilisés en lecture seule.

## Vérification

Le [rapport de validation](validation.json) contrôle les 12 GLB et Blender :
surfaces fermées, faces non dégénérées, UV, images incorporées, coordonnées
finies, dimensions, origine au sol, opacité et identité géométrique entre
matières. Les aperçus et la comparaison servent au contrôle visuel.

Ces fichiers sont des ressources de direction artistique, conservées hors
du client Unity. LOD, colliders et intégration dans le jeu ne sont pas inclus.
