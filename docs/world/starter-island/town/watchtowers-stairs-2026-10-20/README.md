# Aubefont — escaliers, postes de guet et lierre

Passe du **20 octobre 2026**, dans `Unity3D/Arkya/`, scène `StarterIsland`. La demande porte sur de vrais escaliers à la place des rampes, quatre postes de guet plus hauts et davantage de végétation grimpante peinte sur les murailles. Le plan de la ville, ses deux portes, les accès existants et le Terrain restent les références à préserver.

Les trois interventions sont intégrées et la scène est sauvegardée. Les **dix captures après** ont été renouvelées puis revues visuellement. Les escaliers passent leurs **24 parcours**, les guets leurs **80 parcours**, le circuit complet ses **deux trajets**, le contrôle du lierre est réussi et les **64 tests Terrain/déplacement** passent. Un parcours connecté hôte/client rejoint également ses quatre points.

## Deux escaliers vers le rempart

Les deux accès existants reçoivent chacun **28 marches**, avec de vrais plans horizontaux et des contremarches physiques. Le support en rampe continue est désactivé. Chaque accès conserve son pied, son orientation, son palier supérieur et sa largeur de **2,9 m**. Un palier intermédiaire de **1,25 m** coupe la montée.

| Accès | Hauteur de marche | Giron | Longueur horizontale | Triangles ajoutés | Renderers | Colliders |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Ouest | 17,753 cm | 35,893 cm | 11,300 m | 2 024 | 4 | 61 |
| Est | 17,158 cm | 35,893 cm | 11,300 m | 2 024 | 4 | 61 |

Ces valeurs proviennent de [stairs-authoring.json](stairs-authoring.json). Ce contrôle trouve **84 appuis par escalier**, sans erreur supérieure à 0,002 m. Il contrôle la géométrie créée et les ancrages ; il ne prouve pas que le personnage atteint le palier.

Le déplacement reste calculé par le serveur ECS. L’adaptation ajoute un franchissement de marche borné à **0,22 m**, avec maintien d’appui sous le disque des pieds et contrôle du volume du corps sur le mouvement vertical puis horizontal. Les surfaces inclinées conservent leur limite de pente de **40°**. Les sondes périphériques ne s’ajoutent que lorsqu’un obstacle ou la perte de l’appui central le justifie. Les [64 tests réussis](terrain-movement-tests.json) couvrent notamment marches trop hautes, plafond bas, montée et descente, marche et course, orientations d’escalier différentes, raccord de rampe et limite de pente du Terrain.

Source : [ReplaceRampartAccessWithStairs.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReplaceRampartAccessWithStairs.cs). Validation du parcours : [ValidateRampartStairs.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateRampartStairs.cs), entrée `Main()`.

## Quatre guets surélevés

Le projet ajoute quatre silhouettes de pierre peinte, bois et chaume, sous **Raised watchtowers - painted guard posts**. Chaque plateforme de guet se situe **3 m au-dessus du chemin de ronde**, soit **Y = 59,105 m**, et se rejoint par **20 marches de 15 cm**, avec un giron de **25 cm**. Le faîte atteint **Y = 63,505 m**, soit 7,4 m au-dessus du rempart.

Les racines sont `Watchtower North 03`, `Watchtower East 27`, `Watchtower South 55` et `Watchtower West 99`. Elles reprennent les indices 3, 27, 55 et 99 du tracé V2. La tour est est décalée de **85 cm** le long de sa travée pour dégager le départ de ses marches. Le passage existant sous les guets doit conserver au moins **1,8 m de largeur utile** et **2 m de hauteur libre**.

Le générateur impose un plafond de **55 000 triangles**, **24 renderers** et **240 colliders** pour les quatre tours réunies. Le [rapport généré](watchtowers-layout.json) compte **24 504 triangles**, **24 renderers**, **148 colliders** et **six matériaux réemployés**. Ces matériaux Arkya de pierre, bois, toile et chaume sont conservés, sans lumière ni comportement runtime supplémentaire. Aucun franchissement n’est déduit de ce seul rapport d’authoring.

Source : [AddRampartWatchtowers.cs](../../../../../Unity3D/Arkya/AgentScripts/World/AddRampartWatchtowers.cs). Le [rapport de parcours](watchtower-traversal.json) de `ValidateWatchtowerTraversal.Main()` réussit ses **80 cas** : deux lignes de circulation et trois lignes d’escalier par tour, à pied et en course, à 60 et 20 Hz, dans les deux sens. La largeur physique praticable minimale mesurée est de **2,039 m**, au-dessus des 1,8 m demandés, avec un contrôle supplémentaire de hauteur libre à **2 m**.

## Lierre peint sur la maçonnerie

