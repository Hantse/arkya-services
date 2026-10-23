# StarterIsland : passage automatique entre dedicateds

Le runtime de déplacement des joueurs dispose d'un transfert entre partitions
d'une même île. Characters conserve une seule lease et une seule époque
d'autorité par personnage ; le dedicated cible devient propriétaire par une
écriture atomique MongoDB. Le client prépare sa seconde connexion et conserve
le personnage présenté pendant le changement de monde Netcode.

La traversée de quatre exécutables dedicated réels a réussi le 23 octobre
2026, avec un client Unity Editor rendu dans une capture isolée du projet.
Elle confirme six handoffs et une réadmission réelle avec l'état persistant.
Le client natif exporté et le déploiement public restent non validés. Les
mesures de ce run incluent un arrêt maximal de mouvement de 0,42548 seconde
près d'une frontière : elles ne garantissent pas l'absence d'interruption perceptible.
Les images et routes publiques existantes ne sont pas promues automatiquement.

## Découpage et déplacement

Le catalogue Realms fournit les partitions autorisées, leurs endpoints, leur
`islandId`, leur indice et leurs bornes X/Z. Le dedicated récupère ce catalogue
par `GET /internal/game/topology`, authentifié avec sa clé propre. Cette lecture
est disponible avant la readiness afin de permettre le démarrage de tous les
processus ; l'admission d'un joueur et la préparation d'un transfert exigent
ensuite une destination prête.

Le plan générique de 1 000 × 1 000 avec quatre dedicateds forme une grille
2 × 2 de cellules de 500 × 500. La fixture des vrais exécutables utilise le
terrain StarterIsland actuel, de 1 536 × 1 536, soit quatre cellules de
768 × 768. Les indices progressent en X puis en Z. Une frontière intérieure
appartient à la cellule du côté positif ; le coin central appartient à la
quatrième cellule. Le bord extérieur maximal de l'île reste inclus.

Le serveur détecte la destination voisine à partir du déplacement autoritaire.
Le passage par un coin peut choisir directement la partition diagonale. En
attendant que la cible soit préparée, le mouvement reste borné au domaine
source ; le client ne choisit ni son propriétaire ni sa position finale. Le
serveur capture le dernier état au moment de sceller le transfert.

## Préparation, claim, seal et commit

Tous les appels ci-dessous appartiennent à `/internal/game`, avec
`X-Arkya-Dedicated-Key`. La clé est liée au couple realm/serveur et reste dans le
processus serveur. Le client reçoit seulement un ticket temporaire lié au
transfert et à sa destination.

| Phase | Appel | Autorité et état |
| --- | --- | --- |
| Préparation | `POST /leases/{characterId}/transfers/prepare` | La source conserve sa lease et peut encore simuler/sauvegarder. L'identifiant du transfert, sa destination, sa version de topologie et le ticket haché sont persistés. |
| Claim | `POST /transfers/claim` | La cible reçoit le personnage et un état de préparation, avec une lease vide. Elle prépare la connexion sans créer une seconde autorité. |
| Seal | `POST /leases/{characterId}/transfers/{transferId}/seal` | Après préparation de la cible et arrivée à la frontière, la source suspend l'acteur et sauvegarde son état final avec la révision attendue. Les sauvegardes et libérations ordinaires de cette source sont alors refusées. |
| Commit | `POST /leases/{characterId}/transfers/{transferId}/commit` | Une comparaison de version remplace le serveur propriétaire et la lease, puis incrémente l'époque. La cible reçoit l'état final scellé et peut activer l'acteur. |

`GET /leases/{characterId}/transfers/{transferId}` permet aux deux serveurs
concernés de connaître l'état durable : `Prepared`, `Claimed`, `Sealed`,
`Committed` ou `Aborted`. La source retire son acteur après avoir constaté le
commit et ne libère pas la nouvelle lease du dedicated cible. La présentation
cliente reconnaît l'identité persistante du personnage et l'époque d'autorité ;
le compteur de séquence des entrées et du saut est transmis à la connexion
cible.

Chaque phase possède des retries idempotents. Le commit et l'annulation
concurrents utilisent le même document/version : un seul peut gagner. Les
anciennes écritures, heartbeats et libérations sont refusés après le changement
de propriétaire. Un ticket de transfert ne peut pas être réutilisé par la
route d'admission ordinaire, même après la libération de la lease cible.

