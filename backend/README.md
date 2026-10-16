# Backend Arkya

Identity et Characters établissent le parcours compte/personnage en ASP.NET Core .NET 10. Content et l'administration web ajoutent l'édition de quêtes, leur publication versionnée et le chargement par le dedicated. Characters conserve la progression autoritaire. Voir [le guide des quêtes](../docs/QUEST_CONTENT.md), [l'architecture](../docs/ARCHITECTURE.md) et la [décision de découpage](../docs/decisions/0002-backend-services.md).

## Services et propriété des données

| Application | Adresse locale | Responsabilité | Base MongoDB |
| --- | --- | --- | --- |
| `Arkya.Gateway` | `https://localhost:7140` | YARP : entrée publique commune pour Identity et Characters | Aucune |
| `Arkya.Identity.Api` | `https://localhost:7141` | Discord, comptes, sessions | `arkya_identity` |
| `Arkya.Characters.Api` | `https://localhost:7142` | Création et lecture des personnages ; résolution interne | `arkya_characters` |
| `Arkya.Backend.Api` | `http://localhost:5062` | Ancien bootstrap Unity : annuaire mock et connexion dev | Mémoire uniquement |

Les bases restent sous la responsabilité exclusive de leur service, même si elles partagent le même MongoDB local. Characters valide une session via Identity ; il ne lit pas ses collections. Les DTO publics ne contiennent pas les documents de persistance.

## Configuration locale

Pour le déploiement public sur **`api.arkya.online`**, utiliser le [guide Gateway YARP et k3s](deploy/gateway/README.md). Le Traefik externe pointe vers la gateway à `192.168.100.218:80` ; Identity utilise son profil interne `gateway`. Les anciens [manifests Identity directs](deploy/identity/README.md) restent disponibles. Les Dockerfiles `Dockerfile.identity` et `Dockerfile.gateway` utilisent le contexte `backend/`. Les services reconnaissent les proxies explicitement configurés ; Identity dispose de sondes isolées et de clés persistantes.

Le workflow [GitHub Actions Identity](deploy/identity/GITHUB_ACTIONS.md) exécute les tests et publie l’image Linux AMD64/ARM64 dans GHCR. Il fournit le digest à utiliser pour le déploiement k3s.

Le workflow [GitHub Actions Gateway](deploy/gateway/GITHUB_ACTIONS.md) publie son image séparément. La gateway transmet `/v1/auth/...` à Identity et, si `Gateway:CharactersEnabled=true`, `/v1/characters/...` à la destination Characters configurée. Les chemins et tokens restent identiques. Les routes internes, admin et de développement ne sont pas accessibles par la gateway. Characters utilise `ProxyTrust:KnownNetworks`/`KnownProxies` pour reconnaître la gateway ; Identity conserve les paramètres `ReverseProxy:KnownNetworks`/`KnownProxies`. Les deux services continuent à vérifier eux-mêmes les sessions et droits.

Pour tester la gateway localement, lancer Identity et Characters comme ci-dessous puis `dotnet run --project src/Arkya.Gateway` depuis `backend/`. Son profil local active Characters et utilise leurs destinations HTTPS 7141/7142. Pour tester aussi OAuth à travers la gateway, configurer **dans Identity** `Discord:PublicBaseUrl=https://localhost:7140` et `Discord:RedirectUri=https://localhost:7140/v1/auth/discord/callback`, puis enregistrer ce callback local dans Discord. Ces paramètres remplacent seulement l’origine publique du parcours ; la destination interne YARP reste 7141. La vitrine doit alors utiliser cette même origine de gateway et rester autorisée dans le CORS d’Identity.

Prérequis : SDK .NET 10, PowerShell 7.2 ou plus récent pour les exemples, Docker avec Compose pour MongoDB, une application Discord avec client ID et client secret. Depuis `backend/` :

```powershell
Copy-Item .env.example .env
```

Remplacer les valeurs d'exemple dans `.env` par un nom et un mot de passe MongoDB locaux, puis démarrer MongoDB :

```powershell
docker compose up -d mongo
dotnet dev-certs https --trust
```

Le port MongoDB local est limité à l'interface loopback. Le volume Compose conserve les données. Les variables `MONGO_ROOT_USERNAME` et `MONGO_ROOT_PASSWORD` initialisent l'administrateur lors de la première création du volume ; les modifier ensuite ne change pas le mot de passe déjà enregistré. Les applications .NET ne chargent pas automatiquement ce `.env`.

Configurer leurs secrets séparément. Remplacer les valeurs entre chevrons ; encoder les caractères réservés de l'utilisateur et du mot de passe dans l'URI MongoDB. Ces commandes stockent les secrets hors du dépôt :

```powershell
$identityProject = 'src/Arkya.Identity.Api'
$charactersProject = 'src/Arkya.Characters.Api'
$identityKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$charactersKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

dotnet user-secrets set 'Mongo:ConnectionString' 'mongodb://<user>:<password>@localhost:27017/?authSource=admin' --project $identityProject
dotnet user-secrets set 'Discord:ClientId' '<discord-client-id>' --project $identityProject
dotnet user-secrets set 'Discord:ClientSecret' '<discord-client-secret>' --project $identityProject
dotnet user-secrets set 'ServiceAuthentication:Key' $identityKey --project $identityProject

dotnet user-secrets set 'Mongo:ConnectionString' 'mongodb://<user>:<password>@localhost:27017/?authSource=admin' --project $charactersProject
dotnet user-secrets set 'Identity:ServiceKey' $identityKey --project $charactersProject
dotnet user-secrets set 'ServiceAuthentication:Key' $charactersKey --project $charactersProject
```

Dans le [portail développeur Discord](https://discord.com/developers/applications), enregistrer exactement cette redirect URI OAuth2 :

```text
https://localhost:7141/v1/auth/discord/callback
```

Valeurs par défaut et configuration complémentaire :

| Clé | Service | Valeur / rôle |
| --- | --- | --- |
| `Mongo:DatabaseName` | Identity | `arkya_identity` |
| `Mongo:DatabaseName` | Characters | `arkya_characters` |
| `Discord:RedirectUri` | Identity | Callback HTTPS ci-dessus, identique à celui déclaré chez Discord |
| `Discord:PublicBaseUrl` | Identity | `https://localhost:7141`, origine HTTPS publique de l'autorisation navigateur |
| `Sessions:Lifetime` | Identity | `08:00:00`, configurable entre une durée positive et un jour |
| `Identity:BaseUrl` | Characters | `https://localhost:7141` |
| `ServiceAuthentication:Key` | Les deux | Clé entrante de 32 à 512 caractères pour `X-Arkya-Service-Key` |
| `Identity:ServiceKey` | Characters | Clé sortante identique à `ServiceAuthentication:Key` d'Identity |
| `AllowedHosts` | Identity | `localhost;127.0.0.1` en local ; adapter aux noms DNS du déploiement |

Les clés entrantes Identity et Characters peuvent être différentes. Seuls les services et le futur serveur dédié les détiennent. En déploiement, chaque service doit disposer d'un utilisateur MongoDB limité à sa base ; le compte administrateur Compose sert au développement local. Utiliser le gestionnaire de secrets de l'environnement et des URL HTTPS publiques adaptées. Les nouvelles API refusent HTTP hors de `Development`.

Les identifiants Discord et les clés de service sont obligatoires au démarrage. Les services créent leurs index MongoDB au démarrage et nécessitent une connexion à la base. Lancer chaque API dans un terminal distinct :

```powershell
dotnet run --project src/Arkya.Identity.Api
dotnet run --project src/Arkya.Characters.Api
```

Vérifier leurs sondes :

```powershell
Invoke-RestMethod https://localhost:7141/health
Invoke-RestMethod https://localhost:7142/health
```

`/health` vérifie que l'application répond ; ce n'est pas une vérification de disponibilité de toutes ses dépendances.

## Connexion Discord et session Arkya

La connexion du compte Arkya utilise OAuth2 Authorization Code avec le seul scope `identify`. Le client secret, l'échange du code et l'appel à Discord restent côté backend. Ce parcours ne transmet aucun jeton Discord au client ; la session Arkya reste distincte de l'autorisation du Social SDK décrite ci-dessous. Voir la [documentation OAuth2 Discord](https://docs.discord.com/developers/topics/oauth2).

| Route Identity | Entrée | Réponse principale |
| --- | --- | --- |
| `POST /v1/auth/discord/challenges` | `{ "codeChallenge": "<S256>" }` | `200` : `challengeId`, `authorizationUrl`, `expiresAt`, `pollIntervalSeconds` |
| `GET /v1/auth/discord/authorize/{challengeId}` | Navigation vers `authorizationUrl` | Cookie de corrélation et redirection Discord |
| `GET /v1/auth/discord/callback` | Retour navigateur `code` + `state`, ou erreur fournisseur | Page texte de réussite (`200`) ou refus (`400`) |
| `POST /v1/auth/discord/exchange` | `{ "challengeId": "...", "codeVerifier": "..." }` | `202` en attente ; `200` : `accountId`, `displayName`, `sessionToken`, `expiresAt` |
| `GET /v1/auth/me` | `Authorization: Bearer <sessionToken>` | `200` : `accountId`, `displayName`, `expiresAt` ; sinon `401` |
| `POST /v1/auth/logout` | Même bearer | `204` : session révoquée ; sinon `401` |
| `POST /internal/sessions/introspect` | Clé de service + `{ "sessionToken": "..." }` | `200` : même identité que `/me` ; `403` mauvaise clé ; `401` session invalide |

Le client génère un vérificateur aléatoire de 43 à 128 caractères autorisés (`A-Z`, `a-z`, `0-9`, `-`, `.`, `_`, `~`) et calcule `base64url(SHA256(ASCII(codeVerifier)))`, sans padding. Le `codeChallenge` S256 comporte 43 caractères. Ce protocole de remise est propre à Arkya et ne suppose pas de PKCE dans l'API Discord.

Un challenge expire après cinq minutes. `authorizationUrl` pointe vers l'origine fixe `Discord:PublicBaseUrl`, puis le navigateur est redirigé vers Discord. L'état OAuth et le cookie `Secure`, `HttpOnly`, `SameSite=Lax` lient le callback au navigateur initiateur. L'état et l'échange sont consommés une seule fois. Le callback ne met pas la session Arkya dans l'URL.

Un échange en attente renvoie `{ "status": "pending" }`, `202` et `Retry-After: 5` ; attendre au moins cinq secondes entre les tentatives. Un challenge encore présent mais expiré, déjà consommé ou refusé renvoie `410`. Un challenge inconnu, notamment après son nettoyage TTL, ou un mauvais vérificateur renvoie `400`. Si la réponse de l'échange réussi est perdue, recommencer une connexion : le jeton n'est pas récupérable une seconde fois.

Exercice manuel depuis PowerShell, après configuration des deux services :

```powershell
function ConvertTo-Base64Url([byte[]] $bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}
$verifier = ConvertTo-Base64Url ([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$proof = ConvertTo-Base64Url ([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($verifier)))
$challenge = Invoke-RestMethod -Method Post -Uri 'https://localhost:7141/v1/auth/discord/challenges' `
    -ContentType 'application/json' -Body (@{ codeChallenge = $proof } | ConvertTo-Json)
Start-Process $challenge.authorizationUrl

# Autoriser votre connexion dans le navigateur, puis revenir ici.
$exchangeBody = @{ challengeId = $challenge.challengeId; codeVerifier = $verifier } | ConvertTo-Json
do {
    Start-Sleep -Seconds $challenge.pollIntervalSeconds
    $exchange = Invoke-WebRequest -Method Post -Uri 'https://localhost:7141/v1/auth/discord/exchange' `
        -ContentType 'application/json' -Body $exchangeBody
} while ($exchange.StatusCode -eq 202)
$session = $exchange.Content | ConvertFrom-Json
$playerHeaders = @{ Authorization = "Bearer $($session.sessionToken)" }
Invoke-RestMethod -Uri 'https://localhost:7141/v1/auth/me' -Headers $playerHeaders
```

La session opaque est aléatoire ; MongoDB conserve son empreinte et son expiration, jamais le jeton en clair. La durée par défaut est de huit heures, sans refresh token Arkya dans cette tranche. Chaque validation vérifie explicitement expiration et révocation, même si le nettoyage TTL MongoDB n'a pas encore supprimé le document.

### Connexion depuis la vitrine web

La vitrine réutilise Identity et les mêmes comptes Arkya que le client Unity : création de challenge, autorisation Discord dans un onglet, échange avec preuve S256, puis bearer Arkya pour `/me` et `/logout`. Aucun secret Discord ni clé de service ne doit entrer dans les fichiers statiques du site. Le navigateur n'appelle pas les routes Social SDK pour cette connexion.

La configuration `WebClient:AllowedOrigins` est une liste vide par défaut : aucun accès navigateur interorigine n'est alors autorisé. Pour la vitrine actuellement publiée, définir cette configuration non secrète côté Identity :

```json
{
  "WebClient": {
    "AllowedOrigins": ["https://arkya-atlas.easykrill33.chatgpt.site"]
  }
}
```

Une variable de déploiement équivalente est `WebClient__AllowedOrigins__0=https://arkya-atlas.easykrill33.chatgpt.site`. Ajouter chaque futur domaine personnalisé comme origine exacte seulement lorsqu'il sert réellement la vitrine ; aucune configuration DNS n'est fournie par ce réglage. Les origines doivent utiliser HTTPS, sans slash final, chemin, paramètres, fragment, identifiants ni joker. En `Development` uniquement, `http://localhost:51806` et `http://127.0.0.1:51806` peuvent être ajoutés séparément pour l'aperçu local. Une origine invalide empêche le démarrage.

La politique CORS s'applique uniquement à `POST /v1/auth/discord/challenges`, `POST /v1/auth/discord/exchange`, `GET /v1/auth/me` et `POST /v1/auth/logout`. Elle autorise GET/POST, les en-têtes `Content-Type` et `Authorization`, et expose `Retry-After`. Elle n'autorise pas les cookies interorigine (`Allow-Credentials` absent) et ne couvre ni les routes `/internal`, ni le Social SDK, ni les navigations OAuth. Le frontend doit envoyer ses requêtes avec `credentials: "omit"` et respecter le délai de polling ; le cookie de corrélation appartient à la navigation de premier niveau sur Identity.

**Prérequis de connexion publique :** Identity doit être déployé sur une origine HTTPS joignable par les visiteurs, avec MongoDB, ses secrets et ses noms `AllowedHosts`. Fournir cette origine au frontend, configurer `Discord:PublicBaseUrl` et `Discord:RedirectUri`, puis enregistrer exactement le callback HTTPS `/v1/auth/discord/callback` dans l'application Discord. Le domaine du site vitrine ne déploie pas ce backend. La configuration locale `localhost:7141` ne permet pas de connecter les visiteurs du site public. Un reverse proxy doit transmettre correctement le schéma HTTPS via une configuration de proxies explicitement approuvés ; ne pas faire confiance sans restriction aux en-têtes transférés.

Les options de déploiement Identity sont `ReverseProxy:KnownProxies` (IP), `ReverseProxy:KnownNetworks` (CIDR) et `ReverseProxy:ForwardLimit` (1 par défaut). Une liste vide désactive le transfert. `Management:Port=8081`, avec le listener `ASPNETCORE_HTTP_PORTS=8080;8081`, active uniquement `GET /health/live` et `GET /health/ready` sur ce port : la première vérifie le processus, la seconde MongoDB avec `Management:ReadinessTimeout=00:00:02`. Le filtrage `AllowedHosts` reste applicable aux sondes ; envoyer leur en-tête Host. Aucune route API ne fonctionne sur le port de gestion. `DataProtection:KeysPath` désigne un répertoire absolu persistant pour le trousseau partagé ; vide, il conserve le comportement local existant. Le guide Kubernetes documente les volumes, les probes et le contrôle avant activation du frontend.

## Autorisation Discord Social SDK

Après connexion à Arkya, le client Unity peut demander une autorisation sociale distincte avec les scopes `openid sdk.social_layer identify`. Le scope `identify` permet à Identity de vérifier que le compte Discord social est celui associé à la session Arkya. Le secret Discord reste dans Identity ; conserver l'application Discord en client confidentiel. La [documentation officielle du flux SDK](https://docs.discord.com/developers/discord-social-sdk/development-guides/account-linking-with-discord) décrit les échanges et le callback desktop à ajouter dans le portail : `http://127.0.0.1/callback`. Conserver également le callback HTTPS de connexion existant. Ces paramètres externes ne sont pas modifiés automatiquement.

Les trois routes exigent HTTPS, y compris en développement, et `Authorization: Bearer <sessionToken Arkya>` :

| Route Identity | Entrée | Réponse principale |
| --- | --- | --- |
| `GET /v1/auth/discord/social/config` | Bearer Arkya | `applicationId`, `scopes`, `redirectUri` |
| `POST /v1/auth/discord/social/exchange` | `code`, `codeVerifier`, `redirectUri` obtenus par le SDK | `accessToken`, `refreshTicket`, `tokenType`, `expiresIn`, `scope`, `discordUserId` |
| `POST /v1/auth/discord/social/refresh` | `refreshTicket` précédent | Même réponse, avec le nouveau ticket à conserver |

Le SDK génère le vérificateur PKCE, passe son challenge à `Authorize`, puis transmet le code, le vérificateur et le callback retourné à Identity. Seul un callback HTTP sur `127.0.0.1`, chemin `/callback`, sans query, fragment ni identifiants, est accepté ; le port local choisi par le SDK peut varier. Identity échange le code auprès de Discord, contrôle les scopes de communication et `identify`, lit `/users/@me`, compare l'identifiant au compte Arkya, puis revérifie la session avant de répondre. Le client fournit `accessToken` à `UpdateToken` et le garde uniquement en mémoire.

`refreshTicket` contient le refresh token Discord protégé par ASP.NET Core Data Protection ; il est lié à une session Arkya précise et expire avec elle. Le refresh token brut n'est jamais retourné au client ni enregistré dans MongoDB. Le client conserve ce ticket uniquement en mémoire et remplace le précédent après chaque refresh. La rotation du refresh token reste appliquée par Discord. Une session expirée ou révoquée empêche les échanges futurs ; le client doit aussi déconnecter le SDK et effacer ses jetons lors de la déconnexion Arkya. Cette déconnexion ne révoque pas toutes les autorisations Discord du joueur.

En déploiement, protéger et conserver le trousseau Data Protection de l'application `Arkya.Identity` et le partager entre ses réplicas. Une perte de clés oblige à relancer l'autorisation sociale. Les réponses sont `no-store` ; le client HTTP social désactive ses loggers. Ne pas ajouter de journalisation des corps, des en-têtes d'autorisation ou des tickets dans les proxys et outils d'observabilité.

Erreurs principales : `400 invalid_social_exchange`, `400 invalid_refresh_ticket`, `400 discord_authorization_rejected`, `401` session invalide, `403 discord_account_mismatch`, `403 discord_social_scope_required`, `502 invalid_discord_response`, `503 discord_unavailable`. Un refus OAuth, un refresh invalide ou une révocation demande une nouvelle autorisation explicite ; cela ne supprime ni le compte Arkya ni ses personnages. Les tests automatisés simulent Discord ; la validation avec le SDK et une application autorisée reste distincte.

## Gestion des personnages

Les routes de personnages exigent un bearer Arkya valide. Seul le catalogue d'apparence en lecture seule est anonyme. L'identité du propriétaire vient exclusivement de l'introspection Identity. Une indisponibilité d'Identity empêche l'opération et produit `503`.

| Route Characters | Entrée | Réponse principale |
| --- | --- | --- |
| `GET /v1/characters/appearance-options` | Aucune authentification | `200` : version, valeurs par défaut, races, silhouettes, coiffures et palettes autorisées |
| `GET /v1/characters` | Bearer | `200` : tableau des personnages du compte |
| `POST /v1/characters` | Bearer + `name` et `appearance` (exemple ci-dessous) | `201` : personnage ; `400` nom ou apparence invalide ; `409` nom déjà pris |
| `GET /v1/characters/{id}` | Bearer | `200` : personnage ; `404` absent ou autre propriétaire |
| `POST /internal/characters/{id}/resolve` | Clé de service + `{ "sessionToken": "..." }` | `200` : `accountId`, `character`, `sessionExpiresAt` ; `403` mauvaise clé ; `401` session invalide ; `404` personnage non accessible |

Un personnage public contient `id` (GUID), `name`, `level`, `createdAt` et `appearance`. L'apparence complète est retournée par la création, la liste, la lecture individuelle et la résolution interne. Le nom est nettoyé des espaces autour, puis doit comporter de 3 à 20 lettres ASCII `A-Z`/`a-z`. Son unicité globale, insensible à la casse, est protégée par un index MongoDB ; deux créations concurrentes ne peuvent pas obtenir le même nom. Le niveau initial `1` est imposé côté service. Les champs JSON inconnus, par exemple `accountId` ou `level` dans une création, sont refusés, y compris dans `appearance`.

Contrat de création avec personnalisation :

```json
{
  "name": "Aeloria",
  "appearance": {
    "version": 1,
    "race": "elf",
    "silhouette": "b",
    "heightCm": 185,
    "skin": "warm",
    "hairStyle": "braid",
    "hairColor": "brown",
    "eyeColor": "green",
    "outfitColor": "sage"
  }
}
```

Le service valide une liste fermée d'identifiants, sensible à la casse, issue du [manifeste artistique](../docs/art-direction/resources/personnages/manifest.json). Les races sont `human` et `elf`, les silhouettes `a` et `b`, les coiffures `short`, `long` et `braid`. `heightCm` est un entier : 160–195 pour un humain, 170–205 pour un elfe. Les huit teints, huit couleurs de cheveux, six couleurs d'yeux et quatre couleurs de tenue sont exposés avec leurs libellés et couleurs hexadécimales dans le catalogue. Le client envoie leurs identifiants, jamais une couleur libre ni un chemin d'asset. Une apparence partielle, une version inconnue ou une option hors catalogue donne `400` avec des erreurs `appearance.<champ>` ; elle n'est pas enregistrée. Ces choix sont cosmétiques et ne déterminent ni les statistiques ni le déplacement.

Pour préserver les anciens clients, `appearance` omis ou `null` donne le profil stable `human/a/175/warm/short/brown/green/sage`, version 1. MongoDB écrit les nouveaux personnages avec `SchemaVersion = 2` et un sous-document `Appearance`. Les documents historiques de schéma 1 sans apparence sont lus avec ce même profil par défaut ; leur identité, propriétaire et niveau ne changent pas. La conversion en document de persistance écrit le schéma 2. Aucun lot de migration destructif n'est nécessaire ; les schémas inconnus ou les apparences corrompues d'un document actuel ne sont pas remplacés silencieusement.

Le catalogue répond sous la forme `{ version, defaults, races, silhouettes, hairStyles, palettes }`. Une race contient `{ id, label, height: { min, max, default } }`, une silhouette/coiffure `{ id, label }`, et chaque palette (`skin`, `hair`, `eyes`, `outfit`) est une liste de `{ id, label, hex }`. Il ne contient aucune donnée de compte et n'effectue pas d'introspection Identity.

Après l'exemple de connexion :

```powershell
$character = Invoke-RestMethod -Method Post -Uri 'https://localhost:7142/v1/characters' `
    -Headers $playerHeaders -ContentType 'application/json' -Body '{"name":"Aeloria"}'
Invoke-RestMethod -Uri 'https://localhost:7142/v1/characters' -Headers $playerHeaders
Invoke-RestMethod -Uri "https://localhost:7142/v1/characters/$($character.id)" -Headers $playerHeaders
Invoke-RestMethod -Method Post -Uri 'https://localhost:7141/v1/auth/logout' -Headers $playerHeaders
```

Choisir un autre nom si `Aeloria` existe déjà. Le compte ne dispose pas encore d'un quota de personnages ; suppression, renommage et modification d'apparence après création restent à définir. La progression de quêtes utilise ses routes internes dédiées.

La résolution interne vérifie aussi le propriétaire. Elle ne délivre ni ticket d'admission ni bail de personnage et ne prévient pas les connexions simultanées. Le serveur dédié ne doit pas la traiter comme une preuve d'admission au monde.

## API locale historique

Le bootstrap Unity existant continue de cibler `Arkya.Backend.Api` :

```powershell
dotnet run --project src/Arkya.Backend.Api
Invoke-RestMethod http://localhost:5062/health
Invoke-RestMethod http://localhost:5062/servers
Invoke-RestMethod -Method Post -Uri 'http://localhost:5062/dev/auth/login' `
    -ContentType 'application/json' -Body '{"displayName":"Local Player"}'
```

`POST /dev/auth/login` est enregistré uniquement dans `Development`. Ses sessions prévisibles en mémoire ne sont pas des identifiants Identity. `GET /servers` reste une liste mock derrière `IGameServerDirectory`, configurée par `MockServerList:LocalDevHost`, `MockServerList:LocalDevPort` et `MockServerList:LocalDevMaxPlayers`. Voir [Enter World](../docs/ENTER_WORLD_FLOW.md).

## Vérification et limites

```powershell
dotnet build Arkya.Backend.slnx
dotnet test Arkya.Backend.slnx
```

Les tests de persistance MongoDB peuvent être activés avec une instance joignable et l'URI dans `ARKYA_TEST_MONGO` avant `dotnet test`. Ils utilisent des bases de test isolées ; ne jamais fournir une base de production. Sans cette variable, ces scénarios d'intégration ne sont pas exécutés. `Arkya.Backend.Flow.Tests` vérifie aussi le parcours entre les deux API et MongoDB : autorisation Discord simulée, session, création, résolution par le serveur et révocation.

Les tests automatisés n'autorisent pas une vraie application Discord. Une validation réelle du fournisseur nécessite ses identifiants, le callback déclaré et un passage manuel dans le navigateur. Le client Unity et un serveur dédié ne sont pas intégrés ni vérifiés par cette tranche backend.

Avant ouverture publique, compléter l'admission du serveur, les identités et la rotation des clés de service, les politiques de compte et une confirmation utilisateur du challenge dans le navigateur pour limiter l'approbation d'un lien de connexion transmis par un tiers. Le cookie de corrélation lie le callback à un navigateur ; il ne prouve pas à lui seul que ce navigateur appartient à l'initiateur natif du challenge.

Le transport courant est HTTP JSON. Le passage à gRPC unary pour les appels internes dépendra de mesures sur le build Unity dédié ; le streaming attend un besoin d'événements concret. Les écritures de persistance ne doivent pas suivre chaque tick Netcode.