Les plantations forment des nappes irrégulières de **1,25 à 2,15 m de largeur**, avec de longues tiges ramifiées et des feuilles à cinq lobes. Leurs surfaces bombées et leurs envers sont de la géométrie ; les dégradés peints sont portés par les couleurs de sommets. La palette associe vert profond, mousse et sauge. Le relief et les espaces entre colonies laissent la pierre lisible.

Le placement a créé **20 nappes extérieures et 8 intérieures**. Il exclut les portes, les accès d’escalier, les contreforts et meurtrières concernés, ainsi qu’un rayon de **8,5 m autour des tours**. Tous les sommets restent au moins **45 cm sous le niveau du chemin de ronde**. La plantation ne couvre donc ni le sol du chemin ni les créneaux.

| Contenu créé pour 28 nappes | Valeur |
| --- | ---: |
| Feuilles LOD0 | 1 792 |
| Triangles LOD0 | 49 952 |
| Triangles LOD1 | 26 208 |
| Matériaux nouveaux | 1 |
| Lots spatiaux / LODGroups | 5 |
| Renderers simultanés hors fondu / pendant fondu | Jusqu’à 5 / 10 |
| Colliders, lumières, shadow casters, gestionnaires runtime ajoutés | 0 |

Ces nombres sont les comptes créés dans [climbing-plants.json](climbing-plants.json), pas un relevé de performance. Le [contrôle séparé après intégration](climbing-plants-validation.json) est réussi : poids de vent, bounds élargis, matériaux, LOD et absence de scripts, collisions, lumières et shadow casters ajoutés. Les plafonds de sécurité du générateur sont de 60 000 triangles LOD0 et 33 000 LOD1.

Le matériau utilise **Arkya/Environment/Painted Foliage Wind** avec `_UseBakedHeightMask = 1`. Les tiges sont fixées avec `UV1.x = 0` ; les feuilles passent de 0 à leur base à 1 à leur pointe. Le mouvement reste discret : amplitude de vent de **0,018 m**, flottement de **0,004 m** et bounds élargis de **0,16 m** par côté. Les globals de vent existants suffisent ; aucun enregistrement dans `FoliageWindParticles` n’est ajouté.

Source : [AddRampartClimbingPlants.cs](../../../../../Unity3D/Arkya/AgentScripts/World/AddRampartClimbingPlants.cs), entrées `Main()` et `Validate()`. Les nouveaux assets sont isolés sous `Town/RampartUpgrade/ClimbingPlants/`. Les sources approuvées, les packs tiers et l’ancien petit habillage végétal restent préservés.

## Comparaison visuelle

Les mêmes cadrages servent aux deux séries. Le [manifeste avant](before/captures.json) et le [manifeste après](after/captures.json) indiquent caméra, cible et phase du jour. Les dix vues « après » ont été renouvelées avec les trois interventions intégrées. Leur revue visuelle confirme la lecture des marches, les nouvelles silhouettes de guet et les nappes de végétation.

| Vue | Avant | Après |
| --- | --- | --- |
| Enceinte de jour | [Image](before/enclosure-day.png) | [Image](after/enclosure-day.png) |
| Enceinte de nuit | [Image](before/enclosure-night.png) | [Image](after/enclosure-night.png) |
| Pied de l’escalier ouest | [Image](before/west-stair-foot.png) | [Image](after/west-stair-foot.png) |
| Profil de l’escalier ouest | [Image](before/west-stair-side.png) | [Image](after/west-stair-side.png) |
| Escalier est | [Image](before/east-stair.png) | [Image](after/east-stair.png) |
| Guet nord | [Image](before/north-watchtower.png) | [Image](after/north-watchtower.png) |
| Guet sud | [Image](before/south-watchtower.png) | [Image](after/south-watchtower.png) |
| Chemin de ronde et guet | [Image](before/wall-walk-tower.png) | [Image](after/wall-walk-tower.png) |
| Lierre de courtine | [Image](before/climbing-wall.png) | [Image](after/climbing-wall.png) |
| Porte d’arrivée | [Image](before/arrival-gate.png) | [Image](after/arrival-gate.png) |

Les captures sont produites par [ReviewRampartUpgrade.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewRampartUpgrade.cs), en Edit Mode, sans sauvegarde de la scène pendant la revue.

Le [parcours connecté](connected-stair-traversal.json) utilise un véritable hôte et client Netcode sur l’escalier ouest, avec montée et descente à pied et en course, sans saut ni téléportation entre les points. Les quatre points sont atteints, avec hauteur et appui corrects. Deux captures nocturnes montrent le personnage [au palier intermédiaire](player-middle-ascent.png) et [sur le chemin de ronde](player-wall-top.png).

## Validation

