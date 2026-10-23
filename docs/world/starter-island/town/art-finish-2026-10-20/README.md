# Aubefont — composition et matières peintes

La mairie gagne en présence sans écraser la petite place : elle se décale vers l’est, pivote légèrement et grandit de 20 %. Un parvis irrégulier et une approche courbe la raccordent au puits. Des plantations regroupées aux façades, un chaume miel plus calme et des pierres aux valeurs rapprochées donnent une lecture plus douce à la ville.

Cette étape suit la [mairie en pierre](../civic-stone-2026-10-20/README.md) et la [reprise du sol](../ground-2026-10-20/README.md). Les neuf maisons restent actives ; l’ancienne boulangerie reste désactivée. Aucun comportement de jeu n’est ajouté.

## Composition et matières

La mairie passe de **X532/Z554** à **X539/Z552**, à une altitude de **50,306 m**, avec une orientation de **160°** et une échelle uniforme de **1,2**. Ses **181 colliders** suivent la hiérarchie complète et gardent leur géométrie locale. Les **2 322 autres colliders** conservent pose, forme et état. La guirlande rejoint la nouvelle façade. Voir le [rapport de déplacement](hall-relocation.json).

L’ancien raccord civique perd uniquement ses **66 pierres et 45 touches de mousse**. Le nouveau parvis, large de **6,4 m**, rejoint la place par un chemin de **2,2 m** de large. Un lit continu de joints terre et mousse ancre les pierres au sol. **315 pierres et 38 touches de mousse** complètent aussi des lacunes des rues existantes, en conservant leur tracé et les exclusions de l’ancienne boulangerie. Les rapports [parvis](forecourt.json), [joints](civic-joints.json) et [rues](gap-paving.json) précisent ces interventions.

Les **23 groupes plantés** rassemblent 23 buissons, 46 fleurs et 69 touffes d’herbe. Trois petits arbres de cour existants sont élargis. Les végétaux proviennent des modèles Arkya approuvés ; quatre groupes de LOD retirent les petites fleurs et herbes à distance. Le [rapport des jardins](gardens.json) conserve leurs positions et dimensions.

La [passe de matières](SURFACES.md) affecte **29 matériaux dérivés à 648 renderers**, sans remplacer les textures sources. La [retouche finale des pigments](surfaces-pigment-refinement.json) assourdit le chaume et conserve les nuances minérales des pavés. La prairie est adoucie localement sur **13 369 texels**, dans un rayon de **49 m** : les hauteurs, les huit couches sources et les poids de terre, chemins, roche et sable restent identiques. Le total des trois couches de prairie varie au plus de la quantification native, environ **1/255**.

## Halo et ambiance

Le grand disque blanc visible dans la vue Scene de l’éditeur venait de la diffusion du bloom sur les fortes valeurs HDR. Les [essais avec gizmos, flares et particules désactivés](lighting-probe/variants.json) le conservaient ; [borner l’entrée HDR du bloom](lighting-probe/bloom-probe.json) le supprimait. Les captures de caméra hors écran ne reproduisaient pas le défaut : la preuve ci-dessous lit directement l’image LDR affichée par la vue Scene. Le pixel ou matériau à l’origine de cette valeur HDR n’a pas été identifié.

Deux profils dérivés limitent désormais le bloom à **8**, en le laissant actif. Le profil local utilise une intensité de **0,25** et un seuil de **1,2**, avec un léger réchauffement et un contraste modéré. Le soleil, le cycle horaire, les lumières et les profils sources restent inchangés. Le [rapport lumière](lighting-finish.json) contient tous les réglages.

| Vue réelle de l’éditeur | Avant correction | Après correction |
| --- | --- | --- |
| Halo HDR, mêmes effets et gizmos | [Avant](lighting-probe/native-before-finish.png) | [Après](lighting-probe/native-after-finish.png) |

Ces deux images documentent le correctif de lumière avant la dernière reprise du parvis. Les vues finales de composition sont celles du tableau suivant.

