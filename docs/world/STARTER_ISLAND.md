# Île de l’Aube — zone de départ, niveaux 1 à 6

`starter-island` est l’identifiant de contenu de l’île de départ. « Île de l’Aube » est son nom de travail. La même carte doit pouvoir accueillir plusieurs copies de simulation dans chaque realm, afin de répartir les arrivées sans changer le realm des personnages.

La scène d’authoring est livrée dans le projet actif **`Unity3D/Arkya`**, sous Unity **6000.6.6f1**, à l’emplacement [StarterIsland.unity](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity). Le projet historique `My project/` conserve son rôle actuel. Ce document sépare le relief et les repères de conception de la future admission multijoueur.

## Sculpture livrée

Le relief est un **Terrain Unity natif et éditable**, avec sa donnée [StarterIslandTerrain.asset](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Terrain/StarterIslandTerrain.asset). Son emprise fait **1 536 × 1 536 m**, pour une plage verticale de **192 m** ; l’océan se trouve à **Y = 28 m**. Cette emprise inclut les fonds sous-marins et l’eau autour des terres. Elle ne représente pas une surface jouable de même taille.

La heightmap utilise **1 025 × 1 025 échantillons**. La [finition de l’environnement](STARTER_ISLAND_ENVIRONMENT.md) porte la peinture à **1 024 × 1 024 échantillons**, avec six couches : prairie, sable, roche, macro-prairie, terre de chemin et terre de sous-bois. Les textures peintes et les transitions irrégulières remplacent les cinq bases procédurales de la sculpture initiale. Les objets végétaux et minéraux réemploient les [prefabs DA à trois LOD](../art-direction/unity-test/lod/README.md), avec leurs matériaux et textures existants.

Les neuf tracés de circulation mesurent **12 à 16 m de large** : arrivée–refuge, boucle des prairies, axe forestier, boucle forestière ouest, vallon, deux montées vers les crêtes, route du port et descente orientale. La largeur, l’échelle du terrain et les seuils de pente sont des valeurs de départ à vérifier avec le personnage réel et sous affluence.

Le refuge utilise désormais des assemblages de bois et de chaume, la cour du sanctuaire des marches et colonnes arrondies, et la jetée une structure de bois. Ces compositions permettent d’examiner les places, silhouettes et circulations ; leur architecture n’est pas une nouvelle collection artistique approuvée. Les sources Scinty et les modèles DA approuvés restent distincts.

La définition [StarterIslandDefinition.asset](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Layout/StarterIslandDefinition.asset) conserve l’identifiant de zone, les niveaux visés, les secteurs, les routes et les positions d’arrivée et de départ. Elle annonce la capacité de contenu à être instancié et le contrat `realmId/zoneId/instanceId`. Ce sont des données d’authoring, sans admission ni placement serveur.

