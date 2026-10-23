# Entrées d’Aubefont — tours jumelles et herses

Cette révision concerne les deux entrées existantes de `StarterIsland`, dans le
projet actif `Unity3D/Arkya`. Chaque entrée conserve une tour de chaque côté :
les bases et le pont du chemin de ronde sont conservés, avec des parties hautes
en pierre peinte et des toitures pointues en ardoise. Une herse levante reste
en position ouverte sous chaque arche.

Les deux entrées sont intégrées et les captures après ont été relues :
les silhouettes extérieures et le passage du chemin de ronde sont conformes.
Les contrôles géométriques réussissent **24/24**, les parcours serveur locaux
**48/48**, les deux circuits complets **2/2**, et les deux herses terminent
leur fermeture puis leur réouverture avec leurs quatre chaînes correctement
attachées. Les preuves sont liées ci-dessous.

## Implantation et mouvement

| Entrée | X | Z | Centres des tours dans le repère de la porte |
| --- | ---: | ---: | --- |
| `Gate - Arrival` | 518,8825 | 481,2953 | X = −6,5 et +6,5 m |
| `Gate - Forest` | 580,7600 | 565,0000 | X = −6,5 et +6,5 m |

Les tours prolongent la maçonnerie de 3,4 m au-dessus du chemin de
ronde ; leur toit atteint 6,6 m au-dessus de ce niveau. Leur passage transversal
de 3,50 m de large et 2,65 m de haut maintient la continuité du chemin de ronde
entre les courtines. Les bases, les arches et les ponts existants sont conservés ;
les anciens vantaux ouverts et les petits parapets supérieurs des tours sont
retirés de la présentation.

`VillagePortcullisPresentation` déplace uniquement la présentation de la herse :
course verticale de 4 m, durée de 2,4 s pour une course entière, départ et arrêt
adoucis. Les deux chaînes relient les attaches de la grille aux poulies fixes.
La position enregistrée est entièrement ouverte ; la désactivation du composant
rétablit cette pose.

Aucune interaction joueur, collision mobile, règle de fermeture ni réplication
réseau n’est ajoutée. Les éléments porteurs statiques gardent leurs collisions.
La future autorité d’une porte bloquant réellement le passage appartient au
serveur.

## Comparaison visuelle

`ReviewVillageGatehouses.Before()` et `After()` ont chacun enregistré huit vues de jour en
1 600 × 1 000 pixels : extérieur, contrechamp intérieur, mécanisme et chemin de
ronde pour chaque entrée. `After()` réutilise les positions, cibles, champs de
vision et heure enregistrés dans la [référence](baseline.json).

| Vue | Avant | Après |
| --- | --- | --- |
| Arrivée — extérieur | [Image](before/arrival-outside-day.png) | [Image](after/arrival-outside-day.png) |
| Arrivée — intérieur | [Image](before/arrival-inside-day.png) | [Image](after/arrival-inside-day.png) |
| Arrivée — mécanisme | [Image](before/arrival-mechanism-day.png) | [Image](after/arrival-mechanism-day.png) |
| Arrivée — chemin de ronde | [Image](before/arrival-wall-walk-day.png) | [Image](after/arrival-wall-walk-day.png) |
| Forêt — extérieur | [Image](before/forest-outside-day.png) | [Image](after/forest-outside-day.png) |
| Forêt — intérieur | [Image](before/forest-inside-day.png) | [Image](after/forest-inside-day.png) |
| Forêt — mécanisme | [Image](before/forest-mechanism-day.png) | [Image](after/forest-mechanism-day.png) |
| Forêt — chemin de ronde | [Image](before/forest-wall-walk-day.png) | [Image](after/forest-wall-walk-day.png) |

Les rapports des captures [avant](before/captures.json) et [après](after/captures.json) confirment la restauration
de l’état de présentation et du drapeau de modification de la scène. Le helper
ne sauvegarde pas la scène et ne déclenche pas l’animation des herses.

## Budgets et préservation

Le [rapport d’intégration](authoring.json) et le
[contrôle géométrique](geometry-validation.json) mesurent les nouvelles parties :

| Élément | Quantité |
| --- | ---: |
| Tours | 4, avec 2 niveaux de détail chacune |
| Triangles par tour détaillée / distante | 6 032 / 2 816 |
| Composants de rendu ajoutés, deux LOD compris | 16 |
| Matériaux référencés par les ajouts | 7 |
| Triangles de tous les meshes de rendu instanciés, deux LOD compris | 42 144 |
| Colliders de maçonnerie des nouvelles tours | 4 |
| Supports statiques des guides | 4 |
| Colliders sur les grilles mobiles | 0 |
| Lumières ajoutées | 0 |

Le total de triangles inclut simultanément les deux versions des tours ; il ne
représente pas le nombre effectivement dessiné dans une frame. Les matériaux
ardoise, chêne et fer sont propres à cette révision ; les textures peintes et la
pierre existante sont réutilisées. Les coupes des anciens lots passent par des
copies locales : les assets sources restent inchangés.

