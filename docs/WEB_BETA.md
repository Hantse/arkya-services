# Bêta web — client Unity et île dédiée

Le parcours implémenté est `connexion Discord sur le site → session Arkya → sélection du realm/personnage → ticket d’admission → île sur un dedicated Unity`. Les appels Identity, Characters et Realms utilisent les vrais services et MongoDB. Le client web ne crée pas de serveur local et ne propose pas d’entrée hors ligne. Ce guide décrit le code et sa préparation au déploiement ; il ne constitue pas une confirmation de mise en production.

Le [contrat HTTP version 1](WEB_BETA_CONTRACT.md) conserve les DTO du parcours historique. Le [handoff StarterIsland version 2](STARTER_ISLAND_HANDOFF.md) ajoute le passage du joueur entre partitions, ses API privées et ses preuves de validation. La [configuration backend](../backend/README.md), le [guide du site](../sites/arkya/README.md) et le [déploiement des realms](REALM_DEPLOYMENT.md) complètent ce document.

L'atelier actuel affiche « Un visage. Une histoire. », avec les onglets
**Apparence** et **Nom & aventure**, les vues portrait/corps entier et un
cadrage adapté au panneau disponible. Les palettes incluent `magenta` pour
les cheveux et `jade` pour les étoffes ; Identity ne possède pas ces choix.
Le client, le catalogue Characters et le dedicated doivent tous accepter
ces identifiants avant l'activation publique de cette version.

Le manifest WebGL expose `sourceRevision` lors d'un build CI. Ce SHA permet
de reconnaître le client réellement servi ; `buildId` conserve son rôle
de contrat réseau. Dans l'installation Admin à une seule source Argo, les
images Unity publiées doivent être sélectionnées ensemble dans le composant
[client statique](../backend/deploy/gitops/web-client-control/README.md) et
le profil dedicated. La release versionnée décrite dans le guide Realms
met à jour les realms déjà créés ; changer seulement le profil ne suffit pas.

## Parcours et autorité

1. Le site vérifie sa session par `GET /v1/auth/me`, charge le vrai build et transmet la session en mémoire à `ArkyaWebSession.AcceptSiteSession`. La remise ne contient ni jeton Discord ni secret de service. Unity vérifie également la session auprès d’Identity.
2. `CharacterCreationScreen` lit les personnages et le catalogue des realms. Characters vérifie le propriétaire, le realm et la paire réseau exacte de la destination avant de délivrer un ticket opaque valable au plus 60 secondes. Les API acceptent le parcours historique `1 / arkya-web-beta-v1` et le nouveau runtime `2 / arkya-partition-handoff-v1` ; les pins WebGL déjà publiés restent historiques.
3. `CharacterCreationFlow` charge l’île de façon additive, conserve l’UI et sa session, puis ouvre uniquement le client Netcode vers l’endpoint WSS reçu. Un retour ou une déconnexion pendant le chargement annule cette entrée. L’expiration du ticket impose une nouvelle demande ; elle ne déclenche aucun fallback local.
4. `CharacterAdmissionRpc`, de type `IApprovalRpcCommand`, transmet le ticket avant l’entrée en jeu. Le dedicated appelle `/internal/game/admissions/consume`. Il ajoute `ConnectionApproved` seulement après validation, puis crée le ghost lorsque Netcode attribue le `NetworkId`.
5. Le ghost reçoit l’identité et l’apparence retournées par Characters. `GhostOwner` associe la connexion à son acteur ; les RPC d’entrée ne portent aucune position ni identité de cible. Le serveur applique les séquences, bornes, déplacements, sauts, nage et collisions existants. Le client présente son acteur et les autres ghosts interpolés ; seuls l’acteur possédé et sa caméra reçoivent les entrées locales.

Les fichiers statiques du jeu restent téléchargeables : la protection effective du monde est l’admission serveur, indépendamment de l’interface du site. Le Social SDK Discord desktop n’est pas utilisé pour authentifier le client web.

## Construire les exécutables