Captures de la sculpture initiale : [vue d’ensemble](starter-island/overview.png), [arrivée et refuge](starter-island/arrival.png), [sanctuaire](starter-island/sanctuary.png). Pour l’aspect actuel à hauteur de joueur, consulter les [captures de l’environnement](STARTER_ISLAND_ENVIRONMENT.md#contrôles-de-revue).

La [couronne d’eau dangereuse](STARTER_ISLAND_BOUNDARY_WATER.md) commence au rayon 835 m autour du centre de l’île, après les dernières terres. Son eau violette bouillonnante annonce une règle serveur de mort après cinq secondes d’exposition ; le guide précise le contact avec l’eau et la remise à zéro par retour vers l’île.

## Parcours et modelé

L’île introduit les niveaux 1 à 6 en ouvrant progressivement l’espace : baie abritée, refuge, prairies, bois, crête puis ruines. La difficulté indiquée ci-dessous décrit l’intention du contenu à placer ; un relief ou un marqueur de niveau ne crée ni ennemis, ni quêtes, ni progression jouable.

| Secteur | Niveaux visés | Rôle spatial |
| --- | --- | --- |
| Anse de l’Éveil, sud-ouest | 1 | Arrivée dans une anse calme, plage large et liaison vers le refuge. |
| Refuge central | 1 à 6 | Point de rassemblement sur une terrasse dégagée ; départ des boucles locales et repère de retour. |
| Prairies du Refuge, sud et ouest | 1 à 2 | Larges clairières pour apprendre le déplacement et les premières interactions ; plusieurs poches d’activité autour des chemins. |
| Bois des Murmures, centre et nord | 2 à 3 | Sentiers en boucle, clairières et lisières lisibles ; la végétation doit laisser voir les ouvertures. |
| Vallon des Sources, est du centre | 3 à 4 | Creux plus intime, sources de mana et liaison entre bois et montées orientales. |
| Crêtes du Levant, nord-est | 4 à 5 | Deux approches de la hauteur et panorama sur l’île. |
| Sanctuaire de l’Aube, hauteur orientale | 5 à 6 | Dernier secteur de découverte, cour ouverte et liaison vers la côte est. |
| Embarcadère de départ, est | 6 | Réserve spatiale pour le futur départ vers le monde principal ; l’admission et le transport restent à implémenter. |

Le parcours principal fait une boucle ouverte autour du refuge. Des liaisons latérales doivent permettre de rejoindre chaque grand secteur sans concentrer tous les joueurs sur un pont, une porte ou un escalier. Le retour au refuge reste une possibilité de circulation, sans imposer un trajet jusqu’à la baie après les ruines.

La silhouette doit alterner côtes basses accueillantes, anses, caps et hauteurs sculptées. Les reliefs secondaires construisent des vues successives et des terrasses exploitables. Les falaises délimitent les secteurs dangereux ou les côtes exposées ; elles ne doivent pas devenir des murs arbitraires au milieu du parcours. Une plage ou un vallon ne doit pas se terminer dans une cuvette sans sortie praticable.

Les repères de secteur servent à préparer la composition. Les noms et niveaux restent des annotations de travail, à remplacer par des éléments intégrés au monde et par le futur contenu de quêtes. Les matériaux et objets suivent les [références artistiques approuvées](../art-direction/resources/README.md), avec volumes modelés et surfaces peintes. Les packs Scinty restent isolés.

## Ouvrir, modifier et reproduire

1. Ouvrir `Unity3D/Arkya` dans Unity, puis la scène `Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity`.
2. Dans la hiérarchie, sélectionner **Starter Island - editable sculpt 1536m**. Les outils du composant **Terrain** permettent de sculpter, adoucir et peindre directement le relief natif.
3. Conserver les ouvertures de circulation lors des retouches. Les marqueurs du groupe **Layout - levels 1 to 6 - realm instances planned** servent de repères ; les données correspondantes sont dans `Layout/StarterIslandDefinition.asset`.
4. Recaler si nécessaire les objets, repères et points de route après une modification de relief. Actualiser les hauteurs côtières avec `RefreshCoastalHeights`, puis enregistrer la scène et les assets modifiés, avec leurs `.meta`. Rejouer la validation après une modification des pentes, du rivage ou des routes.

La scène propose une [caméra libre de revue en Play Mode](STARTER_ISLAND_ENVIRONMENT.md#rendu-et-déplacement-de-revue). Le contrôleur de personnage de gameplay reste à intégrer. Le Terrain et ses collisions Unity classiques ne constituent pas encore une intégration de collision ECS dans la simulation autoritaire du prototype.

Le script de création est conservé dans [SculptStarterIsland.cs](../../Unity3D/Arkya/AgentScripts/World/SculptStarterIsland.cs), hors du dossier `Assets`. Il s’exécute dans l’éditeur connecté avec le CLI de Unity Hub et le package Pipeline déjà installé. La commande `Main` est réservée à la création initiale : elle refuse de remplacer une scène existante afin de préserver les retouches manuelles.

```powershell
$arkyaCli = 'C:/Program Files/Unity Hub/resources/unity.exe'
$arkyaProject = 'D:/Repositories/arkya/Unity3D/Arkya'

& $arkyaCli status --project-path $arkyaProject --json

# Création initiale uniquement, en Edit Mode, sans scène StarterIsland existante.
& $arkyaCli command run_script --file AgentScripts/World/SculptStarterIsland.cs --entry SculptStarterIsland.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json

# Scène StarterIsland déjà chargée dans l’éditeur.
& $arkyaCli command run_script --file AgentScripts/World/SculptStarterIsland.cs --entry SculptStarterIsland.RefreshCoastalHeights --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/World/SculptStarterIsland.cs --entry SculptStarterIsland.Validate --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/World/SculptStarterIsland.cs --entry SculptStarterIsland.Capture --project-path $arkyaProject --caller plugin --skill unity-cli --json
```

`RefreshCoastalHeights` actualise `Terrain/CoastalHeights.asset` depuis le Terrain actuel pour conserver l’accord visuel de l’eau après une retouche du rivage ; cette commande ne modifie pas la sculpture. `Validate` réécrit [validation.json](starter-island/validation.json). `Capture` produit les images de revue dans `docs/world/starter-island/` sans déplacer durablement la caméra. La vue d’ensemble est orthographique, sans brouillard pour lire la carte ; les deux vues rapprochées conservent l’atmosphère de la scène.

## Contrôles de la sculpture

Le [rapport de validation](starter-island/validation.json), produit dans Unity **6000.6.6f1**, est passé sans erreur. Il contrôle la définition de progression, les **six secteurs**, les **neuf routes**, les shaders des matériaux et l’absence de scripts manquants. L’échantillonnage des routes trouve un dégagement minimal de **5,25 m** au-dessus de l’eau et une pente maximale de **21,92 %**, sous le seuil de revue initial de 35 %. Un parcours de grille sur sol sec relie l’arrivée aux centres des six secteurs et au départ. Les bords du Terrain sont immergés.

Les **26 tests Edit Mode** de `Arkya.World.StarterIsland.Tests` passent dans l’éditeur connecté : niveaux, identifiants, géométrie, positions d’arrivée et de départ, et contrat d’identité des instances. Le [résultat des tests](starter-island/tests.json) est conservé.

Ces contrôles vérifient la géométrie et les données d’authoring. Ils ne simulent pas un personnage, ne testent pas les collisions de tous les objets et ne prouvent pas la praticabilité du parcours par la future navigation ECS. Les captures documentent l’aspect de la scène ; le décompte des groupes LOD et des matériaux dans le rapport ne constitue pas un benchmark de performance.

## Instances par realm — conception à implémenter

Une instance est une copie indépendante de la zone, dans le même realm. Elle se distingue d’une **area**, qui est un domaine d’autorité technique au sein d’une simulation continue. Voir la [décision sur les realms et areas](../decisions/0003-realms-and-area-authority.md).

La destination d’admission doit identifier au minimum le tuple logique `(realmId, zoneId, instanceId)`, avec `zoneId = starter-island`. Le contenu géographique est partagé ; les joueurs, ennemis, objets interactifs et états temporaires appartiennent à leur copie de simulation. Deux copies ne doivent pas échanger de visibilité, d’interactions ou d’impacts. Un éventuel découpage d’une copie en plusieurs areas devra conserver son `instanceId` et les règles d’autorité existantes.

La future admission doit respecter les règles suivantes :

- Vérifier côté serveur le compte, le personnage, son realm et son droit d’entrer avant de choisir une destination. Le client ne choisit pas librement une copie en fournissant un identifiant.
- Réserver ensemble les places d’un groupe ; conserver l’affinité du groupe pendant les admissions et reconnexions. Si la copie cible ne peut accueillir tout le groupe, proposer une file ou une autre copie commune, sans séparation silencieuse.
- Ouvrir une nouvelle copie quand les copies disponibles atteignent leur capacité d’accueil visée ; appliquer aussi une limite absolue. Les seuils doivent être configurables et validés sur le build dédié réel.
- Conserver temporairement une affinité de reconnexion à la copie précédente, avec expiration et contrôle des places. Un redémarrage de processus ne doit pas être présenté comme une reprise déjà garantie.
- Cesser les nouvelles admissions avant de fermer une copie ; attendre son évacuation ou appliquer une migration autoritaire explicitement définie. Le changement de copie conserve le realm et la progression persistante.
- Maintenir une seule présence autoritaire par personnage. Les changements de copie ne doivent pas rejouer des récompenses, transférer un impact vers une autre copie ou créer des sauvegardes concurrentes.

Les capacités souple et absolue restent des **paramètres de benchmark**, pas des nombres de joueurs supportés. Les mesures doivent couvrir les arrivées simultanées, un groupe réuni au refuge, des combats répartis et les pics de réplication. Les critères comprennent la tenue du tick serveur configuré, le budget de bande passante, la mémoire et la capacité à retrouver le groupe. Si une limite technique est dépassée, réduire les seuils ou optimiser avant d’augmenter la population annoncée.

Le service Realm, le rattachement persistant des personnages, les tickets d’admission, les réservations de groupe et l’orchestration des copies restent à réaliser. La scène de l’île ne fournit aucun de ces mécanismes et ne modifie pas les API existantes.

## Vérification de la conception

Les dimensions et les distances de la première sculpture constituent des valeurs de départ. Leur validation demande un parcours à la vitesse réelle du personnage et une caméra de jeu : une vue d’ensemble seule ne valide pas l’échelle.

- **Nouveau joueur :** depuis l’arrivée, identifier le refuge et rejoindre le premier secteur sans explication orale. En cas d’hésitation, corriger les vues et la continuité des chemins avant d’ajouter des marqueurs.
- **Circulation :** parcourir chaque liaison, vérifier les pentes et quitter chaque baie, clairière et palier. Toute chute bloquante, marche impraticable ou impasse involontaire échoue au contrôle ; adoucir le relief ou ouvrir une liaison.
- **Affluence :** concentrer une population de test aux arrivées, au refuge et aux objectifs prévus. Si un seul accès oblige à une file ou masque les interactions, élargir l’espace et répartir les poches d’activité avant de relever la capacité d’instance.
- **Progression :** faire lire les secteurs à un observateur sans connaître les niveaux. Si la crête et les ruines semblent aussi sûres que la baie, renforcer leur position, leur silhouette et leurs transitions visuelles.
- **Abus :** contrôler les raccourcis côtiers, les pentes, les sorties de carte et le départ anticipé. Les futures restrictions de niveau et récompenses relèvent du serveur ; un obstacle de décor n’est pas un contrôle d’autorisation.

Les quêtes, les ennemis, la navigation de production, l’équilibrage de l’expérience et les tests de charge sont des étapes distinctes. La livraison du terrain ne vaut pas validation d’un parcours complet de progression ni d’une capacité MMORPG.
