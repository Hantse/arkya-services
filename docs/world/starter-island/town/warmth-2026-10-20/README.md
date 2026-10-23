# Aubefont — matières, maisons et vie du village

Cette passe poursuit la direction peinte de l’île dans le projet actif
`Unity3D/Arkya`, scène `StarterIsland`. Elle reprend les surfaces du village,
complète les intérieurs et ajoute un hôtel de ville à deux niveaux. Les fonctions
de commerce et de mairie restent décoratives.

## Changements

- Dix maisons pourvues de planchers physiques, de seuils raccordés au Terrain et
  de vitres légèrement teintées. La boulangerie et l’entrepôt gagnent un étage :
  cinq maisons ont désormais un étage accessible.
- Hôtel de ville de 12 × 8 m en X = 532, Z = 554 : soubassement en pierre,
  crépi clair, porche, toiture d’ardoise, campanile et deux bannières.
- Six escaliers intérieurs en L contre les murs droit et arrière, avec palier
  d’angle et trémie adaptée. Les deux escaliers de muraille longent
  désormais la courtine : 28 marches à l’ouest, 36 à l’est, largeur de 2,60 m
  et courte liaison latérale vers le chemin de ronde.
- Le [raccord bas de l’escalier est](east-stair-wide-ground-transition.json)
  ajoute une marche de 75 cm de profondeur : contremarche supérieure de
  12,68 cm et raccord au Terrain inférieur à 18,31 cm sur les lignes testées.
- Les mains courantes et leurs poteaux sont retirés des six escaliers intérieurs,
  des deux escaliers de muraille et des quatre escaliers de guet, conformément
  à la demande. Les barrières horizontales des étages et plateformes restent
  distinctes des volées.
- Crépis ivoire, ocre et sauge, grain fin et variation douce. Les matériaux
  partagés des modules d’origine sont conservés.
- Pierre de muraille et pavés avec des tons minéraux, des joints irréguliers et
  un relief éclairé. La place reçoit 499 dalles ; une peinture locale adoucit
  les bords des chemins sans modifier les hauteurs.
- Dix jardinières et cinquante groupes de fleurs approuvées. Six instances
  de végétation qui entraient dans les maisons sont désactivées localement.
- Trois guirlandes, soit 27 ampoules existantes, raccrochées à l’extérieur
  des façades ; leurs câbles ne traversent plus les pièces.
- Quinze [tonneaux refaits](BARRELS.md), avec douves bombées, couvercle en
  retrait, cerclages fins et deux niveaux de détail.
- Trente-trois [tissus déformables](CLOTH.md) : neuf auvents, dix linges,
  douze bannières de remparts et deux bannières de mairie. Les attaches restent
  fixes ; les plis, ourlets, normales et ombres suivent le vent.

La déformation des tissus est une animation GPU de présentation. Aucun composant
Unity Cloth ni état physique de tissu n’est répliqué. Les références des abris
pluie sont actualisées pour les neuf auvents et les 29 éléments de toiture.

## Revue visuelle

Les [captures avant](before/captures.json) et [après](after/captures.json)
conservent leurs cadrages et l’heure de l’île ; la hauteur des vues intérieures
est recalée sur les nouveaux planchers. Des vues détaillées
supplémentaires montrent la mairie, les tonneaux et l’implantation des escaliers.
Les helpers restaurent la caméra, l’heure, la météo et les paramètres de rendu.

| Vue | Avant | Après |
| --- | --- | --- |
| Marché | [Image](before/market-street-day.png) | [Image](after/market-street-day.png) |
| Sol et place | [Image](before/plaza-street-day.png) | [Image](after/plaza-street-day.png) |
| Muraille | [Image](before/inner-wall-day.png) | [Image](after/inner-wall-day.png) |
| Intérieur de l’auberge | [Image](before/inn-interior-day.png) | [Image](after/inn-interior-day.png) |
| Étage de l’auberge | [Image](before/inn-upper-floor-day.png) | [Image](after/inn-upper-floor-day.png) |