Projet : `Unity3D/Arkya/`, Unity `6000.6.6f1`, modules Web et Linux Dedicated Server. L’éditeur doit être en Edit Mode, sans compilation en cours. Les méthodes publiques suivantes appartiennent à `Arkya.Characters.Editor.CharacterWebBetaBuilder` et sont accessibles par le menu **Arkya → Web beta** :

| Méthode | Sortie relative au projet Unity | Contenu |
| --- | --- | --- |
| `BuildWebClient()` | `Builds/WebBeta/` | Création de personnage et île, client WebGL |
| `BuildLinuxServer()` | `Builds/WebBetaServerLinux/ArkyaServer.x86_64` | Dedicated Linux, backend Mono |
| `BuildWindowsServer()` | `Builds/WebBetaServerWindows/ArkyaServer.exe` | Dedicated Windows pour validation locale |

Le client web produit un build Release (`BuildOptions.None`) pour conserver la compression ; les dedicated restent en Development pour cette bêta. Le web utilise WebGL, sans threads, une mémoire initiale de 512 Mio et maximale de 2 Gio, gzip avec **Decompression Fallback**, cache de données et stripping bas. Le builder exclut uniquement l’archive native WebGL TextMate d’App UI, inutilisée par le jeu et compilée pour une ancienne ABI Emscripten, pendant cet appel de build. Il rétablit le filtre dans un `finally`, sans modifier les fichiers ni les réglages d’import du package, et retire l’ancien contournement `--bind`. Il écrit `build-config.json` avec les noms réels des fichiers Unity ; ne pas le remplacer par des noms supposés.

Le même build web Release peut tester les vraies API HTTP et un dedicated WS sur loopback uniquement lorsqu’il est lui-même servi depuis une page HTTP(S) loopback. La décision utilise `Application.absoluteURL` ; un domaine public exige toujours HTTPS/WSS, même si la session reçue propose une URL localhost. L’option moteur d’accès HTTP permet ce test local, et les validations applicatives imposent ces restrictions.

`CharacterWebTextureProcessor` réduit les grosses textures incorporées aux GLB uniquement pendant le build web : copies temporaires des textures et matériaux, taille maximale de 2048 pixels pour les sources 4K et de 1024 pixels pour les autres sources concernées, compression DXT5 avec mipmaps. Il conserve les palettes minuscules, les UV, les normales et les géométries ; les assets sources et les builds natifs restent intacts. Les tests couvrent aussi la lecture des textures incorporées lorsque leur import Unity interdit la lecture CPU.

Le builder web active temporairement `IncludeAdditionalRPAssets.includeReferencedInScenes`, puis rétablit sa valeur initiale. L’île charge son propre pipeline à l’exécution : ce recensement permet au stripping URP de conserver ses ressources, notamment celles du SSAO, même si ce pipeline n’est pas associé à un niveau de qualité Web.

URP 17.6 crée aussi sa passe FSR EASU lors de l’initialisation du post-traitement, même lorsque ce filtre n’est pas utilisé. Son shader requiert des capacités absentes de WebGL et peut produire un avertissement « Edge Adaptive Spatial Upsampling ». Dans le profil web actuel, cette passe inutilisée est indisponible ; le message ne démontre pas un arrêt des autres effets de post-traitement. Le builder ne masque pas cet avertissement et ne désactive pas les effets.

Depuis `sites/arkya/`, préparer la livraison avec `node prepare-unity-build.mjs ../../Unity3D/Arkya/Builds/WebBeta`. Le build Unity d’origine reste intact ; le script prépare `dist/game/` et découpe les gros fichiers en morceaux d’au plus 4 Mio, avec tailles, ordre et empreintes SHA-256. Le navigateur vérifie les morceaux et le fichier reconstitué avant de le remettre au loader Unity par une URL Blob. Les URLs sont libérées à la fermeture du moteur. Les petits fichiers, crédits et `StreamingAssets/` sont conservés ; un fichier auxiliaire trop volumineux provoque une erreur explicite.

