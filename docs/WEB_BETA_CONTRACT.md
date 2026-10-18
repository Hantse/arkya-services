# Contrat bêta Unity web — version 1

Contrat partagé client Unity, dedicated et services. `protocolVersion` vaut `1`, `buildId` vaut `arkya-web-beta-v1`. La session Arkya du site est transmise en mémoire au client Unity après vérification `/v1/auth/me`, jamais dans une URL. Aucun jeton Discord, secret de service ou accès MongoDB ne rejoint le client.

## API publique, via la gateway

Toutes les routes ci-dessous exigent `Authorization: Bearer <session Arkya>`, HTTPS et une origine CORS explicitement autorisée. Réponses `Cache-Control: no-store`.

`GET /v1/realms` :

```json
{"realms":[{"realmId":"beta-web","displayName":"Bêta web","ready":true,"serverId":"starter-island","endpoint":"wss://game.example/","protocolVersion":1,"buildId":"arkya-web-beta-v1"}]}
```

Les identifiants et domaines ci-dessus illustrent le format, pas une destination déployée. Seules les destinations approuvées et activées dans Realms sont proposées. `ready` décrit l'admission activée pour une destination compatible ; il ne garantit pas que le réseau entre le joueur et le dedicated fonctionne. Les profils Kubernetes exigent aussi un déploiement `Ready`. Une destination standalone explicitement configurée permet un dedicated réel sans Kubernetes ; aucun serveur simulé n'est créé par le backend.

Les contrats existants `POST /v1/characters`, `GET /v1/characters` et `GET /v1/characters/{id}` sont conservés. La création accepte un champ supplémentaire `realmId` nullable ; les réponses personnage l'incluent. S'il est absent, la première demande d'admission valide lie atomiquement le personnage au realm demandé. Ce rattachement ne peut plus changer par ces API. Le propriétaire provient toujours de la session. Les noms restent globalement uniques.

`POST /v1/characters/{characterId}/admissions` :

```json
{"realmId":"beta-web","protocolVersion":1,"buildId":"arkya-web-beta-v1"}
```

Réponse `200` :

```json
{"ticket":"<opaque base64url, 43 caractères>","expiresAt":"2026-10-18T12:01:00Z","endpoint":"wss://game.example/","protocolVersion":1,"buildId":"arkya-web-beta-v1","realmId":"beta-web","serverId":"starter-island"}
```

Le ticket expire après 60 secondes au plus, sans dépasser l'expiration de session. Le backend conserve uniquement SHA-256 du ticket ainsi que SHA-256 de la session authentifiée. Une nouvelle émission remplace un ticket encore en attente ; une lease active interdit une nouvelle admission. Unity ouvre la destination WSS reçue et transmet le ticket par le message applicatif d'admission, avant tout spawn/entrée en jeu. Le serveur ne reçoit pas le bearer joueur sur le transport Netcode. La consommation revalide la session auprès d'Identity : un ticket déjà émis est refusé si sa session a été révoquée.

Erreurs JSON `{"code":"..."}` : `400 invalid_request` ou `incompatible_client`, `401` session invalide, `404 character_not_found`, `409 realm_mismatch`, `409 character_in_use`, `503 realm_unavailable` ou `dependency_unavailable`. Les endpoints Characters existants conservent leurs erreurs antérieures.

## API interne dedicated → Characters

HTTPS hors Development. Toutes ces routes exigent `X-Arkya-Dedicated-Key: <secret>`. Characters associe cette clé, côté serveur, à un couple immuable `(realmId, serverId)` configuré. Aucun header ou corps fourni par l'appelant ne remplace cette identité. Les routes `/internal` ne sont jamais publiées par la gateway.

`POST /internal/game/admissions/consume` :

```json
{"ticket":"<ticket reçu du joueur>"}
```

Réponse `200` :

```json
{"accountId":"<uuid>","character":{"id":"<uuid>","name":"Aelys","level":1,"createdAt":"2026-10-18T12:00:00Z","appearance":{"version":1,"race":"human","silhouette":"a","heightCm":175,"skin":"warm","hairStyle":"short","hairColor":"brown","eyeColor":"brown","outfitColor":"blue"},"realmId":"beta-web"},"realmId":"beta-web","serverId":"starter-island","leaseId":"<uuid>","authorityEpoch":1,"leaseExpiresAt":"2026-10-18T12:01:30Z","sessionExpiresAt":"2026-10-18T20:00:00Z","state":null}
```

`character.appearance` reprend exactement le contrat Characters existant ; les valeurs illustratives doivent être remplacées par les options valides du catalogue. Le dedicated utilise cette apparence validée, jamais une apparence déclarée dans le handshake client. `state:null` signifie première entrée : le dedicated choisit le spawn autoritaire de l'île.

La consommation et la prise de lease sont atomiques dans MongoDB. Les écritures contrôlent à nouveau les expirations avec l'horloge MongoDB au moment du CAS ; synchroniser les horloges des machines de service. Un ticket expiré, rejoué, remplacé, lié à une session révoquée ou destiné à un autre serveur est refusé `409 admission_rejected`. La lease dure 90 secondes au plus et ne dépasse jamais l'expiration de session enregistrée initialement. Le dedicated envoie un heartbeat toutes les 20 secondes. Heartbeat et sauvegarde revalident la référence de session auprès d'Identity sans cache ; révocation ou propriétaire différent donne `409 lease_rejected`, indisponibilité/timeout Identity donne `503 dependency_unavailable`, sans prolongation du bail. Le dedicated déconnecte le joueur lors d'un refus définitif et cesse sa simulation à l'expiration s'il ne peut renouveler. Une nouvelle consommation après expiration incrémente l'époque et invalide définitivement les écritures de l'ancien serveur.

