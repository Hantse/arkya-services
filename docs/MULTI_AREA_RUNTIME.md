# Areas, contenus et portails automatiques

Un realm contient plusieurs **Areas**, chacune représentant une carte complète.
Chaque Area possède ses coordonnées locales et peut être découpée entre
plusieurs dedicateds. Deux cartes peuvent couvrir les mêmes coordonnées sans
partager leur autorité. Les passages entre cartes utilisent des zones d'entrée
ou portails automatiques explicitement configurés ; toucher les coordonnées
d'une autre carte ne déclenche aucun changement d'Area.

Cette tranche ajoute le catalogue de contenus et le transfert entre Areas au
protocole de [handoff entre partitions](STARTER_ISLAND_HANDOFF.md). Les tests
backend passent avec MongoDB réel. Le parcours réel vérifié utilise deux
Areas, quatre processus dedicated et deux clients Editor successifs : les
transferts, les cartes chargées et la reconnexion sont décrits ci-dessous.

## Identité de la carte et contenu Unity

La hiérarchie reste `Realm → Areas → partitions → dedicateds`. Les champs
historiques `topology.islands`, `islandId`, le type `Island` et les IDs de
serveurs `island-*` conservent leurs noms dans les API et les snapshots.
`islandId` désigne ici l'identifiant logique de l'Area.

`contentId` sélectionne un contenu inclus dans les builds Unity. Il est
optionnel : son absence utilise l'identifiant de l'Area. Le backend connaît
uniquement cet identifiant stable ; il ne reçoit ni ne charge un chemin de
scène fourni par un joueur. Le registre Unity
[`ArkyaAreaCatalog.json`](../Unity3D/Arkya/Assets/Arkya/Characters/Resources/ArkyaAreaCatalog.json)
associe chaque contenu à ses scènes client et serveur, son point d'arrivée
initial et son niveau marin. Un contenu absent du build empêche le démarrage
du dedicated ou la préparation du client.

Le registre actuel comprend `starter-island` et `handoff-valley`. Ce dernier
est un contenu de validation distinct de la carte de départ. Ajouter un
identifiant au plan backend ne crée pas une scène : son contenu, ses collisions
et son inclusion dans les deux builds doivent être préparés dans Unity.
L'allocation dynamique de copies privées, d'instances et de raids reste séparée.

## Choisir l'Area au lancement du dedicated

Une invocation du dedicated sert **une seule Area**. Plusieurs processus du
même realm peuvent sélectionner cette Area ; chacun conserve son `serverId`,
sa partition d'autorité et son endpoint. Le même binaire contient les cartes
enregistrées, mais chaque processus ne charge que la scène serveur de son
Area, en plus de sa scène de bootstrap.

Le lancement accepte `--area=StarterIsland`, `--area=starter-island` ou
`--area StarterIsland`. Les noms abrégés ignorent la casse et les tirets :
`StarterIsland` désigne l'ID logique `starter-island`. Si plusieurs IDs du
realm correspondent à ce nom, utiliser l'ID exact. Le sélecteur porte sur
l'Area logique, pas sur son `contentId` ni sur un chemin de scène.

Exemples de **processus distincts**, pour le plan à deux Areas ci-dessous :

```powershell
.\ArkyaServer.exe -batchmode -nographics --area=StarterIsland `
  -arkya-realm-id eu-one -arkya-server-id island-starter-island-01 -arkya-game-port 17943
.\ArkyaServer.exe -batchmode -nographics --area=StarterIsland `
  -arkya-realm-id eu-one -arkya-server-id island-starter-island-02 -arkya-game-port 17944
.\ArkyaServer.exe -batchmode -nographics --area=HandoffValley `
  -arkya-realm-id eu-one -arkya-server-id island-handoff-valley-01 -arkya-game-port 17945