Le site charge `game/build-config.json` et refuse les chemins externes ou sortant de ce dossier. Les fichiers `.unityweb` et leurs morceaux sont servis en binaire sans `Content-Encoding` : le loader Unity effectue la décompression. Le site et les fichiers préparés doivent être publiés ensemble. Aucun build fictif n’est livré lorsque ces fichiers manquent.

Les builders dedicated préparent deux scènes spécifiques : `CharacterDedicated.unity` et `StarterIslandServer.unity`. La seconde copie l’île et installe ses colliders avant le stripping serveur, afin de ne pas dépendre de meshes de rendu supprimés à l’exécution. Les GUID de ces scènes sont conservés lors des reconstructions. Ces scènes générées ne doivent pas être ouvertes pendant leur reconstruction.

Le SDK Discord tente d'activer ses bibliothèques natives pendant tous les builds desktop. Le builder dedicated les exclut par un callback temporaire, rétabli après le build ; les fichiers du package ne sont pas modifiés. L'authentification des joueurs demeure portée par Identity et les tickets Characters, sans SDK social dans le processus serveur.

App UI est une dépendance des outils de l'éditeur. Pour le dedicated Mono, le builder ajoute temporairement les defines documentés `APPUI_PLATFORM_DISABLED` et `APPUI_PLATFORM_EDITOR_ONLY` au target Server et exclut ses plugins natifs. Cela empêche l'initialisation graphique native et évite une dépendance serveur à GTK. Les defines et callbacks sont rétablis après le build ; le client conserve son interface normale.

## Configuration du dedicated

`CharacterDedicatedRuntime` lit la configuration au démarrage. Les secrets restent dans l’environnement du processus ; `ARKYA_DEDICATED_KEY` n’est jamais accepté en argument, stocké dans une scène ou envoyé au navigateur.

| Variable | Valeur / rôle |
| --- | --- |
| `ARKYA_CHARACTERS_URL` | Origine HTTPS interne de Characters, sans chemin |
| `ARKYA_DEDICATED_KEY` | Secret associé côté Characters au couple realm/serveur |
| `ARKYA_REALM_ID` | Identifiant du realm attendu dans l’admission |
| `ARKYA_SERVER_ID` | Identifiant du dedicated attendu dans l’admission |
| `ARKYA_GAME_PORT` | Port TCP d’écoute, `7777` par défaut |
| `ARKYA_WEBSOCKET_PATH` | Chemin WebSocket, `/` par défaut ; identique au chemin annoncé au joueur |
| `ARKYA_READY_FILE` | Fichier facultatif écrit après chargement du terrain, des colliders et écoute effective |
| `ARKYA_ALLOW_LOCAL_HTTP` | `0` par défaut ; `1` autorise uniquement HTTP loopback pour une validation Development avec les vraies API |

Les options non secrètes acceptent aussi leur nom d’argument en minuscules, par exemple `-arkya-game-port 7777`. L’environnement reste préférable pour un conteneur. Lancer le processus avec `-batchmode -nographics` ; provisionner un chemin de readiness accessible en écriture. Le dedicated écoute `AnyIpv4` en WebSocket ; limiter ce port au réseau privé d’ingress.

Le bootstrap annonce la readiness seulement après l’écoute effective. Cette sonde ne certifie pas à elle seule la disponibilité de Characters : une dépendance indisponible refuse l’admission. Lors d’un arrêt normal Unity, le processus retire sa readiness, cesse les admissions et attend les sauvegardes/libérations pendant au plus 15 secondes. Prévoir une marge supérieure pour l’arrêt du conteneur ; un crash ou arrêt forcé est récupéré par l’expiration des leases. Le [guide de l’image Linux](../Unity3D/Arkya/deploy/web-beta/README.md) fournit les commandes Docker, les contraintes de lancement et les résultats du test SIGTERM.