| Contrôle | État lors de la rédaction | Preuve |
| --- | --- | --- |
| Géométrie des deux escaliers | Réussi ; 28 marches et 84 appuis contrôlés chacun | [Authoring](stairs-authoring.json) |
| Parcours serveur des deux escaliers | **24/24 réussis après le correctif final**, marche/course, 60/20 Hz et trois lignes latérales | [Parcours](stairs-server-traversal.json) |
| Guets : géométrie et budget | Quatre tours générées, budgets respectés | [Authoring](watchtowers-layout.json) |
| Guets : circulation et parcours serveur | **80/80 réussis**, largeur physique minimale **2,039 m** | [Parcours](watchtower-traversal.json) |
| Circuit complet du rempart | **2/2 réussis**, **1 749 ticks**, **375,67 m**, aucun tick aérien | [Circuit](wall-circuit-traversal.json) |
| Escalier ouest avec hôte et client connectés | **4/4 points atteints**, montée et descente sans saut | [Parcours connecté](connected-stair-traversal.json) |
| Lierre : géométrie, UV1, bounds, budget | Authoring et contrôle séparé réussis | [Authoring](climbing-plants.json), [contrôle](climbing-plants-validation.json) |
| Tests Terrain et déplacement après correctifs | **64/64 réussis**, aucun échec ni test ignoré | [Tests](terrain-movement-tests.json) |
| Comparaison visuelle | **10 vues après renouvelées**, revue visuelle effectuée | [Captures](after/captures.json) |
| Portes et intégrité de la ville en Play Mode | **306 contrôles réussis**, aucune ancienne rampe active, aucun script manquant ni matériau non supporté ; 21 lumières conservées | [État final](final-state.json) |
| Sauvegarde et ressources | Scène propre en Edit Mode ; Terrain et réglages graphiques identiques au début de cette passe ; aucun `.meta` manquant | [Conservation](preservation-and-save.json) |

La scène avec les trois ajouts a été sauvegardée avant les tests. Aucune modification géométrique n’est intervenue après la série de captures finales. Les scripts d’authoring ne sauvegardent pas automatiquement la scène et n’enregistrent que leurs assets possédés ; les champs `sceneSaved: false` de leurs rapports décrivent donc l’exécution de ces scripts, pas l’état final de la scène. Les anciens rapports V2 à rampe continue restent historiques : ils ne valident pas les nouveaux escaliers ni les accès des guets.

## Fichiers de cette passe

| Lot | Fichiers ou dossiers | Rôle |
| --- | --- | --- |
| Scène | [StarterIsland.unity](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity) | Deux accès physiques, quatre guets et lierre intégré |
| Déplacement runtime | [CharacterLocalPlaytestSystems.cs](../../../../../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/Playtest/CharacterLocalPlaytestSystems.cs) | Franchissement borné des marches, maintien d’appui, conservation des limites de pente et du contrôle du corps |
| Tests | [CharacterTerrainServerTests.cs](../../../../../Unity3D/Arkya/Assets/Arkya/Characters/Tests/Editor/CharacterTerrainServerTests.cs) | Escaliers réels, raccords de rampes, plafond, rebords trop hauts et pente Terrain |
| Générateurs hors Assets | [Escaliers](../../../../../Unity3D/Arkya/AgentScripts/World/ReplaceRampartAccessWithStairs.cs), [guets](../../../../../Unity3D/Arkya/AgentScripts/World/AddRampartWatchtowers.cs), [lierre](../../../../../Unity3D/Arkya/AgentScripts/World/AddRampartClimbingPlants.cs) | Authoring explicite et limité aux objets possédés |
| Validation et captures | [Escaliers](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateRampartStairs.cs), [guets](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateWatchtowerTraversal.cs), [circuit](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateRampartCircuit.cs), [hôte/client](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateRampartNetworkTraversal.cs), [revue visuelle](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewRampartUpgrade.cs) | Preuves sur la géométrie créée et le déplacement effectif |
| Assets et `.meta` | [Town/RampartUpgrade](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Town/RampartUpgrade/) : `Stairs/`, `Watchtowers/`, `ClimbingPlants/` | Meshes générés et matériau de lierre ; fichiers `.meta` conservés avec chaque ressource |
| Documentation | Ce dossier de rapports et captures, [documentation de la ville](../../../STARTER_ISLAND_TOWN.md) | Périmètre, budgets et résultats vérifiables |

Le [diagnostic ponctuel de rampe](../../../../../Unity3D/Arkya/AgentScripts/World/DiagnoseRampartStairSupport.cs) et sa [trace](ramp-support-diagnostic.json) conservent la reproduction de la régression d’appui avant correction. Cette trace est historique ; les résultats finaux sont ceux du tableau de validation ci-dessus.
