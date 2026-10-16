# Quêtes : administration, publication et chargement dédié

Cette tranche relie une interface web d'administration, un catalogue de contenu publié et le prototype Unity à deux areas. Elle permet de définir une quête « tuer N ennemis », de publier une version immuable, de charger cette version au démarrage des dedicateds et de sauvegarder la progression de deux personnages dans Characters.

L'intégration Unity se trouve dans `Unity3D/Arkya/Assets/Arkya/AreaPrototype/`, sur Unity `6000.6.6f1`. Elle utilise le prototype décrit dans [AREA_PROTOTYPE.md](AREA_PROTOTYPE.md). Elle ne branche pas encore les personnages sur une admission de joueur en production.

## Périmètre et propriété

| Élément | Responsabilité | Données possédées |
| --- | --- | --- |
| `backend/src/Arkya.Admin` | Pages Razor, connexion admin locale, édition du brouillon, publication et historique | Cookie de session admin ; aucun document métier MongoDB |
| `backend/src/Arkya.Content.Api` | Validation des définitions, révisions du brouillon et publications immuables | Base `arkya_content`, collection `content_revisions` |
| `backend/src/Arkya.Characters.Api` | Progression persistante des quêtes d'un personnage existant | Base `arkya_characters`, collection `character_quests`, en plus des personnages |
| Serveurs Unity du prototype | Validation des morts, attribution des crédits, progression et sauvegardes autoritaires | État de simulation et progression en mémoire pendant le run |
| Clients Unity | Entrées et affichage de la progression reçue | Aucune écriture de progression faisant autorité |

Content et Characters possèdent des bases séparées, même lorsqu'elles partagent une instance MongoDB locale. L'admin passe uniquement par les routes HTTP de Content. Characters ne lit pas les collections Content : le dedicated valide sa publication épinglée et fournit les règles acceptées lors de la première sauvegarde. Le service Characters protège ensuite ces règles et la monotonie du compteur.

Pour cette tranche, toutes les quêtes du catalogue sont acceptées automatiquement au chargement si leur `questId` n'est pas déjà présent dans la progression. Il n'y a pas encore de PNJ, d'écran d'acceptation, d'abandon, de récompense ou de remise de quête. Toutes les définitions ciblent l'ennemi connu `prototype-monster`.

## Accès et configuration locale

Prérequis : SDK .NET 10, PowerShell 7.4 ou plus récent pour le lancement avec environnement distinct par processus, Docker pour les scénarios MongoDB jetables et Unity `6000.6.6f1` avec le module Dedicated Server Windows pour reconstruire les binaires. La configuration MongoDB locale existante est décrite dans [le guide backend](../backend/README.md).

| Application | Adresse locale | Configuration |
| --- | --- | --- |
| Content | `http://localhost:5143` et `https://localhost:7143` | Profil `https` de `Arkya.Content.Api` |
| Admin | `http://localhost:5183` | Profil `http` de `Arkya.Admin` |
| Characters | `https://localhost:7142` | Profil existant ; une URL HTTP loopback peut être choisie explicitement en Development |

Les exemples ci-dessous sont à exécuter depuis la racine du dépôt. Remplacer tous les marqueurs `<...>` par des valeurs locales ; aucune valeur d'exemple ne constitue un secret utilisable.

Les trois accès restent distincts :

| Secret | Détenteur | Usage |
| --- | --- | --- |
| `Admin:Password` | Opérateur et serveur web Admin | Mot de passe de connexion à l'interface, de 16 à 512 caractères |
| `ContentAdmin:Key` côté Content / `ContentApi:AdminKey` côté Admin | Admin et Content seulement | Header `X-Arkya-Admin-Key`, de 32 à 512 caractères |
| `ServiceAuthentication:Key` côté Content | Content et dedicateds autorisés | Header `X-Arkya-Service-Key`, de 32 à 512 caractères, obligatoirement différent de la clé admin Content |
| `ServiceAuthentication:Key` côté Characters | Characters et dedicateds autorisés | Header `X-Arkya-Service-Key` des opérations de progression |

Utiliser des valeurs indépendantes pour le mot de passe, la clé admin Content et les différentes clés de service. La clé interne Identity reste celle de la configuration Characters existante. Les secrets ne sont pas placés dans les assets Unity, le JSON publié ou les arguments de ligne de commande.

Configurer Content :

