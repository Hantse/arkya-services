# Prototype Unity : frontière entre deux serveurs

Ce prototype isole le passage d'autorité entre deux processus Unity sur une carte continue. Il utilise réellement Entities, des jobs Burst et Netcode for Entities pour les connexions client/serveur. Il sert à observer le déplacement d'un joueur, d'un monstre et de projectiles entre deux domaines d'autorité, ainsi que l'application unique des dégâts.

Depuis la clarification du 23 octobre 2026, une **Area** désigne une carte entière du realm et une **partition** son domaine d'autorité confié à un dedicated. Les noms historiques `AreaPrototype`, `AreaId` et « inter-area » dans ce prototype désignent ses partitions ; ses deux serveurs ne représentent pas deux cartes distinctes. Voir la [décision de vocabulaire](decisions/0003-realms-and-area-authority.md).

Les preuves effectivement obtenues sont suivies dans le [rapport de validation](AREA_PROTOTYPE_VALIDATION.md).

Le 15 octobre 2026, 56 tests Edit Mode ont réussi : 31 sur le protocole d'areas et 25 sur les règles de quête. Les deux builds Windows ont réussi, avec 0 erreur et 0 avertissement de build. Le scénario final avec deux Dedicated Servers et deux clients graphiques a passé ses **54 contrôles** : 7 transferts, 23 impacts appliqués dont 2 interzones, 10 doublons ignorés et aucun impact rejeté. Les quatre codes de sortie sont nuls, les journaux ne présentent aucune erreur runtime détectée et les captures ont été inspectées. Les intervalles maximaux entre snapshots de simulation sont de 0.4005 s et 0.4667 s ; cette mesure ne garantit pas une fluidité visuelle parfaite.

Projet : `Unity3D/Arkya/`, Unity `6000.6.6f1`. Les scènes de comparaison artistique et les assets vendor ne sont pas modifiés. Le backend Identity/Characters et le parcours de connexion Discord restent indépendants de cette expérience locale.

Les versions effectivement résolues avec cet éditeur sont Entities, Netcode for Entities, Unity Transport et Collections `6.6.0`, Burst `2.0.0` et Test Framework `1.8.0`. Unity 6.6 fournit ici ses packages intégrés ; vérifier le lock et les `package.json` résolus plutôt que supposer les versions historiques de la branche Unity 6.0.

## Carte et processus

La carte couvre 1 000 × 500 unités Unity, avec une frontière à `x = 500`. L'échelle de travail choisit une unité pour un mètre :

| Processus | Domaine d'autorité | Port client Netcode | Port pair Unity Transport |
| --- | --- | --- | --- |
| Serveur S1, `AreaId = 0` | `x < 500` | UDP `127.0.0.1:7777` | UDP `127.0.0.1:7790` |
| Serveur S2, `AreaId = 1` | `x >= 500` | UDP `127.0.0.1:7778` | UDP `127.0.0.1:7791` |
| Client 1 | Joueur `Id = 1` | Connecté aux deux serveurs | Aucun |
| Client 2 | Joueur `Id = 2` | Connecté aux deux serveurs | Aucun |

Le monstre conserve l'identifiant `100`. Les acteurs sont des données ECS ; positions, points de vie, propriétaire, époque et état de transfert sont répliqués dans `PrototypeActor`. La simulation vise 30 Hz et l'envoi des ghosts 20 Hz. Le déplacement est effectué par un `IJobEntity` compilable Burst.

Chaque processus client maintient deux mondes Netcode, un par serveur. La présentation fusionne leurs ghosts par identifiant stable, puis par époque et tick. Un acteur ne reçoit qu'un objet de présentation, conservé pendant un transfert. La caméra suit ce même identifiant et aucune scène n'est chargée lors du franchissement de la frontière.

Les clients envoient leurs intentions de déplacement et de tir aux deux connexions ; le serveur vérifie l'identité de test et sa propriété de l'acteur avant de modifier la simulation. Cette première tranche utilise des ghosts interpolés, y compris pour le joueur local ; la prédiction locale n'est pas implémentée.

## Transfert et impacts

Un serveur simule uniquement ses acteurs actifs. Il échange avec l'autre serveur des répliques pour observer les cibles proches de la frontière. Les paquets de réplique périmés sont filtrés par propriétaire, époque et séquence ; ils ne peuvent pas écraser un acteur autoritaire local.