Les hauteurs et la peinture du Terrain, les ancres des deux portes, les arches,
les ponts et les **21 lumières** existantes sont conservés.

## Vérifications

`ValidateVillageGatehouses.BaselineGeometry()` mesure les ouvertures initiales
avec les vrais colliders. La largeur entre piédroits ne suffit pas : les tambours
de tour, leurs semelles et les vantaux existants peuvent réduire le passage.
La [référence géométrique](geometry-baseline.json) et la
[mesure après intégration](geometry-validation.json) donnent des largeurs utiles
**strictement identiques** :

| Passage minimal mesuré | Avant | Après |
| --- | ---: | ---: |
| Arrivée — au sol | 6,635742 m | 6,635742 m |
| Forêt — au sol | 6,635742 m | 6,635742 m |
| Arrivée — chemin de ronde | 3,339844 m | 3,339844 m |
| Forêt — chemin de ronde | 3,320313 m | 3,320313 m |

Les sondes droites sont bornées à
X = −9 à +9 m, à l’intérieur des socles réels ; elles ne prolongent pas
artificiellement cette droite dans les courtines courbes.

`ValidateVillageGatehouses.Main()`, en Edit Mode, réussit **24/24 contrôles** :

- Deux entrées et deux tours par entrée, une de chaque côté.
- Deux grilles entièrement relevées, visibles et sans collision mobile.
- Trois lignes de capsule au sol et trois sur chaque pont.
- La largeur utile à 2 m de hauteur et les appuis du chemin de ronde.
- Les meshes, les matériaux, leurs shaders, leurs métadonnées et les références.
- Les positions des portes, la peinture et les hauteurs du Terrain, ainsi que
  les 21 lumières existantes.

`ValidateVillageGatehouses.Traverse()`, en Play Mode, utilise le véritable
`CharacterLocalTestServerSystem` de l’île : **48 cas**, marche et course à
60 et 20 Hz, pour les deux entrées, au sol et sur les ponts. Chaque passage
utilise trois lignes latérales — centre et ±2 m au sol, centre et ±0,7 m sur les
ponts — et revient à son point de départ sans saut ni
téléportation. Le corps serveur conserve son rayon de 0,30 m et sa hauteur de
1,75 m ; un contrôle complémentaire impose 2 m de dégagement.
Le [rapport de parcours](server-traversal.json) confirme **48/48 réussites**,
**22 278 ticks**, **1 631,544 m** parcourus et aucun tick en l’air.

`ValidateGatehouseWallCircuit.Main()` complète ces passages locaux par les
deux parcours continus du chemin de ronde, y compris les raccords aux
courtines courbes et les accès par les escaliers. Son
[rapport](wall-circuit-traversal.json) confirme **2/2 réussites**,
**1 845 ticks**, **397,093 m** parcourus et aucun tick en l’air.

`ValidateVillagePortcullisPresentation.Main()` observe la fermeture et la
réouverture au fil de vraies frames Unity. Il vérifie la course, la monotonie,
les durées, les attaches de chaînes, les collisions statiques et le retour à
la pose ouverte. Le [rapport d’animation](animation-validation.json) confirme
les deux herses, une course de **4 m**, une fermeture observée en **2,419 s**
puis une ouverture en **2,416 s**, sur **478 échantillons**. Les quatre chaînes
restent attachées sans erreur mesurée ; les collisions statiques, la pose
ouverte et la configuration enregistrée sont restaurées.

Après la sortie du Play Mode, la scène est sauvegardée en Edit Mode avec les
deux herses ouvertes et aucun état de scène restant à enregistrer :
[contrôle de sauvegarde](saved-state.json). La validation géométrique rejouée
sur l’état désérialisé réussit encore **24/24**, avec `sceneSaved: true`.
Le [contrôle final de la console](editor-console-final.json) ne signale aucun
échec de compilation, aucune erreur et aucun avertissement.

Ces preuves concernent l’éditeur Unity et le Play Mode. Aucun build joueur,
test de performance sur machine cible ni nouveau parcours réseau multi-client
n’est revendiqué par cette passe.

## Fichiers

- Scène : [StarterIsland.unity](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity).
- Génération : [UpgradeVillageGatehouses.cs](../../../../../Unity3D/Arkya/AgentScripts/World/UpgradeVillageGatehouses.cs).
- Assets propres, avec métadonnées : [Town/GatehouseUpgrade](../../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Town/GatehouseUpgrade).
- Présentation : [VillagePortcullisPresentation.cs](../../../../../Unity3D/Arkya/Assets/Arkya/World/Environment/Ambience/VillagePortcullisPresentation.cs).
- Captures : [ReviewVillageGatehouses.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewVillageGatehouses.cs).
- Géométrie et 48 parcours serveur : [ValidateVillageGatehouses.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateVillageGatehouses.cs).
- Circuit complet : [ValidateGatehouseWallCircuit.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateGatehouseWallCircuit.cs).
- Liaison ouverte et animation : [ValidateVillagePortcullisPresentation.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ValidateVillagePortcullisPresentation.cs).

Les rapports des passes précédentes restent conservés dans leurs dossiers.