```powershell
$contentProject = 'backend/src/Arkya.Content.Api'
dotnet user-secrets set 'Mongo:ConnectionString' 'mongodb://<user>:<password>@localhost:27017/?authSource=admin' --project $contentProject
dotnet user-secrets set 'Mongo:DatabaseName' 'arkya_content' --project $contentProject
dotnet user-secrets set 'ContentAdmin:Key' '<clé-admin-content-de-32-à-512-caractères>' --project $contentProject
dotnet user-secrets set 'ServiceAuthentication:Key' '<clé-service-content-distincte-de-32-à-512-caractères>' --project $contentProject
```

Configurer Admin, avec la même clé admin Content que ci-dessus :

```powershell
$adminProject = 'backend/src/Arkya.Admin'
dotnet user-secrets set 'Admin:Password' '<mot-de-passe-admin-distinct-de-16-à-512-caractères>' --project $adminProject
dotnet user-secrets set 'ContentApi:BaseUrl' 'http://localhost:5143' --project $adminProject
dotnet user-secrets set 'ContentApi:AdminKey' '<même-clé-admin-content>' --project $adminProject
```

Configurer Characters si ce service n'est pas déjà prêt :

```powershell
$charactersProject = 'backend/src/Arkya.Characters.Api'
dotnet user-secrets set 'Mongo:ConnectionString' 'mongodb://<user>:<password>@localhost:27017/?authSource=admin' --project $charactersProject
dotnet user-secrets set 'Mongo:DatabaseName' 'arkya_characters' --project $charactersProject
dotnet user-secrets set 'ServiceAuthentication:Key' '<clé-service-characters-de-32-à-512-caractères>' --project $charactersProject
dotnet user-secrets set 'Identity:BaseUrl' 'https://localhost:7141' --project $charactersProject
dotnet user-secrets set 'Identity:ServiceKey' '<clé-service-Identity-existante-de-32-à-512-caractères>' --project $charactersProject
```

Les `UserSecretsId` sont respectivement `arkya-content-local`, `arkya-admin-local` et `arkya-characters-local`. User Secrets sert au développement et n'est pas un coffre de déploiement. En déploiement, utiliser les secrets de l'environnement, des comptes MongoDB limités à leur base et HTTPS. Les applications ASP.NET refusent HTTP hors de Development. Le client HTTP Unity autorise HTTPS et HTTP sur loopback ; il ne désactive pas la validation des certificats et ne suit pas les redirections.

Lancer chaque service dans son propre terminal :

```powershell
dotnet run --project backend/src/Arkya.Content.Api
dotnet run --project backend/src/Arkya.Admin
dotnet run --project backend/src/Arkya.Characters.Api
```

Pour HTTPS local, faire confiance au certificat de développement avec `dotnet dev-certs https --trust`. Pour un exercice Unity local entièrement HTTP loopback, Characters peut être lancé explicitement ainsi à la place de sa commande précédente :

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/src/Arkya.Characters.Api --no-launch-profile --urls http://127.0.0.1:5142
```

`GET /health` existe sur les services. Ces sondes attestent que l'application répond ; elles ne constituent pas une vérification de disponibilité de toutes ses dépendances. Les services MongoDB initialisent leur collection/index au démarrage et nécessitent une base joignable.

## Utiliser l'interface admin

Ouvrir `http://localhost:5183`, puis se connecter avec `Admin:Password`. Le serveur Razor agit comme intermédiaire : il appelle Content avec sa clé admin et ne remet pas cette clé au navigateur. Le navigateur conserve un cookie admin HttpOnly, SameSite Strict, d'une durée de deux heures ; les formulaires utilisent un jeton antiforgery. La connexion est limitée en fréquence.

Cette connexion est un accès d'exploitation local propre à l'admin. Elle n'utilise pas les sessions Discord/Identity des joueurs et n'implémente pas encore une gestion d'opérateurs, de rôles Identity ou un audit nominatif de publication.

Créer par exemple la quête suivante dans le brouillon :

```json
{
  "questId": "clear-the-path",
  "title": "Dégager le chemin",
  "description": "Éliminer trois monstres proches du passage.",
  "enemyId": "prototype-monster",
  "requiredKills": 3
}
```

Enregistrer le brouillon, puis publier sa révision enregistrée. L'historique présente les publications et permet de télécharger leur JSON. Relever l'UUID de publication pour configurer `ARKYA_CONTENT_VERSION`. Modifier le brouillon seul ne change aucun serveur ni aucune publication existante.

