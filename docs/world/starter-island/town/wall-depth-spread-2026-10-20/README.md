# Relief des pierres, lierre étalé et escaliers compacts

Cette passe prolonge la [variation de maçonnerie et de végétation](../wall-natural-variation-2026-10-20/README.md) à la demande de l'utilisateur : pierres en relief avec des bords irréguliers, lierre qui s'étale en nappes et escaliers collés à la muraille, plus courts.

## Pierres et végétation

La pierre reçoit un habillage en volume aligné sur les 126 blocs du motif peint existant. Les contours ont des coins cassés, des bords irréguliers et des saillies différentes, bornées à 12 cm. Les raccords structurels en onglet, le chemin de ronde, les ouvertures et les collisions du mur restent intacts. Les fragments qui touchent les escaliers sont écartés.

La [finition de volume](sculpted-face-profile.json) garde des épaules proches de la face, et les [normales finales](sculpted-face-normals.json) séparent son éclairage de celui du chanfrein. Cela évite de dessiner un éventail triangulaire au centre des grandes pierres tout en gardant leurs ombres et leur silhouette en relief.

Les 4 943 fragments sont réunis en 12 cellules, avec 134 859 triangles proches et 44 953 triangles simplifiés. Le relief détaillé s'efface à distance au profit du relief simplifié, puis de la pierre peinte conservée. Il ajoute 24 renderers, un matériau et aucun collider, script runtime ou éclairage. Il ne s'agit pas d'une mesure de FPS.

Le lierre forme 12 nappes, dont les plus larges couvrent 9 à 21,5 m sur plusieurs travées. Les branches latérales et obliques relient les masses, avec des poches vides et des bords plus clairsemés. Les 5 010 feuilles occupent environ 32 % de la surface des grands pans, mesurés par projection des triangles foliaires sur une grille de 16 cm ; cette moyenne inclut les espaces de pierre laissés visibles.

La végétation utilise cinq groupes avec deux LOD et un matériau partagé : 69 744 triangles proches et 32 732 lointains. Les longues tiges traversantes de la première version de cette passe ont été remplacées par des attaches locales et des rameaux fins. Les feuilles utilisent le vent existant, avec les tiges et pétioles ancrés. La mousse d'origine est conservée.

## Escaliers

Les deux volées suivent maintenant la face native de la courtine. Leur bord côté mur pénètre la maçonnerie de 2,5 cm pour fermer le vide, tout en gardant les ouvertures existantes du chemin de ronde.

| Accès | Longueur de volée avant | Après | Réduction | Marches principales |
| --- | ---: | ---: | ---: | ---: |
| Ouest | 12,80 m | 8,39 m | 34,5 % | 26 |
| Est | 13,40 m | 8,97 m | 33,1 % | 28 |

Ces longueurs comprennent le palier intermédiaire de 85 cm, mais pas les paliers d'arrivée. Le giron vaut 29 cm et les contremarches environ 18,8 cm. L'arrivée est comporte une petite marche supplémentaire pour rejoindre le terrain en pente.

Les collisions suivent les marches horizontales et les contremarches verticales. Aucun plan incliné ni garde-corps n'est ajouté. Les volumes précédents sont conservés inactifs. Les marqueurs du parcours suivent les nouveaux paliers.

## Ressources et reproduction

- [StarterIsland.unity](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity) : scène native retouchée.
- [CompactWallStairs](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Town/RampartUpgrade/CompactWallStairs) : masses, dessus de marches et collisions.
- [SculptedMasonry](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Town/RampartUpgrade/SculptedMasonry) : habillage des pierres en volume.
- [SpreadingClimbers](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Town/RampartUpgrade/SpreadingClimbers) : végétation étalée et LOD.
- [CompactRampartStairs.cs](../../../../../Unity3D/Arkya/AgentScripts/World/CompactRampartStairs.cs), [SculptWallMasonry.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SculptWallMasonry.cs), [SpreadWallClimbers.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SpreadWallClimbers.cs) : authoring via les API de l'éditeur.
- [ReviewWallDepthSpread.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewWallDepthSpread.cs), [ValidateCompactWallStairs.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateCompactWallStairs.cs), [ValidateCompactWallCircuit.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateCompactWallCircuit.cs), [ValidateCompactStairNetworkTraversal.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateCompactStairNetworkTraversal.cs) : captures et vérifications.