```

L'origine Characters et la clé liée au couple realm/serveur doivent être
fournies séparément dans l'environnement de chaque processus. Les ports et
endpoints de la topologie doivent correspondre à ces écoutes. Chaque commande
conserve le processus au premier plan ; utiliser des terminaux distincts ou le
superviseur de déploiement pour les exécuter ensemble.

La priorité de configuration est `--area`, puis `ARKYA_AREA_ID`, puis le nom
historique `ARKYA_ISLAND_ID` déjà injecté par Realms. En leur absence, le
dedicated résout son Area depuis sa partition. Une topologie historique sans
partitions conserve uniquement le repli `starter-island`.

La topologie authentifiée reste l'autorité : demander une autre Area que
celle attribuée au `serverId`, une Area inconnue ou un nom ambigu arrête le
processus avec le code 1 **avant le chargement de la carte et l'écoute réseau**.
Une option vide, invalide ou répétée est également refusée. Pour servir deux
Areas, lancer deux processus ; ne pas répéter `--area` dans une invocation.
Un contenu absent du build empêche toujours le démarrage. Le journal de
readiness indique le realm, le serveur, l'Area, le contenu et la scène chargée.

## Exemple : deux Areas et quatre dedicateds

Le corps suivant peut être utilisé avec `POST /admin/realms`, après provision
d'une cible et d'un profil autorisés. Le profil `multi-area-v1` de cet exemple
doit déclarer son serveur modèle, ses endpoints WebSocket et la paire réseau
version 3 décrite plus bas. Chaque carte fait 1 536 × 1 536 unités et utilise
deux partitions en X. Le Y des points d'arrivée est une altitude Unity ; les
rectangles source portent uniquement sur X/Z.

```json
{
  "realmId": "eu-one",
  "displayName": "EU One",
  "targetId": "realm-eu-one",
  "profileId": "multi-area-v1",
  "topology": {
    "islands": [
      {
        "islandId": "starter-island",
        "contentId": "starter-island",
        "dedicatedCount": 2,
        "width": 1536,
        "depth": 1536,
        "originX": 0,
        "originZ": 0,
        "gridColumns": 2,
        "gridRows": 1
      },
      {
        "islandId": "handoff-valley",
        "contentId": "handoff-valley",
        "dedicatedCount": 2,
        "width": 1536,
        "depth": 1536,
        "originX": 0,
        "originZ": 0,
        "gridColumns": 2,
        "gridRows": 1
      }
    ],
    "instanceDedicatedCount": 0,
    "raidDedicatedCount": 0,
    "areaTransitions": [
      {
        "id": "starter-to-valley",
        "sourceAreaId": "starter-island",
        "targetAreaId": "handoff-valley",
        "sourceBounds": { "minX": 810, "minZ": 740, "maxX": 820, "maxZ": 760 },
        "targetPosition": { "x": 750, "y": 20, "z": 750 },
        "targetForward": { "x": 1, "z": 0 }
      },
      {
        "id": "valley-to-starter",
        "sourceAreaId": "handoff-valley",
        "targetAreaId": "starter-island",
        "sourceBounds": { "minX": 810, "minZ": 740, "maxX": 820, "maxZ": 760 },
        "targetPosition": { "x": 790, "y": 38, "z": 750 },
        "targetForward": { "x": 1, "z": 0 }
      }
    ]
  }
}
```

Le rectangle source se trouve dans la partition est de sa carte. Le premier
portail arrive dans la partition ouest de `handoff-valley` ; le retour arrive
dans la partition est de StarterIsland. Le point d'arrivée doit correspondre
au terrain réel et rester hors d'une zone qui redéclencherait immédiatement un
autre passage. Les portails sont dirigés : un retour exige sa propre entrée.

Le plan peut encore être remplacé en état `Draft` avec
`PUT /admin/realms/{realmId}/topology` et le corps
`{ "expectedRevision": 0, "profileId": "multi-area-v1", "topology": { ... } }`.
Une fois déployé, le plan est figé ; une release d'image ne peut pas changer
les contenus ou les portails. Une migration de runtime reste une opération
d'exploitation distincte.

La validation conserve les limites de 32 Areas, 64 dedicateds par groupe et
256 dedicateds au total. Elle autorise au plus 64 transitions avec des IDs
uniques. Les Areas source et cible doivent exister et être différentes ; le
rectangle source doit être fini, strictement positif et inclus dans la carte.
L'arrivée doit résoudre une partition de la carte cible et ses coordonnées
finies sont bornées à ±100 000. `targetForward` est facultatif : absent, il
conserve l'orientation source ; renseigné, il doit être fini et de norme au
carré comprise entre 0,0001 et 1,01. Les contenus et IDs de transitions utilisent
des identifiants de 3 à 31 caractères, avec lettres minuscules, chiffres et
tirets, commençant par une lettre et terminant par une lettre ou un chiffre.

## Catalogue et paire réseau

Les nouveaux builds utilisent `protocolVersion = 3` et
`buildId = arkya-multi-area-v1`, avec le ghost
`Arkya.Characters.Partition.v3`. Les API gardent les paires exactes
`1 / arkya-web-beta-v1`, `2 / arkya-partition-handoff-v1` et
`2 / arkya-character-stats-v1`. Une admission doit correspondre à la paire
exacte du realm. Un profil version 2 peut conserver ses plans historiques ;
un `contentId` explicite ou une transition non vide exige le runtime version 3
pour être déployable. Sinon le brouillon reste enregistrable, mais le
déploiement est refusé avec `topology_runtime_unsupported`.

`GET /internal/game/topology` fournit les partitions historiques et les
champs optionnels `areas = [{ areaId, contentId }]` et `areaTransitions`.
Les entrées standalone de configuration utilisent les mêmes propriétés sous
`GameAccess:StandaloneEntries`. La lecture est possible avant la readiness
pour démarrer les dedicateds ; l'admission et le prepare exigent ensuite une
destination prête. Les clés dedicated doivent couvrir tous les IDs du plan,
via une liste explicite ou `RealmRuntime:ServerIds`, comme décrit dans le
[guide de déploiement](REALM_DEPLOYMENT.md).

Les champs nouveaux absents sont omis de la sérialisation des snapshots afin
de conserver les empreintes des anciens profils. Le plan fige les contenus
et les transitions ; leur modification ne fait pas partie d'une release
portant uniquement sur l'image.

### Profil de déploiement version 3

Après publication du dedicated et du client compatibles, ajouter cet objet
à la liste **existante** `Deployment:Profiles` de Realms. Remplacer le digest
illustratif par celui de l'image vérifiée annonçant exactement
`3 / arkya-multi-area-v1` ; un digest historique version 1 ou 2 ne convient pas.
L'API exige un digest SHA-256 réel de 64 caractères hexadécimaux.

```json
{
  "Id": "multi-area-v1",
  "DisplayName": "Multi-Area · protocol 3",
  "Image": "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:<digest-de-la-build-3>",
  "ImagePullSecretName": "arkya-ghcr",
  "TopologyTemplateServerId": "island",
  "TopologyPublicEndpointTemplate": "wss://api.arkya.online/game/{realmId}/{serverId}/",
  "Servers": [
    {
      "Id": "island",
      "Port": 17943,
      "Transport": "WebSocket",
      "PublicEndpoint": "wss://api.arkya.online/game/eu-one/island/",
      "ProtocolVersion": 3,
      "BuildId": "arkya-multi-area-v1",
      "Args": []
    }
  ]
}
```

Ce profil rend le plan de l'exemple sélectionnable dans Admin après adoption
de la configuration et redémarrage contrôlé de Realms. La cible autorisée, le secret
de registre, les routes WSS vers chaque dedicated et les identités privées
Characters doivent être provisionnés pour les IDs générés du plan, comme
décrit dans le [guide de déploiement](REALM_DEPLOYMENT.md). Les capacités
instance/raid doivent rester à zéro avec ce binaire.

Les profils historiques et leurs snapshots restent inchangés. Le composant
GitOps `partition-handoff` prépare uniquement la version 2 et n'active pas
ce nouveau profil. Passer un realm déjà déployé de version 2 à version 3
demande une migration explicite de runtime ; changer seulement son image
ne suffit pas. Cet exemple ne publie aucune route ni aucun workload.

La promotion automatique du dedicated vers le GitOps bêta vérifie aussi la
paire réseau du client WebGL déjà publié, à partir de sa révision Git figée.
Si la nouvelle image annonce une autre paire, la vérification refuse la
promotion avant toute modification du manifeste. La publication des artefacts
reste possible ; leur adoption pour un nouveau profil suit la configuration
explicite ci-dessus.

## Passage autoritaire et état durable

L'entrée dans une zone source déclenche automatiquement la préparation du
portail. Le joueur n'envoie aucun ID de propriétaire ou point d'arrivée
faisant autorité. Le dedicated source utilise son catalogue authentifié :

1. `POST /internal/game/leases/{characterId}/transfers/prepare` porte le
   `transitionId` et la pose source. Le backend vérifie le propriétaire, le
   rectangle source et le dedicated qui possède l'arrivée configurée. Le
   serveur source immobilise l'acteur pendant le chargement de l'autre carte,
   tout en conservant sa lease et les sauvegardes autorisées avant seal.
2. L'offre Netcode indique le ticket, l'endpoint, l'Area et le contenu cibles.
   Le client charge et prépare la scène cible avant d'envoyer le claim. Le
   `POST /internal/game/transfers/claim` ne crée aucune autorité : sa lease
   est vide et son époque n'est encore qu'une valeur préparée.
3. Le seal revalide la pose finale dans la zone et la partition source.
   `position`, `forward` et `simulation` décrivent l'état final **source** ;
   `arrivalSimulation` contient son adaptation à la carte cible, calculée
   uniquement par le dedicated. La position cible vient du catalogue ;
   `targetForward`, lorsqu'il est renseigné, remplace l'orientation source.
   Le backend conserve séparément `SourceState` et l'état cible.
4. Le commit remplace atomiquement le serveur, la lease, l'époque et l'état
   courant par la destination. Les écritures de l'ancienne source sont alors
   refusées. Le client bascule après réception du ghost possédé cible ; la
   présentation de son personnage et sa caméra restent conservées, puis la
   scène source est retirée.

Les routes de seal, commit, statut et abort restent celles du
[protocole de handoff](STARTER_ISLAND_HANDOFF.md#préparation-claim-seal-et-commit).
Tous les appels internes exigent `X-Arkya-Dedicated-Key`, liée au couple
realm/serveur. Le client reçoit un ticket temporaire, jamais cette clé.

Pour une traversée interne à l'Area, `JsonUtility` peut écrire
`transitionId: ""` et une enveloppe `arrivalSimulation` vide. Le backend
normalise ces seules valeurs vides en absence de portail ; les identifiants
invalides et les payloads d'arrivée non attendus restent refusés.

Le JSON de simulation reste opaque pour le backend. Le runtime conserve son
snapshot versionné complet : intentions et séquences, saut/traversée, ainsi
que les statistiques, ressources, XP et compétences déjà présentes. Le support
de collision source est invalidé pour la nouvelle carte ; la pose d'arrivée
ne remet pas à zéro les compteurs d'entrée ou le gameplay. Cette tranche ne
branche pas les combats, quêtes ou inventaires qui restent dans d'autres
parcours du projet.

Avant commit, l'état courant durable reste l'état final source. Si abort gagne
la comparaison de version, cet état est conservé et la source peut reprendre
seulement après confirmation de `Aborted`. Si commit gagne, l'annulation voit
`Committed` et ne peut pas rétablir la source. Un crash après seal et avant
commit récupère donc la carte source à l'expiration de la lease ; après commit,
la réadmission retrouve la carte cible et ses coordonnées locales.

L'admission initiale et la consommation exposent les champs optionnels
`areaId` et `contentId`. La lease conserve ce couple ; l'état persistant garde
l'identifiant logique dans son champ historique `islandId`. Les leases portant
ces métadonnées utilisent le schéma MongoDB 5 ; les schémas 1 à 4 restent
lisibles. Le compteur de révision et l'époque gardent leurs garanties CAS.

Le transfert est borné à 45 secondes, sans dépasser lease ou session. Le
chargement d'une autre carte peut être visible et immobilise le joueur côté
source : aucun délai nul ou passage imperceptible n'est promis.

## Vérifications disponibles

L'ajout du sélecteur de lancement le 24 octobre 2026 comporte des tests
déterministes de parsing, d'affectation et d'isolation des Areas, y compris
les contenus partagés et les alias ambigus : **32 cas passent, aucun échec**,
exécutés sous .NET contre les vraies assemblies compilées. Les assemblies
complètes Runtime (76 sources) et tests Editor (62 sources) compilent avec
Roslyn fourni par Unity `6000.6.6f1`, les références Unity installées et les
générateurs Entities/Netcode, avec les sorties dans un dossier local isolé.
La connexion MCP locale échouait à l'initialisation sur `127.0.0.1:8080` :
ce premier contrôle ne vérifiait pas l'import, la compilation dans l'Editor
ni un nouveau parcours dedicated/client. Le test réel ci-dessous complète
la validation du démarrage Dedicated Windows. Les autres parcours historiques
valident le chargement multi-Area antérieur, pas ce nouveau sélecteur.

### Test réel d'isolation des dedicateds — 24 octobre 2026

Le run `artifacts/dedicated-area-loading/runs/20261024-two-areas-2x2-02`
termine avec `verification.json: passed = true`. Un nouveau player **Windows
Dedicated Mono Development**, construit par Unity `6000.6.6f1`, utilise la
paire actuelle `5 / arkya-town-npcs-v1`. Le snapshot part du commit Unity
`2b36c90784d9b082b240dd4aa3fc9130692799c8`, avec uniquement le relevé de
scènes de cette tâche ajouté au runtime. `source-provenance.json` conserve
les empreintes des sources ; les builds et données des autres tâches sont
préservés. L'import batch et le build Dedicated se terminent avec le code 0.

Les quatre processus sont vivants simultanément et propriétaires de leurs
ports TCP `51135` à `51138`. Chacun ne charge que le bootstrap commun
`CharacterDedicated.unity` et sa scène d'Area :

| Serveur | PID | Sélecteur | Seule scène de carte chargée |
| --- | ---: | --- | --- |
| `island-starter-island-01` | 70780 | `StarterIsland` | `StarterIslandServer.unity` |
| `island-starter-island-02` | 10080 | `starter-island` | `StarterIslandServer.unity` |
| `island-handoff-valley-01` | 114416 | `HandoffValley` | `HandoffValleyServer.unity` |
| `island-handoff-valley-02` | 47188 | `handoff-valley` | `HandoffValleyServer.unity` |

Les inventaires proviennent de `SceneManager` dans le vrai player : bootstrap,
chaque événement `sceneLoaded`, readiness et état stable cinq secondes plus
tard. Le harness observe ensuite les processus pendant au moins dix secondes
après leur readiness et rejette toute scène étrangère dans cet historique,
y compris les scènes client du catalogue. Les quatre rapports `*-scenes.json`
et les logs bruts sont conservés dans le run.

Un cinquième processus, PID `26096`, utilise la même identité StarterIsland
avec `--area=HandoffValley`. Il sort avec le **code 1**, le message de refus
d'affectation, le bootstrap seul et aucun fichier de readiness. Tous les
processus créés par le test sont ensuite arrêtés et les clés temporaires
supprimées.

Le test est reproductible avec
[`Test-DedicatedAreaLoading.ps1`](../scripts/area-prototype/Test-DedicatedAreaLoading.ps1)
et un nouveau build Development :

```powershell
$artifactRoot = 'D:/Repositories/arkya/artifacts/dedicated-area-loading/build-20261024'
$build = Get-Content "$artifactRoot/build.json" -Raw | ConvertFrom-Json
pwsh -File scripts/area-prototype/Test-DedicatedAreaLoading.ps1 `
  -ServerExecutable $build.serverExecutable `
  -SourceProject "$artifactRoot/project-server"