Lorsqu'un acteur traverse la frontière, le propriétaire source le fige et prépare un état complet pour la destination, avec une époque incrémentée. La destination accuse réception de la préparation. La source libère alors son autorité et envoie le commit ; la destination active l'acteur puis confirme cette activation. La source remplace enfin sa copie autoritaire par une réplique. Les messages de transfert utilisent un canal fiable et des tentatives applicatives espacées de 250 ms.

À l'activation, le pas de mouvement suivant rattrape au maximum 0.5 seconde passée en transfert. Le test de collision du projectile couvre le segment parcouru pendant ce rattrapage. Les suppressions laissent en mémoire une époque minimale : une vieille réplique ne peut pas recréer un acteur supprimé. Un commit ou une confirmation de transfert ancien ne remplace pas une version déjà plus récente de l'entité. Ces protections restent locales au run ; elles ne constituent pas un journal durable de transferts.

Le transfert emporte l'identifiant stable, les points de vie, la position, la vitesse, la cible, les séquences d'entrée et de tir, le registre d'impacts et l'historique de position. Dans le HUD, `Phase = 0` signifie actif ; `1`, figé ; `2`, préparé ; `3`, autorité libérée. Un projectile en attente de confirmation d'impact utilise la phase `4`.

Les collisions de projectile utilisent un test de segment contre cercle dans le plan, calculé à partir des positions ECS. La présentation n'utilise pas ses colliders pour résoudre les tirs. Le propriétaire de la cible valide l'impact et applique les dégâts. Un événement conserve son identifiant lors des retransmissions ; le registre d'impacts uniques empêche son application multiple et suit la cible lors du transfert. Les événements arrivant pendant un transfert attendent l'activation ou sont acheminés au propriétaire courant.

Cette validation locale conserve un historique borné et contrôle notamment l'âge de l'événement et sa proximité géométrique. Elle n'est pas une solution complète de compensation de latence ou de reprise après partition.

## Présentation et commandes

Les formes sont volontairement simples : capsules pour les joueurs et le monstre, sphères pour les projectiles, sol continu et ligne de frontière dorée. Elles observent les ghosts reçus ; leurs transforms ne sont jamais une source de vérité pour les positions ou les collisions.

- Bleu : joueur appartenant à S1 ; vert : joueur appartenant à S2.
- Rouge : monstre ; or : projectile et frontière.
- Point blanc au-dessus de la capsule : joueur du client courant.
- `WASD` ou flèches : déplacement ; `Espace` : un ordre de tir.
- `M` : basculer entre suivi près de la frontière et vue de la carte entière.

Le HUD expose les dernières données reçues : position, propriétaire, époque, tick, phase et santé. La minicarte conserve la vue des deux domaines. Les barres de vie sont calculées à partir des valeurs serveur. Pendant les phases figée et libérée, la vue peut extrapoler la position selon la dernière vitesse pendant au maximum 0.5 seconde. Un objet peut conserver son dernier aspect pendant au maximum une seconde si ses ghosts disparaissent brièvement. Ces corrections visuelles ne maintiennent aucune simulation autoritaire locale.

En mode automatique, les commandes de scénario viennent du système client et les entrées manuelles ne les écrasent pas. Les clients sans rendu exécutent les observations et rapports sans créer la caméra ni les matériaux.

## Construire

Depuis la racine du dépôt, PowerShell 7 :

```powershell
& ./scripts/area-prototype/Build-AreaPrototype.ps1
```

`-UnityPath` permet de remplacer le chemin d'éditeur par défaut `D:/Unity/6000.6.6f1/Editor/Unity.exe` :

```powershell
& ./scripts/area-prototype/Build-AreaPrototype.ps1 -UnityPath '<chemin-vers-Unity-6000.6.6f1/Editor/Unity.exe>'
```

Par défaut, le script construit deux copies isolées, `artifacts/area-prototype/validation-prototype-client/` et `validation-prototype-server/`, avec les scripts du projet, les paramètres nécessaires et les dépendances requises par le prototype. L'éditeur ouvert sur le projet artistique peut rester actif : le build utilise d'autres dossiers et ne charge pas ses scènes. `-FullProjectPackages` conserve le manifest complet et utilise les dossiers `validation-full-client/` et `validation-full-server/` ; ce mode peut aussi importer les packages et shaders étrangers au prototype.

La copie serveur dérive du snapshot client déjà préparé, afin que les deux binaires utilisent le même protocole si d'autres travaux modifient le dépôt entre les builds. Les caches Bee et fichiers temporaires sont dirigés sous le répertoire d'artefacts. Les limites de parallélisme et variables temporaires sont appliquées à l'enfant Unity puis restaurées dans le processus appelant ; elles ne changent pas la configuration globale de la machine.

