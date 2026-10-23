# Aubefont — pavés usés, mousse et détails de marché

Le sol du village reçoit une pierre plus sombre, des formats variés et des
angles érodés. Les rangées se décalent autour du puits ; leurs joints et leur
hauteur varient légèrement. Les rues conservent leurs axes et leurs accès,
avec des bords moins réguliers et de la mousse entre certaines pierres.
Le raccord pavé de la mairie rejoint la place.

Les **949 pierres de la place**, **1 884 pierres de rue** et
**1 611 touches de mousse** sont regroupées dans quatre meshes. La texture
conserve un grain peint discret ; les couleurs des sommets sont converties en
espace linéaire. Les comptes détaillés et les raccords sont dans le
[rapport du sol](authoring.json).

**Cinq petits groupes de paille** accompagnent la boulangerie, les marchands,
l’atelier du tonnelier, l’auberge et le séchoir de l’herboriste : deux gerbes
remplies de tiges courbes et d’épis dorés, une botte liée, des brins couchés et
une corbeille renversée. Ces détails restent à l’écart des voies et n’ajoutent
aucun collider, lumière ou comportement d’exécution. Le
[rapport du décor](dressing.json) compte **7 274 triangles, deux renderers,
142 brins couchés et 454 fibres**.

## Comparaison visuelle

Les captures utilisent les mêmes cadrages avant et après. La revue restaure
la caméra, l’heure, la météo, les lumières et le profil de rendu.

| Vue | Avant | Après |
| --- | --- | --- |
| Place à hauteur de personnage | [Image](before/plaza-street-day.png) | [Image](after/plaza-street-day.png) |
| Pavés de la place | [Image](before/plaza-ground-day.png) | [Image](after/plaza-ground-day.png) |
| Rue du marché | [Image](before/market-street-day.png) | [Image](after/market-street-day.png) |
| Entrée du village | [Image](before/arrival-ground-day.png) | [Image](after/arrival-ground-day.png) |
| Vue générale | [Image](before/village-overview-day.png) | [Image](after/village-overview-day.png) |
| Place au crépuscule | [Image](before/plaza-street-evening.png) | [Image](after/plaza-street-evening.png) |
| Coin de mairie | [Image](before/civic-corner-day.png) | [Image](after/civic-corner-day.png) |
| Accès de la mairie depuis la rue | [Image](before/civic-court-street-day.png) | [Image](after/civic-court-street-day.png) |

Les vues proches de la [boulangerie](after/bakery-straw-day.png) et du
[tonnelier](after/cooper-straw-day.png) montrent la paille. Les deux images
« avant » de la mairie sont prises **après la reprise du sol, avant la
réorganisation de cette cour** ; les six autres précèdent la reprise du sol.

## Terrain et conservation

La peinture a modifié **4 515 pixels** d’alphamap dans une fenêtre de
**170 × 170 pixels**, autour de **X = 550, Z = 520**. Son rayon d’intervention
est de **61 m** ; le contrôle extérieur couvre **63 m**. Le disque de
**10 m autour de la réservation de la halle, X = 531, Z = 561**, est exclu.
Les couches, leurs textures sources et les hauteurs du Terrain sont conservées.

Les anciens renderers de pavage sont désactivés, sans suppression de leurs
sources. Un [mesh dérivé des bordures](legacy-pavers.json) retire les
**43 pavés clairs résiduels** qui partageaient leur renderer avec les bordures :
les **3 916 triangles de bordures** et les colliders sont conservés.

Les **seize contrôles en Edit Mode** du [rapport final](validation.json)
réussissent : géométrie, shaders, `.meta`, Terrain et collision, avec
l’unique exception documentée du lampadaire déplacé. Le sol et ses détails
représentent **89 731 triangles et six renderers**, sans ombre portée
supplémentaire. La [référence initiale](baseline.json) et le
[contrôle précédant la retouche de cour](validation-pre-court.json)
restent disponibles pour comparaison.