Le nettoyage a révélé un défaut lorsque le serveur n’avait aucun joueur : la coroutine demandait à quitter avant le retour du premier callback `wantsToQuit`, qui annulait cette demande. Le correctif laisse passer une frame après fermeture des admissions et retrait de readiness. Une régression Editor passe ; le build Linux corrigé termine deux arrêts à vide en 0,786 et 0,751 seconde, code 0. Le drainage avec bail actif avait été validé sur l’image précédente en 3,167 secondes ; il n’a pas été rejoué sur cette dernière image. Les deux preuves restent distinctes dans le rapport de validation.

## Services et routage WSS

La destination publique du jeu est une URL **WSS**, distincte des routes HTTPS JSON de l’API. Un proxy approuvé termine TLS, conserve l’upgrade WebSocket et relaie le flux vers le port **TCP 7777** privé du dedicated. Le profil Realms doit annoncer `transport=WebSocket`, l’URL publique, le protocole et le build exacts ; les profils UDP existants restent utilisables pour les autres prototypes.

Configurer côté Characters `Realms:BaseUrl`, `Realms:ServiceKey`, les origines exactes `WebClient:AllowedOrigins` et `GameAccess:DedicatedServers`. Chaque clé dedicated correspond à un seul couple `(realmId, serverId)`. Realms expose son catalogue interne avec une clé de service distincte. Une entrée standalone explicitement configurée peut désigner un vrai processus hors Kubernetes ; les profils Kubernetes exigent aussi un déploiement prêt. Aucun `/internal` ne doit être publié par la gateway publique.

Pour la production, fournir les domaines/certificats réels, secrets de déploiement, dépendances MongoDB, destinations internes et règles réseau. Les valeurs d’exemple dans le contrat ne désignent pas des serveurs déjà en ligne. Tester CORS depuis le domaine exact du site et l’upgrade WSS sur l’URL finale avant d’activer la destination dans Realms.

## Persistance et fermeture

`CharacterServerAdmissionSystem` détient une lease de personnage côté monde serveur. Il renouvelle cette lease toutes les 20 secondes, sauvegarde la position et l’orientation toutes les 15 secondes et effectue une dernière sauvegarde avant libération à la déconnexion. Les écritures portent `leaseId`, `authorityEpoch` et `expectedRevision`. Un retry conserve exactement la requête en cours pour bénéficier de l’idempotence backend.

La lease dure au plus 90 secondes et ne dépasse jamais la session Arkya. Une lease expirée ou un refus d’autorité arrête la simulation du personnage. À la reconnexion, le serveur vérifie les coordonnées sauvegardées, les replace sur le terrain ou la surface de nage et revient au point d’arrivée si la position est invalide ou obstruée. Le client ne fournit jamais cet état.

Une sauvegarde restaurable possède une révision strictement positive. Unity
`JsonUtility` matérialise un `state:null` ou absent en objets de valeur zéro :
tester uniquement la nullité envoyait les nouveaux personnages à `(0, 0)`,
dans l'océan, au lieu de l'Anse de l'Éveil. Le serveur exige désormais cette
révision et un emplacement dont le corps entier tient dans l'empreinte du
terrain. Une ancienne sauvegarde à l'origine retrouve donc l'arrivée sèche,
sans effacer le personnage ni remettre sa révision persistante à zéro. Les
sauvegardes de nage valides à l'intérieur du terrain conservent leur position.

`CharacterSpawnRulesTests` couvre ces règles et les données non finies,
limites et obstacles. `CharacterAdmissionNetworkTests` traverse HTTP JSON,
l'admission du vrai serveur WebSocket, la réplication vers quatre clients et
la libération des leases. Son API est explicitement une fixture locale ; le
test des vraies API et de leur authentification reste séparé.