## Comparaison visuelle

Les six vues utilisent les mêmes caméras et phases horaires avant et après. Le helper restaure les états de présentation à la fin de chaque série.

| Vue | Avant | Après |
| --- | --- | --- |
| Ensemble de la ville | [Avant](before/village-overview-day.png) | [Après](after/village-overview-day.png) |
| Mairie et cours voisines | [Avant](before/civic-corner-day.png) | [Après](after/civic-corner-day.png) |
| Rue et marché | [Avant](before/market-street-day.png) | [Après](after/market-street-day.png) |
| Puits et pavage | [Avant](before/fountain-day.png) | [Après](after/fountain-day.png) |
| Extérieur des murailles | [Avant](before/wall-exterior-day.png) | [Après](after/wall-exterior-day.png) |
| Place au crépuscule | [Avant](before/town-twilight.png) | [Après](after/town-twilight.png) |

## Vérification et budget

La [validation finale de scène](validation.json) réussit ses **19 contrôles**, avec la scène sauvegardée. Elle compare l’état courant à la [référence immuable](baseline.json) : colliders, maisons, ancrages, relief, peinture hors fenêtre, lumières, matériaux, shaders et fichiers `.meta`. Le helper compile dans l’éditeur et aucun shader de ville ne présente d’erreur. Le [parcours extérieur](exterior-route.json) rejoint le nouvel ancrage devant la mairie avec une capsule de **0,35 m de rayon et 1,8 m de hauteur** : **97 positions et 96 balayages**, sans obstacle. Ce contrôle en Edit Mode s’arrête devant l’entrée ; il ne simule ni les marches intérieures ni le déplacement réseau.

| Ajout | Triangles stockés | Renderers | Nouveaux colliders / lumières |
| --- | ---: | ---: | ---: |
| Jardins, tous niveaux de LOD | 388 010 | 20 | 0 / 0 |
| Parvis et approche, joints compris | 4 779 | 2 | 0 / 0 |
| Compléments des rues | 7 902 | 4 | 0 / 0 |

Le retrait de l’ancienne approche enlève **1 989 triangles**. Le contenu actif de la ville passe de **4 058 546 à 4 457 248 triangles stockés**, et de **1 419 à 1 445 renderers**. Les deux volumes, les 26 lumières de scène et les 21 lumières de ville sont conservés. Ces inventaires incluent les niveaux de LOD présents ; ils ne mesurent ni les triangles effectivement dessinés, ni les draw calls, ni les FPS. Aucun test Play Mode n’est revendiqué pour cette passe.

## Sources

Les assets propres sont sous `Assets/Arkya/World/StarterIsland/Town/ArtFinish20261020/`, dans `Surfaces/`, `Gardens/`, `Forecourt/`, `PavingGaps/` et `Lighting/`. La scène et le TerrainData de l’île portent les affectations et la peinture locale ; les `.meta` accompagnent les assets. Les sources et ressources Scinty restent séparées.

Les helpers d’authoring sont [MoveCivicHallForArtFinish.cs](../../../../../Unity3D/Arkya/AgentScripts/World/MoveCivicHallForArtFinish.cs), [ReconnectCivicForecourt.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReconnectCivicForecourt.cs), [SettleCivicPavingJoints.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SettleCivicPavingJoints.cs), [FillTownPavingGaps.cs](../../../../../Unity3D/Arkya/AgentScripts/World/FillTownPavingGaps.cs), [RecomposeTownGardens.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RecomposeTownGardens.cs), [RefineTownSurfaces.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineTownSurfaces.cs) et [FinishTownLighting.cs](../../../../../Unity3D/Arkya/AgentScripts/World/FinishTownLighting.cs). [ReviewTownArtFinish.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewTownArtFinish.cs) produit la référence, les captures, le diagnostic du halo et la validation. Ces outils restent hors `Assets` ; leurs commandes d’authoring ne doivent pas être relancées comme une régénération générale de la ville.