Un conflit d'édition ou de publication demande de recharger le brouillon avant de recommencer. Une publication dont la réponse s'est perdue peut être rejouée avec le même `draftRevision` : elle renvoie la même version, la même date et le même snapshot.

## Contrats Content

Toutes les routes de cette section sont des appels d'exploitation ou de service. Un bearer de joueur ne leur donne aucun accès. Les headers admin et service ne sont pas interchangeables ; un header absent, invalide ou présent plusieurs fois entraîne `403`.

| Méthode et route | Authentification | Corps / résultat |
| --- | --- | --- |
| `GET /admin/content/draft` | `X-Arkya-Admin-Key` | `200` : `{ "revision": 0, "quests": [] }` initialement |
| `PUT /admin/content/draft` | `X-Arkya-Admin-Key` | Corps `{ "revision": 0, "quests": [...] }` ; `200` : brouillon avec nouvelle révision ; `400` invalide ; `409` révision dépassée |
| `GET /admin/content/catalogs` | `X-Arkya-Admin-Key` | `200` : tableau des catalogues publiés, du plus récent au plus ancien selon leur révision source |
| `POST /admin/content/catalogs` | `X-Arkya-Admin-Key` | Corps `{ "draftRevision": 1 }` ; `200` : catalogue publié ou résultat identique d'un retry ; `400` invalide/vide ; `409` brouillon périmé non publié |
| `GET /internal/content/catalogs/{version}` | `X-Arkya-Service-Key` | `200` : catalogue immuable ; `404` version absente, non publiée ou UUID incorrect |

Un catalogue utilise ce schéma :

```json
{
  "schemaVersion": 1,
  "version": "bfb16d11-7350-42dd-9a74-6e57fdf27200",
  "publishedAt": "2026-10-15T10:00:00+00:00",
  "quests": [
    {
      "questId": "clear-the-path",
      "title": "Dégager le chemin",
      "description": "Éliminer trois monstres proches du passage.",
      "enemyId": "prototype-monster",
      "requiredKills": 3
    }
  ]
}
```

Règles de validation :

- `schemaVersion` vaut `1`. `version` est un UUID au format `D`, attribué par Content ; `publishedAt` est une date UTC attribuée par Content.
- Un brouillon contient de 0 à 128 quêtes ; une publication contient de 1 à 128 quêtes. Le tableau et ses entrées ne peuvent pas être `null`.
- `questId` est un slug ASCII minuscule de 1 à 64 caractères : lettres/chiffres séparés éventuellement par un tiret simple. Pas d'espace, de tiret initial/final ou de doublon dans un catalogue.
- Le titre comporte de 1 à 120 caractères, sans caractères de contrôle ; les espaces extérieurs sont retirés lors de la sauvegarde. La description est une chaîne non nulle de 0 à 2000 caractères ; retours à la ligne et tabulations y sont autorisés.
- `enemyId` vaut exactement `prototype-monster`. `requiredKills` est un entier compris entre `1` et `10000`.
- Les champs JSON inconnus sont refusés. Le corps HTTP est limité à 512 Kio et la profondeur JSON à 16.

Conserver les IDs d'une même quête entre publications. Changer l'ID désigne une nouvelle quête : le prototype l'accepte en plus de l'ancienne progression, sans la remplacer.

### Révisions, concurrence et immutabilité

La collection `content_revisions` conserve un document par révision. Les définitions d'une révision ne sont jamais modifiées après insertion. L'ID numérique unique garantit qu'une seule édition concurrente gagne l'insertion de `r + 1` ; les autres reçoivent `409`. La révision initiale est `0`, vide.

Une publication marque atomiquement sa révision avec sa date. Son UUID est stable pour cette révision et dispose d'un index unique. Elle expose tout le catalogue validé, puis reste lisible sans dépendre du brouillon courant. Aucun remplacement de publication n'est permis. L'historique ne grossit pas dans un document singleton et ne nécessite pas de transaction MongoDB entre plusieurs collections.

Une édition et une publication dont les appels se chevauchent peuvent réussir toutes deux : le catalogue publié reste exactement le snapshot de la révision observée par l'appel. Une ancienne révision non publiée déjà dépassée avant l'appel est refusée. Le retry d'une révision déjà publiée reste accepté même si de nouvelles éditions ont eu lieu.