Le logout du site ferme le client Unity et entraîne la fermeture de sa connexion. Characters vérifie aussi la référence hachée de session auprès d’Identity lors de la consommation du ticket, du renouvellement et de la sauvegarde. Une session révoquée est donc refusée au prochain appel, même pour un client modifié ; la libération reste possible. L’expiration absolue de session reste une borne du bail. Un crash peut perdre les changements depuis la dernière sauvegarde acceptée. Le runtime version 2 ajoute un snapshot de déplacement/traversée et un transfert atomique de cette lease entre partitions StarterIsland. Le [guide de handoff](STARTER_ISLAND_HANDOFF.md) atteste un run réussi avec quatre exécutables dedicated et un client Unity Editor rendu : six handoffs et un blocage maximal mesuré de 0,43 seconde près d’une frontière. Le client natif exporté et cette nouvelle paire WebGL restent à valider. La reprise transactionnelle de combat/inventaire reste séparée.

## Passage entre partitions StarterIsland

Le nouveau runtime prépare une connexion Netcode cible près d'une frontière,
conserve l'autorité source jusqu'au seal et attend le commit Characters avant
d'activer le joueur cible. La position, l'orientation, les séquences d'entrée,
le saut et l'état de traversée sont conservés. Le passage diagonal est résolu
par les bornes du plan ; tant que la cible n'est pas préparée, la simulation
reste dans les bornes source. Aucun choix d'autorité ou de position n'est reçu
du joueur.

Chaque processus récupère `GET /internal/game/topology` avec sa propre clé,
avant sa readiness. Characters doit connaître toutes les identités, via
`GameAccess:DedicatedServers` ou la liste finie `RealmRuntime:ServerIds`.
Les endpoints WSS et leurs ports privés restent à provisionner. Les pools
instances/raids restent des plans non déployables avec ce runtime d'île.