Il lance deux processus Unity successifs : le client démarre avec `-standaloneBuildSubtarget Player -arkyaBuildRole client`, puis le serveur avec `-standaloneBuildSubtarget Server -arkyaBuildRole server`. Chaque rôle dispose ainsi de son propre contexte de compilation Editor ; les définitions `UNITY_SERVER` et le type de player utilisés par Entities ne sont pas partagés accidentellement entre les deux builds. Unity documente le [sous-type Dedicated Server et son argument de lancement](https://docs.unity.com/en-us/engine/6000.6/manual/platform-specific/dedicated-server/build).

Le build utilise la seule scène `Assets/Arkya/AreaPrototype/Scenes/AreaPrototype.unity`. L'outil Editor la crée si elle n'existe pas, ajoute la référence de shader nécessaire aux formes de présentation et restaure les backends et le sous-type de build précédents. Les deux sorties Windows Mono de développement sont :

```text
artifacts/area-prototype/build/Arkya.AreaPrototype.exe
artifacts/area-prototype/build/server/Arkya.AreaPrototype.Server.exe
```

La seconde sortie utilise le sous-type Unity Dedicated Server et nécessite le module Windows Dedicated Server de cet éditeur. Le lanceur utilise ce binaire pour les serveurs lorsqu'il existe ; en son absence, il conserve un mode de secours avec le player et `-batchmode -nographics`. `run.json` enregistre le chemin réellement lancé pour chaque processus, ce qui permet de distinguer ces deux cas lors d'une vérification.

Après réussite des deux builds seulement, le script récupère les nouveaux assets du prototype générés par Unity (`.unity`, `.mat`, `.meta`) sans écraser ceux déjà présents. Le lock de packages n'est recopié vers le projet source qu'en mode `-FullProjectPackages`. Le script ne recopie pas les dossiers `Library` ou les autres caches vers le projet source.

Options complémentaires de construction :

| Option | Effet |
| --- | --- |
| `-ArtifactRoot <path>` | Déplacer l'ensemble des sorties et des copies de validation |
| `-CleanBuild` | Demander `BuildOptions.CleanBuildCache` pour chaque rôle, afin de reconstruire le cache du build |
| `-LowMemory` | Passer `-diag-debug-shader-compiler` à Unity pour limiter la compilation des shaders à un seul processus ShaderCompiler |

Par exemple, pour recompiler proprement sur une machine contrainte :

```powershell
& ./scripts/area-prototype/Build-AreaPrototype.ps1 -CleanBuild -LowMemory
```

Le script exige `success = true` et `cleanBuild = true` pour chaque rôle. Un résumé Unity `Succeeded` accompagné de `buildErrors > 0` fait donc échouer la construction ; il ne suffit pas à autoriser le test runtime.

Les preuves du build se trouvent dans `artifacts/area-prototype/` :

| Fichier | Contenu |
| --- | --- |
| `unity-build-client.log` / `unity-build-server.log` | Journal de chaque invocation Unity |
| `client-build-response.json` / `server-build-response.json` | Réponse du rôle concerné |
| `build-response.json` | Résultat agrégé : `success`, `cleanBuild`, erreurs, avertissements, `outputPath`, `serverOutputPath` |

`editor-response.json` est la réponse intermédiaire la plus récente ; utiliser les fichiers de rôle et l'agrégat pour apprécier la paire de builds.

La boîte aux lettres de l'éditeur ouvert reste disponible pour construire un seul rôle, si les scripts y sont déjà importés. `command = 'build'` sans rôle construit le client uniquement ; `role = 'client'` ou `role = 'server'` sélectionne explicitement la sortie. Pour obtenir la paire avec ses contextes de compilation distincts, utiliser le script ci-dessus. Exemple de demande client, depuis la racine du dépôt :

```powershell
$mailbox = Join-Path (Get-Location) 'artifacts/area-prototype'
New-Item -ItemType Directory -Path $mailbox -Force | Out-Null
@{ requestId = [Guid]::NewGuid().ToString('N'); command = 'build'; role = 'client' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $mailbox 'editor-request.tmp') -Encoding utf8
Move-Item -LiteralPath (Join-Path $mailbox 'editor-request.tmp') -Destination (Join-Path $mailbox 'editor-request.json')
```

L'éditeur traite la demande hors Play Mode, une fois les imports et compilations terminés. Le résultat se trouve dans `artifacts/area-prototype/editor-response.json` : vérifier `success`, `compilationErrors`, `buildErrors` et `outputPath`. `command = 'status'` permet de lire le contexte de l'éditeur sans lancer de build.

Pour un projet qui n'est pas déjà ouvert, le même point d'entrée existe en batch :

```powershell
& '<chemin-vers-Unity-6000.6.6f1/Editor/Unity.exe>' -batchmode -quit `
    -projectPath '<chemin-absolu-vers-Unity3D/Arkya>' `
    -standaloneBuildSubtarget Player -arkyaBuildRole client `
    -executeMethod Arkya.AreaPrototype.Editor.AreaPrototypeEditorMailbox.BuildFromCommandLine `
    -logFile '<chemin-absolu-vers-le-log-de-build>'
```

Cet exemple construit uniquement le client. Pour le rôle serveur, les deux valeurs deviennent `Server` et `server`. Remplacer les chemins entre chevrons. Ne pas ouvrir en batch le même dossier projet qu'un éditeur actif : Unity protège son projet par un verrou.

## Lancer les deux serveurs et les deux clients

Depuis la racine du dépôt :

```powershell
$run = & ./scripts/area-prototype/Start-AreaPrototype.ps1 -DurationSeconds 120
$run.RunDirectory
```

Le script lance deux serveurs sans fenêtre et deux clients visibles en 960 × 600. Il crée un identifiant de run commun, une origine temporelle commune et un dossier de résultats unique. Les quatre processus quittent à la fin de la durée demandée. Utiliser une seule exécution à la fois : les ports sont fixes.

Le lanceur fonctionne avec Windows PowerShell 5.1 et PowerShell 7. Il retire les clés de service et les identifiants de personnage de l'environnement des clients uniquement ; l'environnement du terminal et celui des serveurs sont conservés.

En automatique, le départ commun est prévu 20 secondes après le lancement pour laisser démarrer les quatre processus ; en manuel, l'avance par défaut est de 8 secondes. `-StartupLeadSeconds` permet de choisir cette avance. L'attente observée par chaque client avant la simulation est conservée dans `InitialSimulationWaitSeconds`.

Le lanceur refuse de démarrer si l'un des ports du prototype est déjà occupé. Le script de build refuse aussi de remplacer ses binaires tant que des processus utilisant ces sorties sont encore actifs. Le nettoyage du vérificateur cible uniquement ses propres processus, en comparant PID, heure de démarrage et chemin du binaire.

Le lanceur accepte notamment :

| Option | Effet |
| --- | --- |
| `-ExecutablePath <path>` | Utiliser un autre binaire client |
| `-ServerExecutablePath <path>` | Utiliser un autre binaire serveur ; sinon préférence au Dedicated Server local puis au player |
| `-DurationSeconds 45` | Durée de l'exercice, de 10 à 600 secondes |
| `-StartupLeadSeconds <seconds>` | Avance avant le départ commun ; `0` choisit automatiquement 20 s en scénario automatique et 8 s en manuel |
| `-Automated` | Commandes et cibles de scénario reproductibles |
| `-HeadlessClients` | Clients sans fenêtre ni rendu |
| `-GraphicsApi Direct3D11` | API des clients graphiques : `Direct3D11` par défaut, `Direct3D12` ou `Automatic` également possibles |
| `-PeerDelayMs 50` | Ajouter 50 ms à la réception de chaque message entre serveurs |
| `-DuplicateImpacts` | Réémettre des messages d'impact pour éprouver la déduplication |
| `-OutputRoot <directory>` | Choisir le répertoire parent des résultats |

Le délai est une injection applicative sur le lien entre les deux serveurs ; il ne simule pas toutes les propriétés d'un réseau étendu, notamment la gigue ou une partition.

## Vérifier le scénario

Le test lance les quatre processus, attend leur arrêt puis confronte leurs rapports :

```powershell
$result = & ./scripts/area-prototype/Test-AreaPrototype.ps1
$result.Passed
```

Par défaut : clients sans rendu, scénario automatique de 45 secondes, 50 ms de délai entre serveurs et impacts dupliqués. Pour observer les fenêtres pendant cette vérification :

```powershell
$result = & ./scripts/area-prototype/Test-AreaPrototype.ps1 -VisibleClients
```

Le joueur 1 démarre à `(480, 250)`, le joueur 2 à `(500.6, 250)` et le monstre à `(470, 255)`. Les temps sont relatifs à l'origine commune du run. Le joueur 1 avance vers la droite entre 5 et 17 secondes, puis revient vers la gauche entre 20 et 32 secondes ; il tire environ toutes les deux secondes entre 1 et 32 secondes. Le joueur 2 avance vers la droite entre 3 et 7 secondes, vers `x ≈ 532.6`, puis s'arrête.

Cette position initiale place une partie du volume de collision du joueur 2 à gauche de la frontière : avec un rayon de collision de 1.2 unité, un premier impact peut traverser le lien serveur avant le transfert du projectile lui-même. Son déplacement ultérieur laisse ensuite aux autres projectiles le temps de changer de propriétaire avant l'impact. Le monstre poursuit sa cible et permet d'observer son propre transfert. Le vérificateur exige :

- Progression des deux simulations et vidage des transferts et impacts en attente.
- Concordance du nombre de transferts sortants et entrants et un propriétaire autoritaire final par identifiant.
- Au moins un impact interzone et au moins un doublon d'impact ignoré.
- Aucun impact légitime rejeté dans ce scénario contrôlé (`RejectedImpacts = 0`).
- Santé cohérente avec le registre d'impacts uniques, avec 100 points initiaux et 10 points par impact.
- Un projectile à impact unique ne touchant pas deux cibles.
- Sur chaque client, connexion aux deux serveurs, une seule scène chargée et observation des transferts de joueur, de monstre et de projectile.
- Absence de doublons de présentation et intervalle maximal entre deux mises à jour reçues ne dépassant pas une seconde.
- Codes de sortie nuls et absence d'enregistrement d'exception, d'assertion ou d'erreur dans les quatre journaux runtime.

Le script produit `verification.json`, renvoie son résultat et échoue si un contrôle ne passe pas. Il peut aussi relire une exécution terminée sans relancer de processus :

```powershell
& ./scripts/area-prototype/Test-AreaPrototype.ps1 -RunDirectory '<dossier-du-run>'
```

Les rapports et journaux se trouvent dans `artifacts/area-prototype/runs/<date-identifiant>/` : `run.json`, `server-0.json`, `server-1.json`, `client-1.json`, `client-2.json`, les quatre logs, `process-exits.json` et `verification.json`. Ce dernier inclut le diagnostic `RuntimeLogs` et les lignes pertinentes en cas d'erreur. Les simples avertissements de shader et traces ordinaires Netcode ne sont pas assimilés à une exception runtime.

Un run automatique avec clients graphiques demande aussi deux captures par client : `client-1-crossing.png` et `client-2-crossing.png` à partir de la douzième seconde, puis `client-1-return.png` et `client-2-return.png` à partir de la vingt-huitième seconde. Elles contiennent la carte, les acteurs et le HUD de l'image rendue. La seconde capture intervient pendant le trajet de retour, avant le franchissement effectif vers la seconde 30 ; l'aller-retour complet est confirmé par les époques et propriétaires finaux. Les clients sans rendu ne produisent pas ces fichiers.

Les clients écrivent leur rapport atomiquement chaque seconde puis à la destruction de la présentation. `MaxPresentationGapSeconds` mesure l'intervalle entre deux changements de tick ou d'époque reçus pour un même acteur après sa première snapshot de simulation ; il exclut l'attente intentionnelle initiale, enregistrée séparément dans `InitialSimulationWaitSeconds`. Cette mesure ne décrit pas la fluidité GPU. `SceneLoads` compte le chargement initial et les chargements suivants. `DuplicateVisuals` est le maximum de racines de présentation supplémentaires observées pour un même identifiant.

## Limites

Il s'agit d'une preuve locale de séparation d'autorité, avec deux domaines fixes et deux identités de client de test. Les liaisons écoutent uniquement sur loopback. Le marqueur de run commun écarte des paquets d'une autre exécution ; il ne remplace pas l'authentification de service ou l'admission d'un joueur.

La reprise après crash, le transfert durable en cas de partition, les sauvegardes MongoDB, les tickets d'admission, la prédiction client, la gestion d'intérêt à grande échelle et les déplacements entre machines ne sont pas couverts. Le rattrapage et l'extrapolation bornés ne sont pas une compensation de latence complète. La santé, les époques de suppression et les registres d'impacts restent en mémoire. Le protocole doit évoluer avant tout hébergement public.

Les deux areas représentent des portions d'un même univers de test, pas deux realms. Le [modèle de realms](decisions/0003-realms-and-area-authority.md) prévoit des personnages rattachés à un realm ; le service Realm, son catalogue et le champ persistant correspondant ne sont pas implémentés ici.

Un test sans rendu démontre les observations réseau et les invariants contrôlés dans les rapports ; il ne remplace pas une inspection des fenêtres clientes. Les résultats de compilation et d'exécution doivent être rapportés séparément d'une validation visuelle.