[Hôtel de ville](after/civic-house-day.png) ·
[Intérieur de la mairie](after/civic-upper-interior.png) ·
[Tonneau en gros plan](after/barrel-detail-day.png) ·
[Escalier ouest sans rampe](after/wall-stair-day.png) ·
[Escalier est sans rampe](after/east-wall-stair-day.png) ·
[Vue d’ensemble](after/village-overview-day.png).

## Fichiers et budgets

Les ressources propres sont isolées sous :

- `Assets/Arkya/World/StarterIsland/Town/VillageUpgrade/Architecture/`
- `Assets/Arkya/World/StarterIsland/Town/VillageUpgrade/Barrels/`
- `Assets/Arkya/World/StarterIsland/Town/VillageCloth/`
- `Assets/Arkya/World/StarterIsland/Town/Warmth20261020/`

L’authoring et la revue sont dans `AgentScripts/World/` :
`EnhanceVillageArchitecture`, `WarmVillageSurfaces`,
`RefineVillageStoneMaterials`, `UpgradeVillageCloth`,
`UpgradeVillageBarrels`, `FinishVillageWarmth`, `RehangVillageFestoons`
et `ReviewVillageWarmth`. Les shaders de tissu sont dans
`Assets/Arkya/World/Environment/Shaders/`.
`AlignRampartStairsWithWalls` porte le déplacement des accès extérieurs.
`RemoveWatchtowerStairRails` retire les rampes de guet dans quatre meshes dérivés
sans modifier les sources. Les rapports de retrait sont
[intérieurs](interior-stair-handrail-removal.json),
[muraille](open-stairs-ground-approach.json) et
[guets](watchtower-stair-rails-removed.json).

## Vérification

Les [44 parcours intérieurs](interior-server-traversal.json) réussissent après
la mise en L et le retrait des mains courantes : onze bâtiments, marche et
course, à 60 et 20 Hz, en aller-retour,
sans saut ni téléportation entre les points. Le contrôle utilise le véritable
déplacement serveur ECS et les colliders de la scène, avec un dégagement
supplémentaire de 2 m en hauteur.

Les [24 parcours des accès de muraille](stairs-server-traversal.json) passent
après le raccord de 75 cm à l’est, en montée et descente, à pied et en course,
à 60 et 20 Hz, sur trois lignes latérales. Les
[deux circuits complets](wall-circuit-traversal.json) couvrent 397,093 m et
1 845 ticks, sans tick aérien. Les [80 parcours de guet](watchtower-traversal.json)
passent après le retrait de leurs mains courantes ; la largeur physique minimale
mesurée est de 2,208 m.

Les [26 contrôles de tissus](cloth-validation.json) réussissent dans l’Editor
avec Direct3D11 : géométrie, attaches, vent nul, normales et ombres URP.
Les trois sondes comparent deux phases et une répétition identique, avec un
contrôle sans émetteur d’ombre. L’exposition du récepteur est bornée pour
éviter de confondre un blanc saturé avec une absence d’ombre.

La scène est sauvegardée. Les dix contrôles de
[l’état final](final-state.json) passent : matériaux et shaders valides,
scripts présents, `.meta` présents, anciens colliders inclinés désactivés,
33 tissus et 15 tonneaux avec deux LOD. Le
[contrôle final de l’Editor](editor-console-final.json) ne signale aucune
erreur de compilation ni erreur ou avertissement dans la console actuelle.
Cette passe n’a pas exécuté de build joueur ni de nouvelle session hôte/client.

Les ajouts de pavage et de contact de mur représentent 14 760 triangles et
sept renderers ; les jardinières et fleurs, 130 720 triangles et trois
renderers. L’accès pavé de la mairie ajoute 180 triangles et un renderer.
Chaque tonneau utilise 2 240 triangles de près ou 984 de loin, avec un seul
renderer visible et trois matières. Ces comptes ne sont pas une mesure de
performance sur le client final.

Le budget reste de 21 lumières ponctuelles, avec six lanternes actives au
maximum. Les hauteurs du Terrain conservent leur SHA-256
`0fe6f9005188cc2f3f0ea5fad2a7f41f8561672201070929b767dfe9c73c0676`.
Les positions X/Z et orientations des maisons sont conservées ; leurs petites
corrections verticales assurent que les nouveaux planchers restent au-dessus du
Terrain. Les assets Scinty et le projet historique `My project` sont inchangés.