Le document conserve le dernier transfert : un nouveau `prepare` peut remplacer
le statut du transfert précédent avant que l'ancienne source ne le lise.
Un `409` sur cette lecture ne prouve donc pas à lui seul la perte de lease.
L'ancienne source vérifie son bail par heartbeat : un `409` sur ce bail confirme
qu'elle doit retirer son ancien acteur sans sauvegarde ni libération ; une
erreur de dépendance n'est pas cette preuve. Une source encore propriétaire,
même scellée, reçoit un heartbeat valide. Un retour rapide A → B → A conserve
la progression des époques 1 → 2 → 3 : le claim de retour prédit l'époque 3
mais garde une lease vide jusqu'au commit.

Le transfert expire au plus après 45 secondes, sans dépasser la lease ou la
session. `POST /leases/{characterId}/transfers/{transferId}/abort`, appelé par
la source avec sa lease initiale, autorise la reprise seulement si son résultat
durable est `Aborted`. Un résultat `Committed` impose de retirer l'acteur source.
Après un crash et l'expiration du bail, une nouvelle admission retrouve la
partition de la position persistée. Un état déjà scellé reste disponible même
si le processus source disparaît avant le commit. Ces récupérations ne
remplacent pas une reprise instantanée d'un processus arrêté.

## État réellement transféré

Le snapshot conserve la position, l'orientation et la révision de l'état, ainsi
que `simulation = { schemaVersion: 1, payload: "..." }`. Le payload JSON est
produit et validé par le dedicated, puis conservé intégralement par Characters.
Il contient les données actives du joueur admis :

- mouvement présenté, intention X/Z et course ;
- dernière séquence d'entrée, séquence de saut et âge relatif de l'entrée ;
- vitesse verticale, séquence de saut consommée et phase de traversée ;
- support de collision et hauteur de support.

L'âge de l'entrée est relatif, car les horloges de simulation de deux processus
ne commencent pas ensemble. La restauration n'accorde pas un nouveau saut et
ne réinitialise pas les compteurs réseau. Les sauvegardes périodiques portent
aussi cette enveloppe. Les leases avec cet état ou un transfert utilisent le
schéma MongoDB 4 ; les leases historiques restent lisibles.

L'acteur distant de ce parcours ne porte actuellement aucun composant de santé,
de combat, d'inventaire ou de quête. Ce transfert ne prétend donc pas migrer ces
systèmes du prototype gameplay. Une extension de l'acteur devra étendre et
tester explicitement son snapshot. Les PNJ, le combat entre partitions, la
réplication d'intérêt entre voisins et l'allocation d'instances/raids restent
des travaux distincts.

## Protocole et exploitation

Le nouveau client et le dedicated utilisent `protocolVersion = 2` et
`buildId = arkya-partition-handoff-v1`. Les API conservent la paire historique
`1 / arkya-web-beta-v1` pour les images déjà sélectionnées. Une demande
d'admission doit correspondre exactement à la paire du realm ; un ancien
client ne reçoit pas un ticket vers un runtime de transfert incompatible.
Les pins de production et le frontend historique restent inchangés.

Le profil Realms doit déclarer `TopologyTemplateServerId`, les versions exactes
du serveur modèle et un `TopologyPublicEndpointTemplate` WebSocket distinct
par partition. Le flag figé `topologyRuntimeSupported` seul ne suffit pas : la
demande de déploiement et le déployeur revérifient aussi la paire réseau et le
type de chaque processus avant tout appel Kubernetes.

La version 2 actuelle exécute uniquement des domaines `Island`. Un plan avec
`instanceDedicatedCount` ou `raidDedicatedCount` non nul reste enregistrable,
mais son flag de support est faux et son déploiement est refusé avec
`topology_runtime_unsupported`. Ces emplacements attendent leur runtime,
leur allocateur et leurs règles d'entrée propres.

Characters accepte des clés explicitement déclarées dans
`GameAccess:DedicatedServers`, ou une liste finie dérivée d'un secret commun :

```json
{
  "RealmRuntime": {
    "RealmId": "beta-web",
    "DedicatedKeySeed": "<secret injecté hors du dépôt>",
    "ServerIds": [
      "island-starter-island-01",
      "island-starter-island-02",
      "island-starter-island-03",
      "island-starter-island-04"
    ]
  }
}
```

La liste doit reprendre les identifiants du plan figé, sans wildcard, doublon
ni identité implicite ; elle accepte de 1 à 256 serveurs. Elle ne se combine
ni avec `RealmRuntime:ServerId`, ni avec des clés dedicated explicites. Sans
`ServerIds`, le chemin historique `ServerId` reste compatible. Le seed reste
dans les services ; chaque dedicated reçoit seulement sa clé dérivée.