Les scripts [de build isolé](../scripts/area-prototype/Build-StarterIslandPartitions.ps1)
et [de test à quatre dedicateds](../scripts/area-prototype/Test-StarterIslandPartitions.ps1)
utilisent les vraies API et MongoDB. Le [bilan du handoff](STARTER_ISLAND_HANDOFF.md#preuves-au-23-octobre-2026)
rapporte sept waypoints, six handoffs dont deux en l'air, ainsi que la
persistance et la réadmission réelles, avec quatre dedicateds et un client
Editor rendu. Les validations historiques ci-dessous ne prouvent pas le
parcours WebGL de cette nouvelle paire.

## Validation

Le correctif de spawn `ef90ae8` passe **543 tests EditMode** et **3 tests
PlayMode** dans la [CI Unity](https://github.com/Hantse/arkya-mmorpg/actions/runs/39619856892),
dont les 29 nouvelles régressions. Le seul test ignoré reste celui exigeant
`ARKYA_WEB_BETA_TICKETS_FILE`. Les 28 règles et le test à quatre clients
passent également dans l'Editor via MCP. Sur le vrai Terrain chargé,
l'absence d'état et une ancienne sauvegarde d'origine reviennent à
`(405, 33.6876, 350)`, au-dessus du niveau marin `28`, sans collider dans le
gabarit du personnage. La scène initiale est restaurée hors Play Mode et
reste non modifiée. Des diagnostics Netcode de regroupement de ticks sont
présents lors du test Editor à quatre clients ; ces contrôles ne mesurent
pas les performances du serveur public.

Les trois builds (WebGL, Windows, Dedicated Linux) et les contrôles de
démarrage puis d'arrêt des deux images passent dans cette même CI. La
promotion `ba8fdb2` associe les images immuables de la source `ef90ae8` ;
le profil Admin sélectionne cette paire et porte la release de `beta-web`
à la version `2`, depuis le dedicated `7da7ef5`. Le reconciler existant
applique cette intention après synchronisation Argo CD.

Après la sélection GitOps `dba4c5e`, le manifest public expose `ef90ae8`
à 18:42 (Bruxelles). Les quatre payloads répondent 200 avec les bons types
et caches, l'upgrade WSS répond 101 et le vrai WebGL affiche l'atelier
actuel. Aucune erreur console navigateur, seul l'avertissement FSR connu.
Les six validateurs GitOps passent hors cluster ; le hash de configuration
change dans le template Realms, ce qui provoque son rollout sans rebuild
des services. Le digest effectif du pod dédié et l'admission avec un
compte Discord restent non attestés sans session Admin/joueur.

Le 21 octobre 2026, la [CI Unity de `fd77651`](https://github.com/Hantse/arkya-mmorpg/actions/runs/39591280771)
valide le checkout publié de l'atelier et de l'île : **514 tests EditMode**
et **3 tests PlayMode** réussis, aucun échec. Le seul test EditMode ignoré
est `CharacterRemoteNetworkTests.RealServicesAdmitTwoOwnersRejectInvalidAndReplayedTicketsAndReleaseLeases`,
qui exige la fixture réelle `ARKYA_WEB_BETA_TICKETS_FILE`. Ces suites ne
prouvent donc pas l'admission avec une session Discord sur le site public.
Les trois builds (Windows, dedicated Linux et WebGL), la santé et l'arrêt
des deux conteneurs ont ensuite réussi. Les neuf jobs sont verts ; la
promotion Unity `aa8fbc9` publie une paire d'images issue de cette même source.
À 15:19 (Bruxelles), le client public expose `fd77651` et affiche le nouvel
atelier dans un vrai navigateur. Les quatre fichiers du build répondent
HTTP 200 avec leur cache et leur type attendus ; voir la
[vérification du client statique](../backend/deploy/gitops/web-client-control/README.md).
Ce chargement autonome ne couvre pas l'admission avec un compte Discord.
La gateway corrigée dans `ca2a3d3` passe 176 tests, dont le vrai pipeline
Production derrière un proxy approuvé. Après la CI et la promotion des
services, l'upgrade WSS public passe de HTTP 400 à **101** à 15:38, puis
le client .NET ouvre également la connexion à 15:39. L'admission avec
une session Discord et le digest effectif du realm restent à vérifier.

État constaté le 18 octobre 2026, également consigné dans [WEB_BETA_VALIDATION.json](WEB_BETA_VALIDATION.json) :

| Vérification terminée | Résultat |
| --- | --- |
| Suite backend complète, avec MongoDB réel pour les tests de persistance | 502 cas réussis, 0 échec, 0 ignoré ; un seul passage par projet |
| Contrats client Unity, dont bornes du chemin WebSocket | 24 tests réussis |
| Retour à la création avec réduction du viewport | 1 test de layout réussi |
| Arrêt dedicated sans joueur | 1 régression Editor réussie : admissions fermées, readiness retirée, fermeture reportée après le callback Unity |
| Client des services Unity | 23 tests réussis |
| Copies de textures pour WebGL | 9 tests réussis |
| Deux clients Netcode avec les vraies API et WebSocket | 1 test réussi, états des deux personnages vérifiés dans MongoDB |
| Build WebGL Release final | Réussi, 91 850 994 octets au total ; ressources du pipeline de l’île conservées |
| Builds dedicated Windows et Linux | Compilation réussie |
| Image Linux et vrai navigateur WebGL | Admission, reprise, déplacement et sauvegarde dans MongoDB vérifiés |
| SIGTERM Linux avec un personnage connecté | Sortie 0 en 3,2 secondes, sauvegarde finale et libération du bail vérifiées |
| Livraison Web en 23 morceaux | Studio, admission Linux, marche/course/saut vérifiés dans Edge après reconstruction et contrôle SHA-256 |
| Révocation de session pendant une partie | Vrai logout HTTP 204 ; déconnexion décidée par le dedicated avant la revalidation du site, bail libéré et dernière révision acceptée conservée |

Les résultats backend sont dans `artifacts/web-beta/backend-final-tests/summary.json` et les 25 tests récents contrats/layout dans `artifacts/web-beta/unity-final-tests.xml`. Les autres groupes Unity ont été exécutés séparément ; ce tableau ne constitue pas un passage unique de toute la suite Unity.

`CharacterRemoteNetworkTests.RealServicesAdmitTwoOwnersRejectInvalidAndReplayedTicketsAndReleaseLeases` utilise deux vrais clients Netcode, un vrai listener WebSocket et les vraies API. Il vérifie l’apparence propriétaire, la réplication des deux acteurs, l’isolation des entrées, le refus des tickets invalides/rejoués et la fin des leases. Il exige la fixture réelle `backend/tools/Arkya.WebBeta.Fixture` et la variable `ARKYA_WEB_BETA_TICKETS_FILE`, pointant vers son fichier privé de tickets renouvelés juste avant le test. Sans fixture, le test est explicitement ignoré ; aucun serveur d’admission simulé ne le remplace. Les fichiers privés et leurs secrets ne doivent pas être versionnés ou imprimés.

Le premier Release WebGL a également tourné dans un vrai navigateur Windows avec le dedicated Windows et les services locaux réels. Le site et Unity ont effectué quatre vérifications `/me`, chargé les realms et personnages, puis effectué deux admissions et ouvert deux connexions WebSocket successives. Les entrées clavier de marche/course et Espace ont été observées. L’audit indépendant MongoDB confirme le déplacement, la sauvegarde périodique, la sauvegarde finale et la libération au retour, puis la reprise exacte de la position sauvegardée à la seconde entrée et la nouvelle libération. Les observations sont dans `artifacts/web-beta/browser-real-windows/observations.json` et l’audit dans `artifacts/web-beta/linux-validation/browser-state-audit.jsonl`. Aucun défaut JavaScript non intercepté n’a été relevé ; les performances et phases d’animation n’ont pas été mesurées dans cette session.

Cette validation utilise un compte et une session Arkya préchargés dans des bases de test isolées, puis vérifiés par les vraies API Identity, Characters et Realms avec MongoDB. Elle n’exerce pas le consentement OAuth Discord public et ne livre aucun OAuth simulé ni contournement d’authentification dans le produit.

Le navigateur a révélé une compression de la colonne de personnalisation après retour de l’île. La correction passe le test Unity de layout et la vérification dans le build web final. Le rebuild conserve aussi les ressources SSAO du pipeline chargé par l’île ; la dernière session navigateur ne relève aucune erreur console ni exception JavaScript. L’avertissement de passe FSR inutilisée décrit plus haut reste présent.

La révocation externe en cours de partie a été vérifiée indépendamment du site : HTTP 204, libération du bail observée en au plus 13,2 secondes, puis fermeture du socket et retour à la création avant la prochaine revalidation `/me` de la page. Le bouton de déconnexion du site a ensuite effacé sa session et masqué le jeu malgré le 401 attendu pour cette session déjà révoquée. Aucun défaut JavaScript non intercepté ; cinq avertissements de framebuffer WebGL apparaissent uniquement pendant `Quit`, sans empêcher la fermeture. Le [rapport du parcours par morceaux](../artifacts/web-beta/browser-chunks-linux/observations.json) conserve les observations et leurs limites de mesure.

Le client est publié en version 6 sur la [page Jouer](https://arkya-atlas.easykrill33.chatgpt.site/play.html?lang=fr). Le manifest et un morceau de 4 Mio sont téléchargés publiquement avec les bons types MIME, sans encodage HTTP parasite ; l’empreinte du morceau est conforme. Un navigateur Edge neuf sur le site publié affiche la connexion Discord et ne télécharge aucun fichier Unity sans session. Le certificat TLS de l’API publique n’est pas reconnu comme fiable depuis cette machine (nom et chaîne refusés au dernier contrôle du 18 octobre à 10:52 UTC) : OAuth Discord public et WSS public ne sont donc pas vérifiés. Deux onglets/comptes simultanés et le logout pendant le chargement restent à valider dans le vrai navigateur. Aucun résultat local ne vaut confirmation de mise en production.

Le parcours natif de test local sur loopback `17843` est conservé. La bêta web concerne une seule île par realm et le mouvement persistant ; les quêtes, le combat, l’inventaire/récolte, les totems, les copies multiples de l’île et le prototype de transfert entre areas ne sont pas raccordés à cette admission.