Pour revenir à un ancien contenu, épingler son UUID au prochain démarrage coordonné des areas. Cela ne migre pas les règles des quêtes déjà acceptées.

## Progression Characters

Ces routes exigent la clé de service Characters et un personnage existant. Il n'existe pas de route publique de création de progression et le client Unity ne leur envoie pas directement ses morts ou compteurs.

| Méthode et route | Résultat |
| --- | --- |
| `GET /internal/characters/{characterId}/quests` | `200` : `{ "characterId": "<UUID>", "quests": [...] }` ; `404` personnage absent |
| `PUT /internal/characters/{characterId}/quests/{questId}` | `200` : progression enregistrée/idempotente ; `400` invalide ; `404` personnage absent ; `409` conflit de révision, de règles ou de monotonie |

Corps d'une sauvegarde :

```json
{
  "catalogVersion": "bfb16d11-7350-42dd-9a74-6e57fdf27200",
  "requiredKills": 3,
  "kills": 2,
  "expectedRevision": 1
}
```

Réponse :

```json
{
  "questId": "clear-the-path",
  "catalogVersion": "bfb16d11-7350-42dd-9a74-6e57fdf27200",
  "requiredKills": 3,
  "kills": 2,
  "completed": false,
  "revision": 2
}
```

La première écriture utilise `expectedRevision: 0` et crée la révision `1`. Les suivantes fournissent la révision connue. `kills` reste compris entre `0` et `requiredKills` et ne peut pas diminuer. La version du catalogue et l'objectif d'une quête acceptée ne peuvent pas changer. `completed` est calculé par le service et ne fait pas partie du corps de sauvegarde. L'index unique porte sur le couple personnage/quête.

Si la réponse d'une écriture se perd, renvoyer exactement les mêmes règles, compteur et révision attendue. Characters reconnaît l'écriture déjà appliquée et restitue son résultat. Le dedicated conserve ce payload tant que son résultat est ambigu, même si de nouveaux kills arrivent entre-temps ; il envoie ensuite le nouveau compteur avec la révision confirmée. Il ne transforme pas un `409` en écrasement automatique.

Les erreurs de dépendance remontent en `503`, sans détails de connexion dans la réponse. Le dedicated borne ses appels HTTP, réessaie les sauvegardes transitoires et s'arrête après trois tentatives infructueuses. Une réponse non transitoire ou incompatible, notamment un conflit CAS, provoque un arrêt avec erreur et bloque la poursuite des crédits.

## Charger et lancer les dedicateds

Construire les binaires dans un dossier d'artefacts réservé à cette tranche :

```powershell
./scripts/area-prototype/Build-AreaPrototype.ps1 `
    -ArtifactRoot '<repo>/artifacts/content-workbench'
```

Remplacer `<repo>` par le chemin absolu du dépôt. `-UnityPath` permet de fournir un autre emplacement de l'éditeur. Le script compile le client et le dedicated dans des projets de validation isolés ; il produit notamment :

```text
artifacts/content-workbench/build/Arkya.AreaPrototype.exe
artifacts/content-workbench/build/server/Arkya.AreaPrototype.Server.exe
artifacts/content-workbench/build-response.json
artifacts/content-workbench/unity-build-client.log
artifacts/content-workbench/unity-build-server.log
```

Les deux personnages doivent déjà exister dans Characters et avoir des UUID distincts non vides. Pour un parcours réel de création, utiliser Identity puis les routes publiques Characters documentées dans [le guide backend](../backend/README.md). Aucune route de création de personnage de développement n'a été ajoutée pour les quêtes.

Dans le terminal qui lancera les serveurs, fournir les sept variables :

```powershell
$env:ARKYA_CONTENT_URL = 'http://localhost:5143'
$env:ARKYA_CONTENT_SERVICE_KEY = '<clé-service-content>'
$env:ARKYA_CONTENT_VERSION = '<UUID-D-de-la-publication>'
$env:ARKYA_CHARACTERS_URL = 'https://localhost:7142'
$env:ARKYA_CHARACTERS_SERVICE_KEY = '<clé-service-characters>'
$env:ARKYA_CHARACTER_1_ID = '<UUID-D-personnage-1-existant>'
$env:ARKYA_CHARACTER_2_ID = '<UUID-D-personnage-2-existant>'
```

Si Characters a été lancé sur HTTP loopback comme ci-dessus, utiliser `http://127.0.0.1:5142` pour `ARKYA_CHARACTERS_URL`. Les clés doivent correspondre à `ServiceAuthentication:Key` de leur service respectif ; la clé admin Content ne convient pas au dedicated.

