# Pierre et végétation des remparts — 20 octobre 2026

Cette passe répond au rythme trop régulier de la maçonnerie et aux bouquets
répétitifs du lierre. Elle conserve la direction peinte d'Arkya : variations
de volume et de couleur lisibles, pierre mate et verts assourdis.

La pierre reçoit des blocs de proportions différentes et des joints moins
uniformes. Les petites plaquettes répétées sont remplacées par des accents
plus rares et moins saillants. La végétation part de tiges continues et
ramifiées, avec des feuilles plus fines, des jeunes pousses et des espaces
entre les masses.

Les volumes structurels raccordés en onglet, les collisions, les portes et
les escaliers constituent la référence à préserver. La scène avant retouche
est conservée dans
[`artifacts/wall-natural-variation-2026-10-20/StarterIsland-before.unity`](../../../../../artifacts/wall-natural-variation-2026-10-20/StarterIsland-before.unity).

## Réalisation

- **Pierre** : une nappe de 16 × 8 m contient 126 blocs, cinq longueurs
  (0,5 à 2,5 m) et trois hauteurs (0,4 à 1,2 m). Les nuances de calcaire,
  grès et gris doux, les touches peintes et les bords usés remplacent le
  motif de petites assises. Le relief de matière reste mat et discret.
- **Rythme des façades** : suppression de 936 faux joints géométriques et
  de 233 plaquettes répétées. Les 43 accents conservés sont irréguliers et
  peu saillants, avec le même pigment que le mur. Les masses structurelles,
  les angles en onglet et leurs appuis restent intacts.
- **Végétation** : 50 colonies, 3 887 feuilles fines et ramification
  continue depuis le sol. Vingt-cinq colonies reçoivent des masses
  latérales plus fournies ; les autres laissent davantage voir la pierre.
  La mousse des anciens lots reste conservée. Les nouvelles feuilles
  utilisent le shader de vent existant, avec les tiges et pétioles ancrés.

Les [données de maçonnerie](masonry-authoring.json),
la [finition peinte finale](masonry-painted-finish.json) et les
[données du lierre](climbers-authoring.json) détaillent les ressources et
les changements.

## Captures

Les neuf vues `before` et `after` emploient les mêmes caméras et la même
heure de jour. La vue ensoleillée supplémentaire est une vue après seule,
à une heure différente ; elle ne constitue pas une comparaison avant/après.

| Sujet | Avant | Après |
| --- | --- | --- |
| Pierre et angle 46 | [Vue](before/46-outside-day.png) | [Vue](after/46-outside-day.png) |
| Angle 74 | [Vue](before/74-outside-day.png) | [Vue](after/74-outside-day.png) |
| Angle 93 | [Vue](before/93-outside-day.png) | [Vue](after/93-outside-day.png) |
| Lierre 59 | [Vue](before/ivy-059-close-day.png) | [Vue](after/ivy-059-close-day.png) |
| Lierre 95 | [Vue](before/ivy-095-close-day.png) | [Vue](after/ivy-095-close-day.png) |
| Lierre 94 | [Vue](before/ivy-094-close-day.png) | [Vue](after/ivy-094-close-day.png) |
| Mur intérieur | [Vue](before/courtyard-wall-day.png) | [Vue](after/courtyard-wall-day.png) |
| Chemin de ronde | [Vue](before/wall-walk-junction-day.png) | [Vue](after/wall-walk-junction-day.png) |
| Enceinte | [Vue](before/whole-rampart-day.png) | [Vue](after/whole-rampart-day.png) |

[Détail supplémentaire au soleil](after-sunlit/ivy-059-close-day.png).

## Vérifications et coût

Les [19 contrôles de préservation](validation.json) passent : les 2 497
colliders de la scène, les 26 lumières, les hauteurs et la peinture du
Terrain, les références hors retouches déclarées et les 427 ressources
sources contrôlées restent inchangés. Aucun script manquant ni matériau
invalide n'est détecté.

Le [test GPU du vent](climbers-wind-validation.json) rend le vrai maillage
et son matériau à deux instants. Il compare aussi un vent nul et les seuls
triangles ancrés. Les contrôles de vent sont distincts des captures de
présentation : 890 pixels de feuilles changent avec le vent, contre zéro
avec le vent nul et zéro sur les 3 912 pixels visibles des tiges ancrées.

Le [rafraîchissement des buffers de rendu](climbers-render-refresh.json)
a remis à jour l'affichage des maillages modifiés sans changer leurs
données ni leurs références. Les captures finales et le test du vent
ont été effectués après ce rafraîchissement.

La [sauvegarde finale](../../../../../artifacts/wall-natural-variation-2026-10-20/final-save-command.json)
confirme la scène sauvegardée, sans modification en attente, et les
35 nouveaux assets accompagnés de leur `.meta`. Le
[contrôle final de la console](../../../../../artifacts/wall-natural-variation-2026-10-20/final-console-command.json)
ne relève aucune erreur ni aucun avertissement après archivage des
diagnostics ci-dessous ; la compilation est terminée sans échec.

Les plantes utilisent six groupes avec deux LOD et un matériau :
78 536 triangles proches et 34 134 lointains. L'ensemble actif de
l'enceinte au LOD le plus détaillé passe de 221 211 à 195 450 triangles,
soit **25 761 de moins**. Ce comptage ne constitue pas une mesure de FPS.
Aucun collider, lumière ou comportement runtime n'est ajouté.

Un appel de finition a dépassé le délai de l'outil pendant une longue
opération de l'éditeur. Unity a retrouvé la main sans redémarrage ; la
scène a ensuite été sauvegardée et les contrôles exécutés sur les données
effectives. Les deux erreurs de délai de l'outil sont conservées dans le
[journal de diagnostic](../../../../../artifacts/wall-natural-variation-2026-10-20/console-diagnostic-command.json).
L'étape facultative de regroupement de l'historique Undo a été retirée des
commandes de finition. La cause native exacte du délai n'est pas établie.

Les minces marques de jonction de certaines travées, déjà visibles avant,
restent une limite à très courte distance. Aucun nouveau parcours réseau
ni build distribué n'est revendiqué : les collisions sont comparées
intégralement à la référence, et la modification porte sur la présentation.

## Fichiers et édition

- [StarterIsland.unity](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity) : affectations de pierre et nouveau groupe de plantes.
- [NaturalMasonry](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Town/RampartUpgrade/NaturalMasonry) et [NaturalClimbers](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Town/RampartUpgrade/NaturalClimbers) : dérivés natifs avec `.meta`.
- [RefineWallMasonry.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineWallMasonry.cs) et [RefineWallClimbers.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineWallClimbers.cs) : commandes d'authoring ciblées, hors `Assets`.
- [ReviewWallNaturalVariation.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewWallNaturalVariation.cs) : captures et contrôle des invariants.

Conserver les ressources d'origine et ne pas relancer les anciens
générateurs complets des remparts sur cette scène retouchée.