Les endpoints publics de chaque partition, l'upgrade WSS, les certificats et
les routes d'ingress doivent être provisionnés. Les ports dedicated et les API
`/internal/game` restent privés ; les seuls messages de jeu du client passent
par ses connexions Netcode autorisées. La fixture locale autorise explicitement
HTTP/WS sur loopback. Une entrée standalone de test peut déclarer les mêmes
`partitions` et `topologyVersion` que le catalogue des realms déployés.

## Construire et vérifier les quatre processus

Les outils prennent un snapshot isolé du projet Unity sous `artifacts/`, avec
ses empreintes de sources. Le script peut construire les rôles Windows client
et dedicated depuis cette capture. Le parcours attesté ci-dessous construit
le dedicated et utilise un second Unity Editor rendu pour le client ; l'Editor
ouvert sur le projet de travail reste indépendant.

```powershell
$editor = 'D:/Unity/6000.6.6f1/Editor/Unity.exe'
$artifactRoot = 'artifacts/starter-island-partitions/build-validation'
pwsh -File scripts/area-prototype/Build-StarterIslandPartitions.ps1 `
  -ArtifactRoot $artifactRoot -Role Server -UnityExecutable $editor
$build = Get-Content "$artifactRoot/build.json" -Raw | ConvertFrom-Json
pwsh -File scripts/area-prototype/Test-StarterIslandPartitionRules.ps1 `
  -ProjectPath "$artifactRoot/project-server" -UnityExecutable $editor
pwsh -File scripts/area-prototype/Test-StarterIslandPartitions.ps1 `
  -ClientExecutable $editor `
  -EditorClientProject "$artifactRoot/project-server" `
  -ServerExecutable $build.serverExecutable