```

Le relevé est activé seulement en Development/Editor par
`ARKYA_DEDICATED_SCENE_VALIDATION=1`. La fixture HTTP locale fournit uniquement
une topologie authentifiée, avec une clé distincte par dedicated ; elle ne
simule pas le chargement Unity. Ce contrôle valide le démarrage, l'affectation,
l'écoute et l'isolation des cartes. Aucun client, admission, persistance,
handoff ni déploiement public n'est exercé par ce run. MCP reste inaccessible
sur `127.0.0.1:8080` ; cette preuve provient du build batch isolé et des vrais
players, sans inspection de l'Editor principal.

### Autres validations du runtime multi-Area

Les suites complètes passent avec MongoDB de test isolé : **271 tests
Characters et 429 tests Realms**, soit 700 réussites, aucun échec ni test
ignoré. Elles couvrent les plans et métadonnées, les coordonnées locales
superposées, les portails invalides, les payloads source/cible séparés, les
retries, le fencing, la course abort/commit, le redémarrage du repository et
la réadmission dans la carte cible. Le snapshot opaque contenant des données
de gameplay est conservé intégralement par ces vérifications backend.

L'interface Admin utilise l'anglais et la culture `en-GB`, quelle que soit
la langue du navigateur. Elle permet de configurer le contenu de chaque
Area et les portails, puis conserve ces champs lors de l'édition d'un
brouillon. Les textes rédigés par les joueurs et les opérateurs restent
inchangés. La suite Admin complète compte 682 réussites et un test MongoDB
ignoré ; les contrôles ciblés des portails et des pages traduites passent.

Les 44 tests Unity ciblés de frontières d'autorité, snapshots de handoff,
portails et catalogue passent dans un Editor isolé (`6000.6.6f1`). La connexion
MCP n'exposait aucune instance de l'Editor principal ; sa validation directe
reste indisponible.

Le run `artifacts/multi-area/runs/20261023-172319-e72769e2` termine avec
`verification.json: passed = true`. Il utilise quatre **vrais processus
dedicated Windows**, deux pour Starter Island et deux pour Handoff Valley,
ainsi que deux clients Editor rendus successifs. Le premier effectue neuf
points de passage, huit transferts (cinq internes et trois portails) et cinq
reprises de saut en vol, avec quatre chargements de scène. La caméra, le
modèle, l'identité, l'apparence et le gameplay restent conservés.

Les statistiques non initiales vérifiées sont notamment vie `73/127`, mana
`41/95`, endurance `63/111`, Warrior niveau 3/XP 41, Scout niveau 2/XP 17 et
rang appris 1. Le snapshot final est sauvegardé dans Handoff Valley sur son
second dedicated, époque 10. Une réadmission API conserve exactement le
snapshot ; le second client graphique charge directement cette même carte,
avec position, orientation et gameplay restaurés, sans passer par Starter
Island. Les captures `client-completed.png` et `client-map.png` confirment les
noms d'Area corrects dans le HUD et la carte.

| Mesure du parcours Editor final | Maximum observé |
| --- | ---: |
| Immobilisation à une frontière interne | 0,232 s |
| Attente de portail, borne observée incluant la promotion finale | 4,246 s |
| Intervalle entre lectures, chargement de scène compris | 1,674 s |
| Absence de baseline gameplay effectivement observée | 0,173 s |

La présentation conserve le dernier état pendant la relève du ghost. Le
test refuse une absence de baseline dépassant une seconde et compare tous
les champs à son retour, avant de valider chaque passage. Le premier essai
complet, `20261023-170909-5c41033d`, passait aussi les huit transferts et la
reconnexion, mais son premier chargement à froid dans l'Editor bloquait le
rendu environ 20,58 secondes. Ce délai de chargement n'est pas présenté comme
un délai réseau ni comme une perte d'état. Les mesures historiques du runtime
version 2 ne servent pas de preuve pour cette version 3.

Les tickets de test sont émis seulement après une première image réellement
rendue par le client, avec un accusé de lecture de sa configuration. Le
premier démarrage à froid de l'Editor ne consomme ainsi pas leur durée de vie.
Les rapports publics restent dans le dossier du run ; les configurations et
clés temporaires sont nettoyées après l'arrêt des processus de test.

Le [script de build isolé](../scripts/area-prototype/Build-StarterIslandPartitions.ps1)
et le [harness](../scripts/area-prototype/Test-StarterIslandPartitions.ps1)
préparent le scénario avec `-MultiArea`. Exemple de parcours Editor rendu,
après inclusion des contenus Unity dans la capture :

```powershell
$editor = 'D:/Unity/6000.6.6f1/Editor/Unity.exe'
$artifactRoot = 'artifacts/multi-area/build-validation'
pwsh -File scripts/area-prototype/Build-StarterIslandPartitions.ps1 `
  -ArtifactRoot $artifactRoot -Role Server -UnityExecutable $editor
$build = Get-Content "$artifactRoot/build.json" -Raw | ConvertFrom-Json
pwsh -File scripts/area-prototype/Test-StarterIslandPartitions.ps1 `
  -MultiArea -ClientExecutable $editor `
  -EditorClientProject "$artifactRoot/project-server" `
  -ServerExecutable $build.serverExecutable
