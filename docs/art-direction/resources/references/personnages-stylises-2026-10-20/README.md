# Référence principale — personnages stylisés

Le **20 octobre 2026**, l’utilisateur a fixé ces six images comme cible pour
les personnages d’Arkya : « Niveau personnage, y faut s'inspirer (mais genre
a 100%) de ça ».

L’alignement demandé porte sur **l’ensemble du langage visuel et le niveau de
finition** : proportions, silhouette, anatomie, visage, cheveux, construction
du costume, matières et rendu. Cette référence prime pour les personnages sur
les anciennes explorations et les modèles Humain/Elfe actuellement intégrés.
La référence arbre V3 continue de guider les objets d’environnement.

![Référence couleur — silhouette, équipement et matières](06-poses-couleur.png)

## Images conservées

Les six PNG de 1280 × 720 sont des copies exactes des pièces jointes, sans
recadrage ni retouche. Le [manifeste](reference.json) conserve leur ordre,
leur nom d’origine et leur empreinte SHA-256. L’auteur et le lien d’origine
n’étaient pas indiqués dans le message.

| Image | Vue | Ce qu’elle permet de comparer |
|---|---|---|
| [01 — Portrait couleur](01-portrait-couleur.png) | Visage en action | Yeux, expression, peau, mèches, bords d’armure et reflets |
| [02 — Action couleur](02-action-couleur.png) | Profil et équipement | Ligne d’action, épaisseurs, couches du costume et lames |
| [03 — Vues sculpture](03-vues-sculpture.png) | Face, trois-quarts, dos, profil | Proportions, anatomie, silhouette et construction à 360° |
| [04 — Volumes sculpture](04-volumes-sculpture.png) | Trois-quarts, dos, profil | Chevelure, superpositions, attaches et continuité des formes |
| [05 — Poses sculpture](05-poses-sculpture.png) | Quatre poses avec armes | Appuis, équilibre, gestuelle et lecture sans couleur |
| [06 — Poses couleur](06-poses-couleur.png) | Quatre poses avec armes | Palette, hiérarchie des valeurs et séparation des matières |

## Critères à reprendre

| Aspect | Cible pour les prochains personnages |
|---|---|
| Proportions | Anatomie adulte stylisée, élancée et athlétique ; membres longs, articulations construites et équilibre crédible. Adapter ce langage aux différentes silhouettes et races. |
| Silhouette | Grandes masses distinctes, courbes affirmées, asymétrie contrôlée et accessoires généreux. Le personnage doit rester identifiable en petite vue et de dos. |
| Modelé | Surfaces continues, transitions souples et plans choisis avec précision. Épaules, coudes, genoux, mains et pieds doivent garder une forme convaincante en volume. |
| Visage | Yeux expressifs avec paupières modelées, sourcils affirmés, nez et lèvres sculptés, joues et mâchoire nuancées. L’expression doit tenir en gris et en gros plan. |
| Cheveux | Grandes mèches pleines et sculptées, direction claire des sillons, alternance de masses et de creux ; dégradés peints qui accompagnent les mèches. Le volume compte autant que la couleur. |
| Costume | Plaques et bordures épaisses, biseaux soignés, attaches lisibles, superposition réelle des couches. Les plis du tissu suivent le drapé et les points de tension. |
| Matières | Peau douce, tissu mat, cuir distinct, métal aux reflets francs et surfaces colorées satinées. Les variations peintes renforcent les volumes et les creux. |
| Couleur | Grandes zones cohérentes, contraste chaud/froid et accents concentrés. La palette observée associe jade/turquoise, or chaud, ivoire, cuir brun et cheveux magenta. |
| Magie | Lames et cristaux lumineux avec contour, épaisseur et variations internes lisibles. Leur lumière doit conserver la lecture du visage et du costume. |
| Pose | Ligne d’action claire, épaules et bassin engagés, mains en prise sur les armes, appuis crédibles. L’équipement et la chevelure participent à la gestuelle. |

La chevelure magenta segmentée, la couronne à cristaux, les doubles lames,
les sandales lacées et l’ensemble jade/or/ivoire constituent le design précis
du personnage illustré. Ils servent de référence directe de construction
et de finition ; les autres coiffures, équipements et palettes d’Arkya doivent
atteindre la même cohérence avec leurs propres silhouettes.

![Référence grise — qualité du modelé à 360°](03-vues-sculpture.png)

## Comparaison des prochaines passes

1. Présenter une vue entière de face, trois-quarts, profil et dos, à cadrage
   comparable aux planches.
2. Examiner une version grise sous un éclairage neutre : silhouette, visage,
   anatomie, cheveux et costume doivent déjà fonctionner.
3. Comparer un portrait et des détails de cheveux, de mains et d’équipement
   aux images 01 et 02.
4. Vérifier en couleur la distinction peau/tissu/cuir/métal et la maîtrise
   des accents lumineux.
5. Contrôler une pose au repos et une pose dynamique, puis le personnage à
   la distance réelle de caméra dans Unity : expression, silhouette et
   grandes masses doivent rester lisibles.

Les images fixent une cible artistique. Elles ne donnent aucun budget de
triangles, de textures, de matériaux ou d’os ; ces budgets devront être
mesurés dans le jeu en préservant les formes qui portent cette cible.

## État des personnages existants

Le **22 octobre 2026**, l’utilisateur a retenu le package `valkyrie_2019`,
importé sous `Unity3D/Arkya/Assets/Valkyrie/`, comme base des futurs personnages.
Les images conservées ici guident les adaptations de cette base. Son
[intégration dans la création et le jeu](../../../../CHARACTER_CREATION.md#base-retenue--valkyrie_2019)
utilise désormais une variante Arkya avec matériaux URP et animations adaptées ;
les originaux du fournisseur sont conservés. Les anciennes apparences restent
compatibles avec cette base commune.

L’[atelier Humain/Elfe](../../personnages/README.md), les
[sources riggées](../../personnages/game/README.md) et les modèles de la
[création Unity](../../../../CHARACTER_CREATION.md) restent des prototypes
à comparer à cette référence. Leurs vérifications de rig, d’animation ou de
personnalisation ne constituent pas une validation de ce niveau de finition.

Cette livraison conserve la référence et les critères de travail. L’adaptation
des modèles et leur validation artistique dans Unity restent à réaliser.
