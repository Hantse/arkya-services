# Tonneaux du village

`UpgradeVillageBarrels.cs` remplace les visuels des dix tonneaux de maison et des
cinq tonneaux de cour. Les racines, positions, échelles et colliders existants
sont conservés et comparés après l'opération.

Chaque silhouette comporte seize douves bombées, des joints légèrement creusés,
un couvercle de sept planches en retrait et trois cerclages fins chanfreinés,
avec de petits rivets. Les normales des douves suivent la courbure et les UV du
bois suivent le fil vertical. Les trois matériaux dérivés emploient la texture
peinte de bois existante, deux tons de chêne chauds et un fer bleu grisé modérément
réfléchissant.

Deux tailles partagent quatre maillages : 2 240 triangles pour le détail et 984
pour la distance. Les quinze groupes LOD utilisent au maximum un Renderer
visible chacun, avec trois sous-maillages. Il n'y a pas de nouveau collider,
éclairage ou gestionnaire par image.

Les anciens tonneaux des cours faisaient partie des lots de mobilier. Avant
toute mutation, le script retrouve leurs sommets d'après les rayons, hauteurs et
angles du profil source. Il exige exactement 288 triangles de bois et 288
triangles de cerclages par tonneau. Les triangles conservés sont recopiés sans
modification dans un nouveau maillage ; les sources restent intactes. Un
désaccord de provenance interrompt l'opération avant création d'assets.

Assets dérivés : `Assets/Arkya/World/StarterIsland/Town/VillageUpgrade/Barrels`.
Rapport : `barrel-authoring.json`. L'exécution Editor a réussi pour les quinze
tonneaux, avec les 288 triangles de chaque matière retrouvés sur chacun des cinq
tonneaux de cour. Les positions et colliders sont restés identiques. Le
[gros plan capturé dans Unity](after/barrel-detail-day.png) a été inspecté :
volume bombé, grain vertical, couvercle en retrait et trois cerclages sont
lisibles. Cette version est conservée.