```

Le rapport doit distinguer client Editor et client natif, confirmer les quatre
PID dédiés, les deux cartes visitées, leurs transitions et la réadmission
finale dans la seconde carte. Les secrets temporaires sous `private/` restent
hors du rapport public. Le client natif, le build WebGL multi-Area et le
déploiement public ne sont pas validés par ce parcours Editor.

## Sources

- [Catalogue et validation communs](../backend/src/Arkya.ServiceDefaults/GameTopology.cs).
- [Plans et compatibilité des runtimes](../backend/src/Arkya.Realms.Api/Application/RealmTopologyBuilder.cs).
- [Transferts Characters et CAS](../backend/src/Arkya.Characters.Api/Application/GameTransfers.cs).
- [Contrats explicites d'admission et de seal](../backend/src/Arkya.Characters.Api/Contracts/GameAdmissionContracts.cs).
- [Tests backend inter-Area](../backend/tests/Arkya.Characters.Tests/AreaTransitionTests.cs).
- [Catalogue de contenus Unity](../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/Multiplayer/CharacterAreaCatalog.cs).
- [Préparation et retrait des scènes](../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/CharacterCreationFlow.cs).
- [Transfert serveur](../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/Multiplayer/CharacterServerHandoffSystems.cs).
- [Transition du client](../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/Multiplayer/CharacterRemoteSession.cs).