Ces nombres décrivent le contenu, **pas une mesure de FPS**. Aucun build joueur,
test de charge ou nouveau parcours hôte/client n’est revendiqué pour cette passe.

La scène finale est enregistrée. Pendant l’allègement du premier lot de meshes,
Unity a refusé une opération Undo trop volumineuse et vidé son historique.
Le résultat final a été vérifié ensuite ; la sauvegarde initiale de la scène et
du Terrain reste dans `artifacts/village-ground-2026-10-20/`. Le helper corrigé
utilise une sauvegarde sur disque pour les lots trop volumineux ; sa compilation
a été vérifiée. Ce message historique de l’éditeur n’est pas une erreur de shader
ou de compilation du projet.

## Coin de mairie et silhouettes

Les dix maisons et la mairie reçoivent des toitures légèrement affaissées,
des rives courbes, des pignons et des pièces de charpente moins rectilignes.
La [version finale des onze meshes](../organic-2026-10-20/silhouette-lightweight-refinement.json)
compte **162 337 triangles**, soit **1 969 triangles ajoutés** aux sources
concernées, avec un déplacement maximal de **12,4 cm**. Les **142 renderers de
façade** utilisent leurs meshes et matériaux d’origine. Les portes, fenêtres,
seuils, décorations, transformations et colliders existants sont conservés.

Le [coin de mairie](civic-court.json) est dégagé en déplaçant un lampadaire
existant vers l’auberge et en remontant la guirlande de neuf ampoules de
**1,34 m** à ses attaches. Le raccord pavé contourne le banc déjà en place.
**Un seul BoxCollider**, celui du pied de lampadaire, suit ce déplacement :
sa forme reste identique, ainsi que les **2 498 autres colliders** de la scène.
Les bâtiments et références des lumières sont conservés.

L’accès extérieur jusqu’à l’entrée passe **117 contrôles de position et
116 balayages**, avec une capsule de rayon **35 cm** et de hauteur **1,80 m**.
Ce contrôle statique ne simule ni la montée de la marche du porche inchangée,
ni un parcours serveur à l’intérieur.

## Sources et fichiers

La scène est `Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity`,
dans le projet actif `Unity3D/Arkya`. Le groupe
**Town ground - worn paving and garden transitions** contient le pavage et le
sous-groupe de paille. Les ressources nouvelles et leurs `.meta` sont isolées
sous **`Assets/Arkya/World/StarterIsland/Town/Ground20261020/`**, dont
**`Dressing/`** pour la paille et la corbeille. Seule la peinture du fichier
`Terrain/StarterIslandTerrain.asset` change ; ses octets doivent conserver leur
traitement binaire Git. Les silhouettes possèdent leurs onze meshes sous
**`Town/OrganicSilhouettes20261020/`**.

| Source | Rôle |
| --- | --- |
| [ReworkVillageGround.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReworkVillageGround.cs) | Pavage, mousse, peinture locale et raccords |
| [PaintedVillageGround.shader](../../../../../Unity3D/Arkya/AgentScripts/World/PaintedVillageGround.shader) | Pigments, relief peint et éclairage |
| [RemoveLegacyGroundPavers.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RemoveLegacyGroundPavers.cs) | Retrait ciblé des anciens pavés clairs |
| [DressVillageGround.cs](../../../../../Unity3D/Arkya/AgentScripts/World/DressVillageGround.cs) | Paille, corbeille et affinement des gerbes |
| [ReviewVillageGround.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewVillageGround.cs) | Référence, contrôles et captures |
| [SurveyVillageGround.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SurveyVillageGround.cs) | Relevé des surfaces existantes |
| [SoftenVillageSilhouettes.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SoftenVillageSilhouettes.cs) | Courbes des toits, rives et angles supérieurs |
| [ClarifyCivicCourt.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ClarifyCivicCourt.cs) | Lampadaire, guirlande et contrôle de l’accès extérieur |

Les commandes d’ajout refusent les ressources déjà présentes. Les méthodes de
retouche actualisent uniquement les dérivés possédés ; elles ne recréent pas les
placements des cours.