Lancer les deux areas et les deux clients avec les binaires construits :

```powershell
$run = ./scripts/area-prototype/Start-AreaPrototype.ps1 `
    -ExecutablePath '<repo>/artifacts/content-workbench/build/Arkya.AreaPrototype.exe' `
    -ServerExecutablePath '<repo>/artifacts/content-workbench/build/server/Arkya.AreaPrototype.Server.exe' `
    -OutputRoot '<repo>/artifacts/content-workbench/runs' `
    -Automated -HeadlessClients -DuplicateImpacts -QuestScenario -DurationSeconds 18
```

`-QuestScenario` fournit un exercice déterministe : les clients restent en place, le joueur 1 tire sur le monstre proche de la frontière, les projectiles ne blessent pas les joueurs et le monstre réapparaît après sa mort. Retirer `-HeadlessClients` pour voir les clients ; retirer `-Automated` pour jouer manuellement. Les ports Netcode `7777`/`7778` et peer `7790`/`7791` sont limités à loopback. Le launcher refuse les runs qui se chevauchent sur ces ports et retire les clés dédiées de l'environnement des processus clients.

Au démarrage, chaque serveur charge exactement l'UUID configuré, vérifie le schéma et les définitions, charge la progression de son personnage puis prépare sa simulation. La requête d'écoute Netcode n'est créée qu'après cette validation. Une configuration partielle, un catalogue absent ou incompatible, un personnage absent ou un service inaccessible provoque une sortie en erreur et un rapport `startup-failure-{area}.json`.

Les variables sont optionnelles pour conserver le prototype historique : si aucune des sept n'est définie, les quêtes sont désactivées. Dès qu'une variable est présente, les sept sont requises. L'interface client reçoit la progression autoritaire par RPC ; elle n'a pas besoin des clés ni d'un accès HTTP Content/Characters.

### Version des quêtes déjà acceptées

Il n'y a pas de rechargement à chaud. Changer une publication ou `ARKYA_CONTENT_VERSION` ne prend effet qu'au prochain démarrage des serveurs. Les deux areas doivent utiliser la même publication ; une divergence détectée entre peers arrête le serveur.

Au chargement de la progression, une quête déjà acceptée conserve son `catalogVersion`, son `requiredKills`, son compteur et sa révision. Une nouvelle publication qui passe son objectif de `3` à `8` ne transforme pas l'objectif déjà accepté. Une quête retirée du catalogue conserve sa progression. Les nouveaux IDs sont acceptés automatiquement. Cette tranche ne prévoit ni migration de quête acceptée, ni nettoyage des anciennes entrées ; prévoir cette politique avant d'accumuler un grand nombre de publications disjointes.

### Autorité, transferts et arrêts

Le propriétaire autoritaire du monstre valide la transition vivant → mort et produit un crédit portant l'identité de l'impact. Les impacts et crédits rejoués sont dédupliqués en mémoire pendant le run. Les crédits distants sont retransmis jusqu'à acquittement.

Le journal d'un joueur possède un writer fixe pour cet exercice : area `0` écrit la progression du joueur/personnage `1`, area `1` celle du joueur/personnage `2`. Ce rôle est distinct de l'area qui possède actuellement l'entité après un transfert. Traverser la frontière ne transfère pas le journal et ne crée pas deux writers ; le propriétaire de la cible route le crédit vers le writer fixe du tireur.

Chaque processus peer échange une identité de boot. Un payload n'est accepté qu'après écho de l'identité du boot courant. Le redémarrage unilatéral d'un voisin déjà connu entraîne un arrêt avec erreur, afin d'empêcher le rejeu de crédits contre un journal rechargé sans son registre mémoire. Reprendre l'exercice en redémarrant les deux areas avec une nouvelle identité de run, comme le fait le launcher. Ce mécanisme n'est pas un journal durable d'événements ni une reprise après panne transparente.

Une progression n'est durable qu'une fois son écriture confirmée par Characters. Un crash avant sauvegarde peut perdre des crédits ; aucun exactly-once durable à travers crash n'est revendiqué. Les arrêts automatiques bornent la vidange des sauvegardes et crédits, puis signalent un échec si elle n'aboutit pas. Le protocole peer est celui d'un prototype loopback et ne remplace pas des identités de service authentifiées pour l'hébergement public.

