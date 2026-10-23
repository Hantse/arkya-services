# Aubefont — mairie en pierre et parvis ouvert

La maison du boulanger située devant la mairie est retirée de la composition.
L’édifice civique devient plus imposant : maçonnerie peinte, contreforts aux
quatre angles, porche à arcades, campanile en pierre et grandes bannières
turquoise. L’espace libéré permet de lire la façade depuis la place.

La maison reste désactivée dans la scène pour permettre un retour en arrière.
**Neuf maisons sont actives** ; le groupe conserve son nom historique
`Buildings - ten inhabited silhouettes`. Les jardinières et lianes détachées
de la boulangerie sont retirées de meshes dérivés, sans modifier leurs sources
ni les parties appartenant aux autres maisons. La guirlande de neuf ampoules
est rattachée à la mairie.

Les [deux poutres détachées de l’ancien pignon](orphan-beam-removal.json)
sont également retirées : **88 triangles**, avec les **3 332 triangles**
restants du lot de mobilier conservés. Le [petit accès pavé devenu sans
destination](bakery-spur-removal.json) disparaît avec **28 pierres et
20 joints moussus**, soit **852 triangles**. Seuls leurs indices sont retirés
d’un mesh dérivé ; le reste du pavage et le Terrain sont conservés.

Le [rapport de maçonnerie](hall-masonry.json) décrit **11 904 triangles et
un renderer ajoutés**. Le porche utilise un mesh dérivé de **240 triangles**
sur son renderer existant. Les charpentes intérieures, ouvertures des portes
et fenêtres, planchers et emplacement de la mairie sont conservés. Aucune
lumière ni logique de jeu n’est ajoutée.

## Comparaison visuelle

Les quatre vues utilisent les mêmes positions, objectifs et lumière de jour.
L’état « avant » inclut la reprise précédente du sol et des silhouettes.
Le helper restaure ensuite la caméra et les réglages de présentation.

| Vue | Avant | Après |
| --- | --- | --- |
| Coin de mairie | [Image](before/civic-corner-day.png) | [Image](after/civic-corner-day.png) |
| Accès depuis la rue | [Image](before/civic-street-day.png) | [Image](after/civic-street-day.png) |
| Façade depuis le parvis | [Image](before/civic-frontcourt-day.png) | [Image](after/civic-frontcourt-day.png) |
| Place à hauteur de personnage | [Image](before/plaza-street-day.png) | [Image](after/plaza-street-day.png) |

## Conservation et contrôles

La [référence initiale](baseline.json) enregistre le Terrain, les ancrages
des bâtiments et les **2 499 colliders** présents avant cette passe.
Les hauteurs, la peinture complète et les couches sources du Terrain sont
identiques après modification.

Les **122 colliders de la boulangerie** restent intacts sous sa racine inactive.
Les deux colonnes du porche passent à **64 × 64 cm**, leurs pieds à
**82 × 82 cm**, sans déplacer leurs centres ni modifier leurs hauteurs.
Quatre BoxColliders accompagnent les nouveaux contreforts. Les **2 373 autres
colliders** conservent leurs transformations, géométrie et état.

Le [retrait de la maison](house-removal.json) conserve les attributs et l’ordre
des triangles des décors restants. Les **25 contrôles en Edit Mode**
du [rapport final](validation.json) réussissent sur la **scène enregistrée** :
neuf maisons actives, retrait des deux poutres et du pavage, collisions,
Terrain, assets et `.meta`, matériaux, shaders et absence de scripts manquants.
Les helpers compilent dans l’éditeur sans nouveau diagnostic ; les captures
finales montrent le parvis débarrassé des anciennes poutres.

Le [parcours extérieur](exterior-route.json) reste dégagé après
l’épaississement du porche : **117 contrôles de position et 116 balayages**,
avec une capsule de **35 cm de rayon et 1,80 m de hauteur**. Il reprend le
tracé validé lors de la passe précédente, jusqu’au point extérieur à l’entrée.
Ce contrôle statique ne simule pas la montée de la marche du porche ni un
déplacement en Play Mode.

Les nombres de triangles et de renderers décrivent le contenu ; ils ne
constituent pas une mesure de FPS. Cette passe de présentation ne modifie
aucun système de déplacement ou d’autorité serveur.

## Fichiers

La scène est `Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity`,
dans le projet actif `Unity3D/Arkya`. La maçonnerie est regroupée sous
`Hotel de ville - Aubefont civic house/Civic stone - monumental masonry and arched entry`.
Les ressources et leurs `.meta` sont isolées sous
**`Assets/Arkya/World/StarterIsland/Town/CivicStone20261020/`** :
`Hall/` pour la pierre, les charpentes et les toitures dérivées ; `Removal/`
pour les lots de décor sans la boulangerie, les poutres et le pavage retirés.

| Helper | Rôle |
| --- | --- |
| [BuildMonumentalCivicHall.cs](../../../../../Unity3D/Arkya/AgentScripts/World/BuildMonumentalCivicHall.cs) | Pierre, arcades, campanile et collisions ciblées |
| [RemoveCivicForecourtHouse.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RemoveCivicForecourtHouse.cs) | Maison inactive, retrait de ses décors et rattachement de la guirlande |
| [RetireBakeryGableHeaders.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RetireBakeryGableHeaders.cs) | Deux poutres détachées de l’ancien pignon |
| [RemoveRetiredBakeryPaving.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RemoveRetiredBakeryPaving.cs) | Retrait ciblé de la branche pavée sans destination |
| [ReviewCivicStone.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewCivicStone.cs) | Référence, captures et contrôles |
| [SurveyCivicStone.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SurveyCivicStone.cs) | Relevé préalable des bâtiments et collisions |
