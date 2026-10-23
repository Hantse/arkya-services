# Tissus du village

`UpgradeVillageCloth.cs` a remplacé les anciennes plaques par des maillages
subdivisés : neuf auvents, dix linges, douze bannières existantes et les deux
drapeaux de l'hôtel de ville. Les nouveaux objets `Village cloth banner...`
sont également pris en charge lors d'une nouvelle invocation.

Le drapé au repos, les plis et les ourlets font partie de la géométrie. Le shader
`Arkya/Environment/Painted Village Cloth` déforme réellement les sommets sur le
GPU. Il s'agit d'une animation de présentation, pas d'une simulation physique
Unity Cloth. Le vent et l'humidité proviennent des globals existants
`_ArkyaFoliageWind`, `_ArkyaFoliageClock` et `_ArkyaWeather`. Aucun gestionnaire
par image, composant Cloth, collider, lumière ou champ réseau n'est ajouté.

Les attaches ont un masque UV1 nul. Le bord supérieur du linge rejoint le câble
existant ; les coins avant des auvents rejoignent leurs poteaux. Les masques de
liberté et leurs gradients permettent aux normales de suivre les plis. Les
passes de profondeur, normales et ombres utilisent la même déformation.
Les anciens blasons rigides posés sur les tissus deviennent un motif peint ;
leurs triangles retirés et leurs limites monde sont consignés dans le rapport
d'authoring. La maçonnerie et les collisions restent conservées.

Les matériaux et les maillages dérivés sont isolés sous
`Assets/Arkya/World/StarterIsland/Town/VillageCloth`. Les sources restent intactes.
Les Renderer d'auvents sont conservés pour ne pas casser les références des
abris météo. Les limites CPU des maillages disposent de 0,65 m de marge par axe ;
les tissus ne participent pas au static batching ni au registre des émetteurs
de feuilles. Le budget d'authoring est plafonné à 50 000 triangles et 80 Renderer.

`ValidateVillageCloth.cs` vérifie la géométrie, les attaches au câble et trois
cas GPU : linge, auvent, bannière. La passe technique de silhouette ne participe
pas au rendu normal. Elle permet de mesurer le mouvement géométrique sans le
confondre avec une variation de couleur. Les points attachés doivent conserver
exactement les mêmes pixels, les sommets libres doivent bouger et le contrôle
sans vent doit rester fixe. Le validateur capture aussi la vraie passe de
normales et les ombres URP sur un récepteur immobile, avec le tissu en
`ShadowsOnly`. Tous les objets de sonde sont temporaires et les globals,
l'éclairage et la cible de rendu sont restaurés.

L'exécution dans l'Editor a réussi : **33 Renderer, 30 880 triangles, huit
matériaux et 1 905 sommets attachés**. Les 130 points contrôlés sur les câbles de
linge restent à moins de 5,407 mm de leur ligne. Le shader a été importé et
compilé sans erreur sur Direct3D11. Aucun gestionnaire CPU par image n'a été
ajouté ; ces chiffres décrivent le contenu, pas une mesure de temps GPU.

Le 20 octobre 2026 à 08:49 UTC, les **26 contrôles en Edit Mode ont réussi**.
Trois maillages représentatifs sont rendus aux phases 0 et 3,4 s, avec un vent
fixe de 1,6. Les mesures de variation de pixels sont :

| Sonde | Silhouette | Sommets attachés | Sommets libres | Normales | Ombre URP |
| --- | ---: | ---: | ---: | ---: | ---: |
| Linge | 3 784 | 0 | 44 | 46 270 | 7 823 |
| Auvent | 2 097 | 0 | 206 | 63 942 | 29 |
| Bannière | 2 287 | 0 | 56 | 53 128 | 3 147 |

Le contrôle sans vent et la répétition d'une même phase donnent zéro variation
pour les trois cas. L'ombre de l'auvent est mesurée séparément de sa silhouette :
son retrait modifie 30 779 pixels du récepteur et le cube témoin en modifie
4 085. Le récepteur gris et l'éclairage contrôlé évitent la saturation du ciel
de la scène. L'ambiance, les états des lumières directionnelles, les globals,
les matrices et la cible de rendu sont restaurés, avec vérification après
chacune des trois sondes.

Les preuves se trouvent dans [cloth-authoring.json](cloth-authoring.json) et
[cloth-validation.json](cloth-validation.json), accompagnés des images
`cloth-*.png`. La [vue du marché capturée dans Unity](after/market-street-day.png)
a été inspectée : les auvents conservent leur drapé, leurs festons et leurs
points d'appui. La mesure GPU confirme l'animation que cette capture fixe ne
peut pas montrer.