## Vérification

Construire et tester le backend :

```powershell
dotnet build backend/Arkya.Backend.slnx
dotnet test backend/Arkya.Backend.slnx
```

Tests ciblés :

```powershell
dotnet test backend/tests/Arkya.Content.Tests/Arkya.Content.Tests.csproj
dotnet test backend/tests/Arkya.Admin.Tests/Arkya.Admin.Tests.csproj
dotnet test backend/tests/Arkya.Characters.Tests/Arkya.Characters.Tests.csproj
```

Les tests MongoDB s'activent explicitement avec une instance de test joignable :

```powershell
$env:ARKYA_TEST_MONGO = 'mongodb://127.0.0.1:<port-mongo-jetable>'
dotnet test backend/Arkya.Backend.slnx
Remove-Item Env:ARKYA_TEST_MONGO
```

Ne jamais utiliser une base de production. Les tests créent et suppriment leurs propres bases isolées. Sans `ARKYA_TEST_MONGO`, les cas MongoDB apparaissent comme ignorés : cela ne constitue pas une validation de la persistance.

Le scénario complet est fourni par `scripts/content-workbench/Test-QuestContent.ps1`. Il utilise une fixture MongoDB Docker, deux personnages de test, les services locaux et les binaires Unity de cette tranche. Il exerce la connexion au site admin, l'enregistrement et la publication, deux areas et deux clients, la progression persistée, un redémarrage sur une nouvelle publication dont l'objectif change et le refus au démarrage d'une version inexistante. Les personnages de fixture sont insérés directement dans MongoDB uniquement par le script de test ; cette préparation n'ajoute aucune route API de développement et ne représente pas une admission de joueur.

```powershell
./scripts/content-workbench/Test-QuestContent.ps1 `
    -ExecutablePath '<repo>/artifacts/content-workbench/build/Arkya.AreaPrototype.exe' `
    -ServerExecutablePath '<repo>/artifacts/content-workbench/build/server/Arkya.AreaPrototype.Server.exe' `
    -OutputRoot '<repo>/artifacts/content-workbench/runs' `
    -DurationSeconds 25
```

Les chemins des deux binaires sont obligatoires ; `DurationSeconds` accepte de 15 à 35 secondes. Le script crée un sous-dossier horodaté avec `verification.json`, les runs `initial-load` et `reload-persisted-progress`, les logs .NET et les preuves du refus `unknown-catalog`. Chaque run conserve son `run.json`, les logs, les rapports Unity et `process-exits.json`. La fixture Docker et les processus possédés sont nettoyés en fin d'exécution ; les secrets temporaires restent dans l'environnement des processus concernés et les valeurs antérieures sont restaurées.

Les rapports de build, logs Unity, rapports des areas/clients et vérifications du script sont les preuves à consulter pour un run donné. Les tests de règles Unity couvrent notamment la validation du catalogue, les règles de kill, le maintien de la version acceptée et l'identité de boot peer. Ce guide décrit les commandes et garanties attendues, sans attribuer un résultat définitif à une exécution non consultée.

## Limites de cette tranche

- Aucun objectif autre que « tuer N ennemis », aucune récompense, économie, inventaire, dialogue ou chaîne de prérequis.
- Acceptation automatique de toutes les quêtes ; pas de choix, d'abandon, de répétition ni de migration d'une quête déjà acceptée.
- Le chargement Unity de ce prototype borne actuellement l'historique à 256 progressions par personnage. Characters ne définit pas encore de quota équivalent : une politique d'archivage/acceptation est nécessaire avant d'accumuler autant de quêtes distinctes.
- Deux personnages mappés par configuration, pas d'admission realm/personnage, de bail de session ou d'authentification de joueur intégrée au prototype.
- Compte admin local partagé, pas de rôles Identity d'opérateurs, d'audit nominatif ni de rotation orchestrée des clés.
- Chargement au démarrage uniquement et arrêt coordonné nécessaire après redémarrage partiel ; pas de journal durable des morts ni de garantie de progression non sauvegardée après crash.
- Contenu limité aux comportements et à l'ennemi déjà pris en charge par le code. Les assets, animations et nouveaux mécanismes nécessitent encore leur propre implémentation et distribution.
