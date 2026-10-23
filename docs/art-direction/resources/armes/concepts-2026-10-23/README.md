# Armes — concepts du 23 octobre 2026

**21 propositions 2D : trois variantes pour chacune des sept catégories demandées.**
Statut artistique : **direction approuvée par l'utilisateur** (« Nickel », 23 octobre 2026).
Ces planches définissent l'intention visuelle. Les [objets 3D et leur import Unity](../README.md)
prolongent cette première livraison ; leur adaptation en volume reste à examiner.

## Direction artistique

Les concepts reprennent la [référence personnage du 20 octobre](../../references/personnages-stylises-2026-10-20/README.md)
pour les volumes sculptés, les biseaux épais, les équipements et les matières.
La [référence arbre V3](../../references/arbre-peint-v3/README.md) guide la peinture et
le modelé du bois : grandes variations qui suivent les formes, creux colorés
et arêtes éclaircies avec mesure.

Les variantes se lisent de gauche à droite :
- **A** : acier bleuté, bois et cuir brun, garnitures chaudes ; le bâton A porte une pierre ambrée.
- **B** : courbes organiques, bois sculpté, jade, bronze et touches ivoire.
- **C** : cristal turquoise, métal et or chaud, lumière contenue.

Ces lettres décrivent des propositions visuelles, sans introduire de faction,
de rareté ou de statistique de gameplay. Les planches partagent un fond neutre
et une présentation comparable ; elles ne donnent pas une échelle physique commune.

## Planches

| Catégorie | Identifiants | Image |
|---|---|---|
| Épées à une main | E1-A, E1-B, E1-C | [PNG](01-epees-une-main.png) |
| Épées à deux mains | E2-A, E2-B, E2-C | [PNG](02-epees-deux-mains.png) |
| Haches à une main | H1-A, H1-B, H1-C | [PNG](03-haches-une-main.png) |
| Haches à deux mains | H2-A, H2-B, H2-C | [PNG](04-haches-deux-mains.png) |
| Boucliers | B-A, B-B, B-C | [PNG](05-boucliers.png) |
| Bâtons | BT-A, BT-B, BT-C | [PNG](06-batons.png) |
| Arcs | AR-A, AR-B, AR-C | [PNG](07-arcs.png) |

### Épées à une main

![Épées à une main — trois variantes](01-epees-une-main.png)

### Épées à deux mains

![Épées à deux mains — trois variantes](02-epees-deux-mains.png)

### Haches à une main

![Haches à une main — trois variantes](03-haches-une-main.png)

### Haches à deux mains

![Haches à deux mains — trois variantes](04-haches-deux-mains.png)

### Boucliers

![Boucliers — trois variantes](05-boucliers.png)

### Bâtons

![Bâtons — trois variantes](06-batons.png)

### Arcs

![Arcs — trois variantes](07-arcs.png)

## Provenance et contrôle

Génération avec l'outil intégré **imagegen**, en sept appels, un par planche.
Les références locales ont été examinées pour formuler les prompts ; les appels
ont créé de nouvelles images sans joindre les références comme images d'entrée.
Les [prompts exacts](prompts.json) sont conservés avec la demande d'origine.

Chaque PNG conserve la sortie originale de l'outil, sans retouche.
Le [manifeste](manifest.json) indique ses dimensions et son empreinte SHA-256.
Contrôle visuel : trois objets complets par planche, catégories et codes lisibles,
prises distinctes pour les armes à une/deux mains, trois cordes d'arc visibles,
cohérence des matériaux et des accents turquoise.
Les épées E2-B et E2-C restent proches dans leur silhouette générale ; leur garde,
leur largeur et leur traitement de lame les différencient.

La réalisation 3D possède désormais ses volumes arrière, dimensions et repères
de prise ; voir la [collection Unity](../README.md). Les modèles sont statiques.
La validation artistique de cette adaptation en volume appartient à l'utilisateur.