Les anciens générateurs complets ne doivent pas être relancés sur la scène retouchée. Les assets d'origine sont conservés ; les nouveaux assets ont leur `.meta` généré par Unity.

Les captures `before` et `after` emploient les six mêmes caméras et la même heure de jour (phase 0,32). La référence native et les invariants compressés sont conservés dans `artifacts/wall-depth-spread-2026-10-20`.

| Sujet | Avant | Après |
| --- | --- | --- |
| Relief et angle de mur | [Vue](before/46-outside-day.png) | [Vue](after/46-outside-day.png) |
| Nappe de lierre 59 | [Vue](before/ivy-059-close-day.png) | [Vue](after/ivy-059-close-day.png) |
| Nappe de lierre 94 | [Vue](before/ivy-094-close-day.png) | [Vue](after/ivy-094-close-day.png) |
| Escalier ouest | [Vue](before/stair-west-side-day.png) | [Vue](after/stair-west-side-day.png) |
| Escalier est | [Vue](before/stair-east-side-day.png) | [Vue](after/stair-east-side-day.png) |
| Arrivée au chemin de ronde | [Vue](before/stair-east-upper-connection-day.png) | [Vue](after/stair-east-upper-connection-day.png) |

## Vérifications

- [Géométrie des escaliers](stairs-geometry-validation.json) : 65 stations de contact avec le mur et 582 sondages d'appui et de dégagement.
- [Déplacement serveur](stairs-server-traversal.json) : 24 cas réussis, montée et descente, marche et course, 20 et 60 Hz, trois lignes latérales. Aucun saut ou téléportation intermédiaire.
- [Circuit complet](wall-circuit-traversal.json) : deux parcours complémentaires couvrent les deux portes, les deux escaliers et tout le chemin de ronde, sans frame sans appui. Trois jonctions critiques sont aussi traversées dans les deux sens.
- [Deux sessions hôte/client](connected-stair-traversal.json) : 20 points de passage atteints, aucune perte d'appui sur 668 échantillons. Après arrêt et convergence, l'écart maximal client/serveur est inférieur à 0,7 mm et l'erreur de hauteur à 0,4 mm. L'écart maximal en mouvement atteint 1,03 m ; le test vérifie le franchissement et la convergence à l'arrêt, pas la qualité de l'interpolation.
- [Préservation et budgets](validation.json) : 28 contrôles passent. Les 2 175 colliders hors racines des escaliers, les 26 lumières, les hauteurs et la peinture du terrain ainsi que 441 assets sources restent inchangés. Aucun script manquant ni matériau invalide.
- [Vent sur les maillages finaux](spreading-climbers-wind-validation.json) : 1 694 pixels foliaires changent entre deux poses GPU ; zéro pixel change avec le vent nul et zéro sur les 4 471 pixels visibles des tiges ancrées.

Le test connecté dans l'éditeur a produit des avertissements Netcode de regroupement de ticks lorsque la simulation prenait du retard. Ils sont conservés dans le [journal de diagnostic](../../../../../artifacts/wall-depth-spread-2026-10-20/console-diagnostic-command.json). Cette passe ne constitue pas un benchmark serveur ni une validation de build distribué.

La [sauvegarde finale](../../../../../artifacts/wall-depth-spread-2026-10-20/final-save-command.json) confirme la scène sauvegardée en mode édition, sans modification en attente, et les 48 nouveaux assets avec leurs `.meta`. La [console finale](../../../../../artifacts/wall-depth-spread-2026-10-20/final-console-command.json) confirme une compilation terminée sans échec, zéro erreur et 25 avertissements Netcode de cadence conservés.