`POST /internal/game/leases/{characterId}/heartbeat` :

```json
{"leaseId":"<uuid>","authorityEpoch":1}
```

Réponse `200` : `{"leaseExpiresAt":"2026-10-18T12:01:50Z"}`.

`PUT /internal/game/leases/{characterId}/state` :

```json
{"leaseId":"<uuid>","authorityEpoch":1,"expectedRevision":0,"position":{"x":120.0,"y":35.0,"z":180.0},"forward":{"x":0.0,"z":1.0}}
```

Réponse `200` :

```json
{"revision":1,"position":{"x":120.0,"y":35.0,"z":180.0},"forward":{"x":0.0,"z":1.0}}
```

Cet objet devient `state` à la prochaine consommation. La bêta concerne une seule île par realm. Les coordonnées viennent uniquement de la simulation du dedicated ; valeurs finies et bornées vérifiées par Characters, placement sûr sur le terrain vérifié par le dedicated au chargement. Écriture par comparaison de révision et de lease/époque. Rejouer exactement une sauvegarde déjà acceptée peut retourner son résultat ; une écriture concurrente ou obsolète retourne `409 state_conflict`. Une lease expirée, étrangère ou périmée retourne `409 lease_rejected`.

`POST /internal/game/leases/{characterId}/release` avec le même corps que heartbeat retourne `204`. Sauvegarder avant libération ; le navigateur ne peut ni sauvegarder ni libérer une lease. La déconnexion du site demande au client Unity de se déconnecter ; le dedicated libère ensuite sa lease. Release n'exige pas une session encore valide auprès d'Identity et reste donc possible après logout tant que le bail appartient toujours au même serveur/époque et n'est pas expiré. Une sauvegarde après révocation est refusée : la dernière sauvegarde acceptée reste la référence persistante.

## API interne Characters → Identity

`POST /internal/sessions/introspect-reference`, avec `X-Arkya-Service-Key` (clé interservice Identity existante), reçoit exactement :

```json
{"sessionReference":"<SHA-256 hexadécimal majuscule, 64 caractères>"}
```

Réponse `200` : `{"accountId":"<uuid>","displayName":"Aelys","expiresAt":"2026-10-18T20:00:00Z"}`. `401` signifie référence inconnue, mal formée, expirée ou révoquée ; `403` signifie clé interservice incorrecte. Cette route n'a pas de CORS navigateur et demeure exclue de la gateway. La référence est calculée côté Characters seulement après validation du bearer ; elle n'est ni acceptée dans les DTO publics ni renvoyée au dedicated. Mongo conserve cette référence dans `game_leases` schéma 2, jamais le bearer. Les anciens enregistrements sans référence conservent leur état mais ne peuvent plus être consommés/renouvelés/sauvegardés ; une nouvelle admission les remplace après libération/expiration.

L'appel HTTP interne est borné à cinq secondes, corps compris. Characters recoupe l'accountId, relit l'horloge après IO et ne peut augmenter l'expiration de session initiale, même si Identity répond avec une date plus tardive. Une révocation concurrente à une validation déjà engagée peut croiser cette requête ; elle est observée à la prochaine validation, au plus tard au prochain heartbeat en régime normal. Il n'y a pas de notification poussée ni de transaction distribuée Identity/Characters.

## API interne Characters → Realms

`GET /internal/realms/entries` avec `X-Arkya-Service-Key` retourne le même objet `realms` que le catalogue public. La clé est distincte des clés dedicated et de la clé administrateur Realms. Characters effectue la validation de session et n'expose pas les routes opérateur. Échec Realms : admission refusée, aucun fallback fictif.

## Configuration et transport

- Characters : `Realms:BaseUrl`, `Realms:ServiceKey`, `WebClient:AllowedOrigins[]`, `GameAccess:DedicatedServers[]` (`realmId`, `serverId`, `key`). Aucune clé n'est placée dans un fichier public.
- Realms : `GameAccess:ServiceKey`, `GameAccess:StandaloneEntries[]` (`realmId`, `displayName`, `serverId`, `endpoint`, `enabled`, `protocolVersion`, `buildId`). Liste vide par défaut ; configuration explicite d'un vrai dedicated pour le développement local.
- Les serveurs des profils de déploiement ajoutent `transport` (`Udp` par défaut, ou `WebSocket`), `publicEndpoint` WSS, `protocolVersion`, `buildId`. WebSocket produit des ports Kubernetes TCP ; UDP demeure inchangé.
- TLS WSS est terminé par le proxy approuvé, qui relaie l'upgrade WebSocket vers le dedicated TCP. Le ticket reste dans le protocole applicatif, pas dans la query du handshake HTTP. Les routes HTTP de l'API et du jeu restent distinctes.
- Seuls des endpoints WSS absolus sans credentials, query ni fragment sont admis en production. Le chemin échappé `Uri.AbsolutePath` transmis à Unity Transport doit occuper au plus 125 octets UTF-8 (capacité utile de `FixedString128Bytes`) ; par exemple `é` compte comme les six octets de `%C3%A9`. En Development, `ws://localhost:<port>/` et `ws://127.0.0.1:<port>/` sont acceptables pour un vrai dedicated local.

Le bootstrap Unity dédié consomme ses clés depuis l'environnement/provisionnement serveur. Il doit refuser les connexions non admises, charger l'état persistant, appliquer les mêmes règles de terrain et ne jamais créer un serveur local dans le client web.