```

Sans `-ArtifactRoot`, le [script de build](../scripts/area-prototype/Build-StarterIslandPartitions.ps1)
crée un dossier unique `build-<timestamp>-<guid>`. L'exemple fixe un dossier
pour retrouver sans ambiguïté son `build.json`. Le [test des règles de partition](../scripts/area-prototype/Test-StarterIslandPartitionRules.ps1)
exécute `CharacterHandoffStateTests` et `CharacterAuthorityBoundaryTests` dans
la capture isolée, une fois son build terminé. Il conserve le XML, le log et
`partition-rules-summary.json`.

Le [test à quatre processus](../scripts/area-prototype/Test-StarterIslandPartitions.ps1)
accepte `-EditorClientProject` vers une capture sous `artifacts/` qui n'est
pas déjà ouverte. Dans ce mode, `-ClientExecutable` désigne Unity.exe. Le
[helper Editor](../Unity3D/Arkya/Assets/Arkya/Characters/Editor/CharacterPartitionEditorValidation.cs)
ouvre la scène de création, entre en Play Mode, maintient la Game View et la
boucle de jeu actives, puis quitte après le rapport. Le client est rendu : le
script ne lui passe pas `-nographics`. Les diagnostics de démarrage sont
archivés dans `client-startup.json`.

`-Role Client` construit séparément le client natif ; sans
`-EditorClientProject`, le test utilise l'exécutable fourni dans
`-ClientExecutable`. Cette variante native n'est pas validée par le run
attesté : sa construction a été arrêtée. Le `clientExecutable` de `build.json`
est un chemin de sortie prévu, et ne prouve pas à lui seul qu'un client a été
construit, notamment après `-Role Server`.

`-RefreshSource` capture une nouvelle révision ; conserver les deux rôles sur
la même capture avant de comparer leurs résultats. Le test exige un vrai
`mongod` local : fournir `-MongodExecutable` si le binaire attendu n'est pas
présent, ou `-MongoUri` vers un MongoDB de test loopback existant. Il prépare
de vraies instances Identity, Characters et Realms, quatre identités dedicated,
une sauvegarde près de la frontière et les tickets de joueurs. Il lance quatre
processus dedicated et un client qui traverse les quatre domaines.

Le dossier de run conserve `processes.json`, `build-artifacts.json`, les logs,
les observations de lease, `client-report.json`, les captures et les
vérifications de persistance et de reconnexion. `verification.json` indique
le mode client (`Editor` ou `Native`) et doit confirmer quatre dedicateds
effectivement lancés, quatre serveurs visités et au moins quatre handoffs.
Les fichiers de la fixture sous `private/` contiennent des secrets temporaires
et ne doivent pas être publiés avec le rapport. Un run existant n'est pas
écrasé ; `-KeepProcesses` laisse explicitement ses processus pour investigation.

## Retrouver l'implémentation

- [GameTransfers.cs](../backend/src/Arkya.Characters.Api/Application/GameTransfers.cs) : préparation, claim, seal, commit et annulation, avec contrôle du propriétaire et de l'époque.
- [CharacterServerHandoffSystems.cs](../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/Multiplayer/CharacterServerHandoffSystems.cs) : préparation de la cible, gel du snapshot et nettoyage du joueur source.
- [CharacterRemoteSession.cs](../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/Multiplayer/CharacterRemoteSession.cs) : connexion cliente de transition et conservation du personnage présenté.
- [Tests backend du transfert](../backend/tests/Arkya.Characters.Tests/GameTransferTests.cs) : retries, timeouts, fencing et retour rapide A → B → A, en mémoire et avec MongoDB.

## Preuves au 23 octobre 2026

| Vérification | État attesté |
| --- | --- |
| Backend Characters et Realms, MongoDB réel | 516 tests passés : Characters 217 et Realms 299, aucun échec ni test ignoré, y compris les pools et le retour rapide A → B → A. |
| Unity ciblé dans l'Editor | Dernière sélection de règles : 31 tests passés, 0 échec. Les sélections précédentes de 92 et de 28 tests se recouvrent ; elles ne sont pas additionnées. |
| Fixture réelle HTTP à quatre destinations | Préflight réussi : catalogue dédié authentifié, protocole 2, quatre bornes, sauvegarde et libération MongoDB. |
| Quatre dedicateds construits et client Unity Editor rendu | Réussi : 4 processus dedicated et 1 processus Editor distinct, 4 serveurs visités, 7 waypoints, 6 handoffs dont 2 en l'air. |
| Persistance et réadmission réelles | Époques 2 → 8 pendant la traversée, révision persistée 8, bail final libéré ; réadmission à l'époque 9 avec position, orientation et simulation complète conservées, puis libération. |
| Client natif exporté | Non validé : sa construction a été arrêtée ; le résultat Editor n'atteste pas ce binaire. |
| Déploiement public / parcours WebGL de cette nouvelle paire | Non attesté par ces contrôles locaux. |

Le run réussi est
[`20261023-104828-5acba6d8/verification.json`](../artifacts/starter-island-partitions/runs/20261023-104828-5acba6d8/verification.json).
Ses [processus](../artifacts/starter-island-partitions/runs/20261023-104828-5acba6d8/processes.json)
et [empreintes de binaires et assemblies](../artifacts/starter-island-partitions/runs/20261023-104828-5acba6d8/build-artifacts.json)
distinguent les quatre exécutables serveur du client Unity Editor ; les
empreintes de la capture initiale sont dans
[`source.json`](../artifacts/starter-island-partitions/build-clean/source.json).
Ces artefacts locaux sont ignorés par Git et doivent être conservés avec le
rapport si la preuve est partagée.

Le [rapport client](../artifacts/starter-island-partitions/runs/20261023-104828-5acba6d8/client-report.json)
confirme une connexion conservée, une identité, une présentation et une
apparence stables. Il compte un chargement initial de scène et aucun
rechargement pendant les handoffs. Sur ce run de 71,27 secondes, l'intervalle
maximal entre snapshots lus est de 0,28941 seconde, le blocage de mouvement
maximal mesuré près d'une frontière de 0,42548 seconde et le déplacement
maximal entre deux lectures de position de 1,33610 mètre. Ce dernier chiffre
mesure un pas entre échantillons, pas une erreur de correction réseau isolée.
Ces mesures attestent un passage automatique sans reconnexion manuelle ni
recréation de la présentation ; elles ne promettent pas zéro interruption
perceptible.

Les vérifications de [l'état persisté](../artifacts/starter-island-partitions/runs/20261023-104828-5acba6d8/persisted-state.json)
et de [la réadmission](../artifacts/starter-island-partitions/runs/20261023-104828-5acba6d8/relogin-state.json)
passent sur les vrais services et MongoDB. La réadmission vérifie l'admission
et la consommation d'une nouvelle lease avec l'état conservé ; elle ne lance
pas une seconde traversée graphique. Les captures
[pendant le parcours](../artifacts/starter-island-partitions/runs/20261023-104828-5acba6d8/client-crossing.png)
et [en fin de parcours](../artifacts/starter-island-partitions/runs/20261023-104828-5acba6d8/client-completed.png)
complètent les mesures.

Deux essais antérieurs à froid ont échoué avant le premier snapshot possédé ;
leurs rapports restent dans les runs `20261023-102438-5992a3dc` et
`20261023-103449-f67012b8`. Le résultat final est celui du run identifié
ci-dessus avec le helper Editor de validation.
