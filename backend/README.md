# Backend Arkya

Le site et l’admin disposent de [sessions renouvelables](../docs/AUTH_SESSIONS.md),
avec cookies HttpOnly, rotation et révocation. Admin nécessite désormais sa propre
base de sessions `arkya_admin` ; provisionner sa connexion MongoDB avant déploiement.

La [console d'administration](../docs/ADMIN_CONSOLE.md) regroupe le dashboard,
les comptes et accès bêta, la modération, les personnages et les déploiements
k3s. Le [profil facultatif admin-console](deploy/gitops/admin-console/README.md)
raccorde les API privées sur la topologie bêta complète.

Identity et Characters établissent le parcours compte/personnage en ASP.NET Core .NET 10. Content et l'administration web ajoutent l'édition de quêtes, leur publication versionnée et le chargement par le dedicated. Characters conserve la progression autoritaire. Voir [le guide des quêtes](../docs/QUEST_CONTENT.md), [l'architecture](../docs/ARCHITECTURE.md) et la [décision de découpage](../docs/decisions/0002-backend-services.md).

Content possède aussi les [actualités et notes de version](../docs/PUBLICATIONS.md).
L'Admin prépare, publie et dépublie les brouillons ; le launcher consulte un feed
public anonyme. Les changements non publiés restent privés et les notes de version
portent explicitement la version du jeu concernée.

## Services et propriété des données

Les [bugs (F8) et suggestions (F9)](../docs/BUG_REPORTS.md) ajoutent `Arkya.Reports.Api`
(`arkya_reports`), le suivi dans Admin et un MCP administrateur protégé
sur `/mcp/reports`. Le [composant Reports](deploy/gitops/reports-control/README.md)
complète le profil Admin existant ; la CI promeut son digest avec les autres services.

Le [backoffice de déploiement des realms sur k3s](deploy/realms/README.md) ajoute `Arkya.Realms.Api` et l'écran **Realms** d'`Arkya.Admin` : définitions dans `arkya_realms`, demandes durables et observation Kubernetes. Le module est activé explicitement dans Admin. Le backend de déploiement reste interne ; seul le backoffice peut être publié sur son domaine via la gateway. Voir [l'architecture réseau et le périmètre](../docs/REALM_DEPLOYMENT.md).

Après le frontend et Discord, le [profil Argo `admin`](deploy/gitops/admin/README.md) ajoute Admin, Content et Realms dans `arkya-beta`, sur le même YARP. Les deux modules sont activés ; l'exécution des dedicateds attend leurs profils et autorisations. Ce profil termine TLS dans Traefik et active explicitement HTTP privé pour les échanges Admin/API, avec contrôle du pair réseau et clés distinctes. L'admin utilise Discord avec une liste explicite d'identifiants utilisateur autorisés, distincte de la connexion joueur.

| Application | Adresse locale | Responsabilité | Base MongoDB |
| --- | --- | --- | --- |
| `Arkya.Gateway` | `https://localhost:7140` | YARP : routes publiques explicitement autorisées, dont Identity, Characters et publications Content | Aucune |
| `Arkya.Identity.Api` | `https://localhost:7141` | Discord, comptes, sessions | `arkya_identity` |
| `Arkya.Characters.Api` | `https://localhost:7142` | Personnages, admissions, baux et position serveur persistante | `arkya_characters` |
| `Arkya.Content.Api` | `https://localhost:7143` | Quêtes et catalogues, brouillons d'actualités et notes de version, feed publié | `arkya_content` |
| `Arkya.Realms.Api` | `https://localhost:7144` | Déploiement et catalogue interne des destinations approuvées | `arkya_realms` |
| `Arkya.Backend.Api` | `http://localhost:5062` | Ancien bootstrap Unity : annuaire mock et connexion dev | Mémoire uniquement |

Les bases restent sous la responsabilité exclusive de leur service, même si elles partagent le même MongoDB local. Characters valide une session via Identity ; il ne lit pas ses collections. Les DTO publics ne contiennent pas les documents de persistance.

## Configuration locale

Pour le déploiement public sur **`api.arkya.online`**, utiliser le [guide Gateway YARP et k3s](deploy/gateway/README.md). Le Traefik externe pointe vers la gateway à `192.168.100.218:80` ; Identity utilise son profil interne `gateway`. Les anciens [manifests Identity directs](deploy/identity/README.md) restent disponibles. Les Dockerfiles `Dockerfile.identity` et `Dockerfile.gateway` utilisent le contexte `backend/`. Les services reconnaissent les proxies explicitement configurés ; Identity dispose de sondes isolées et de clés persistantes.

Le workflow [GitHub Actions Identity](deploy/identity/GITHUB_ACTIONS.md) exécute les tests et publie l’image Linux AMD64/ARM64 dans GHCR. Il fournit le digest à utiliser pour le déploiement k3s.

Le workflow [GitHub Actions Gateway](deploy/gateway/GITHUB_ACTIONS.md) publie son image séparément. La gateway transmet `/v1/auth/...` à Identity et, si `Gateway:CharactersEnabled=true`, `/v1/characters/...` à la destination Characters configurée. `Gateway:ContentEnabled=true` ajoute uniquement `GET /v1/content/posts` et `GET /v1/content/posts/{id}`, avec leur préflight `OPTIONS`. Le profil GitOps Admin configure leur destination Content privée. Les chemins et tokens restent identiques. Les routes internes, de gestion Content/Realms et de développement ne sont pas accessibles sur le domaine API ; le backoffice reste sur son domaine distinct. Characters utilise `ProxyTrust:KnownNetworks`/`KnownProxies` pour reconnaître la gateway ; Identity conserve les paramètres `ReverseProxy:KnownNetworks`/`KnownProxies`. Les services continuent à vérifier eux-mêmes les sessions et droits.

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
| `POST /v1/auth/discord/challenges` | `{ "codeChallenge": "<S256>", "returnUrl": "<page Compte autorisée, facultatif>" }` | `200` : `challengeId`, `authorizationUrl`, `expiresAt`, `pollIntervalSeconds`, `returnUrl` si fourni |
| `GET /v1/auth/discord/authorize/{challengeId}` | Navigation vers `authorizationUrl` | Cookie de corrélation et redirection Discord |
| `GET /v1/auth/discord/callback` | Retour navigateur `code` + `state`, ou erreur fournisseur | Redirection vers le `returnUrl` du challenge après validation de l’état et du cookie ; sinon réponse texte historique (`200` ou `400`) |
| `POST /v1/auth/discord/exchange` | `{ "challengeId": "...", "codeVerifier": "..." }` | `202` en attente ; `200` : `accountId`, `displayName`, `sessionToken`, `expiresAt` |
| `GET /v1/auth/me` | `Authorization: Bearer <sessionToken>` | `200` : `accountId`, `displayName`, `expiresAt` ; sinon `401` |
| `POST /v1/auth/refresh` | Cookie `arkya.refresh`, `Origin` autorisé et `X-Arkya-Refresh: 1` | `200` : nouvel accès, cookie remplacé ; `401` expiré/révoqué ; `403` origine/en-tête refusé |
| `POST /v1/auth/logout` | Bearer natif, ou cookie + origine/en-tête refresh navigateur | `204` : session/famille révoquée et cookie supprimé ; sinon `401`/`403` |
| `POST /internal/sessions/introspect` | Clé de service + `{ "sessionToken": "..." }` | `200` : même identité que `/me` ; `403` mauvaise clé ; `401` session invalide |
| `POST /internal/sessions/introspect-reference` | Même clé + `{ "sessionReference": "<SHA256 hex64>" }` | Validation privée par empreinte : même identité/expiration, sans bearer ; `401` si expirée/révoquée |

Le client génère un vérificateur aléatoire de 43 à 128 caractères autorisés (`A-Z`, `a-z`, `0-9`, `-`, `.`, `_`, `~`) et calcule `base64url(SHA256(ASCII(codeVerifier)))`, sans padding. Le `codeChallenge` S256 comporte 43 caractères. Ce protocole de remise est propre à Arkya et ne suppose pas de PKCE dans l'API Discord.

Un challenge expire après cinq minutes. `authorizationUrl` pointe vers l'origine fixe `Discord:PublicBaseUrl`, puis le navigateur est redirigé vers Discord. L'état OAuth et le cookie `Secure`, `HttpOnly`, `SameSite=Lax` lient le callback au navigateur initiateur. L'état et l'échange sont consommés une seule fois. Le callback ne met pas la session Arkya dans l'URL.

Pour le site, `returnUrl` est facultatif et limité à une origine exacte de `WebClient:AllowedOrigins`, chemin `/account.html`, avec seulement les paramètres facultatifs `lang=fr|en` et `returnTo=play.html`, sans doublon ni fragment. Une destination invalide est refusée avant création du challenge. Le retour est persisté avec le challenge et utilisé uniquement après consommation valide de l’état OAuth et du cookie, y compris lors d’un refus Discord ou d’une erreur du fournisseur. Aucun code, état, jeton ou vérificateur n’est ajouté à cette redirection. Les clients natifs qui omettent ce champ conservent leur parcours existant.

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

La vitrine réutilise Identity et les mêmes comptes Arkya que le client Unity : création de challenge, autorisation Discord dans le même onglet, retour automatique sur la page Compte, échange avec preuve S256 puis validation du bearer Arkya par `/me`. Le challenge web est conservé temporairement dans `sessionStorage` pour survivre à la navigation ; il est lié à l’origine API et effacé après utilisation ou annulation. Aucun secret Discord ni clé de service ne doit entrer dans les fichiers statiques du site. Le navigateur n'appelle pas les routes Social SDK pour cette connexion.

La configuration `WebClient:AllowedOrigins` est une liste vide par défaut : aucun accès navigateur interorigine n'est alors autorisé. Pour la vitrine actuellement publiée, définir cette configuration non secrète côté Identity :

```json
{
  "WebClient": {
    "AllowedOrigins": ["https://arkya-atlas.easykrill33.chatgpt.site"]
  }
}
```

Une variable de déploiement équivalente est `WebClient__AllowedOrigins__0=https://arkya-atlas.easykrill33.chatgpt.site`. Ajouter chaque futur domaine personnalisé comme origine exacte seulement lorsqu'il sert réellement la vitrine ; aucune configuration DNS n'est fournie par ce réglage. Les origines doivent utiliser HTTPS, sans slash final, chemin, paramètres, fragment, identifiants ni joker. En `Development` uniquement, `http://localhost:51806` et `http://127.0.0.1:51806` peuvent être ajoutés séparément pour l'aperçu local. Une origine invalide empêche le démarrage.

La politique CORS s'applique à `POST /v1/auth/discord/challenges`, `POST /v1/auth/discord/exchange`, `GET /v1/auth/me`, `POST /v1/auth/refresh` et `POST /v1/auth/logout`, pour les origines exactes configurées. Elle autorise GET/POST et les en-têtes `Content-Type`, `Authorization`, `X-Arkya-Refresh`, expose `Retry-After` et permet les credentials. Le frontend utilise `credentials: "include"` uniquement pour l'échange navigateur avec `useRefreshCookie: true`, le refresh et la déconnexion, avec `X-Arkya-Refresh: 1` ; les autres requêtes restent en `credentials: "omit"`. Ces opérations sur le cookie exigent également une origine autorisée côté serveur. Le refresh reste dans un cookie HttpOnly, jamais dans le JSON ou le stockage JavaScript. Cette politique ne couvre ni `/internal`, ni le Social SDK, ni les navigations OAuth ; le cookie de corrélation appartient à la navigation de premier niveau sur Identity. Voir le [contrat des sessions renouvelables](../docs/AUTH_SESSIONS.md).

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
| `GET /v1/characters/entry-context` | Bearer | `200` : présence de personnages et dernier realm admis par un dedicated ; [contrat](../docs/CHARACTER_ENTRY.md) |
| `POST /v1/characters` | Bearer + `name` et `appearance` (exemple ci-dessous) | `201` : personnage ; `400` nom ou apparence invalide ; `409` nom déjà pris |
| `GET /v1/characters/{id}` | Bearer | `200` : personnage ; `404` absent ou autre propriétaire |
| `POST /internal/characters/{id}/resolve` | Clé de service + `{ "sessionToken": "..." }` | `200` : `accountId`, `character`, `sessionExpiresAt` ; `403` mauvaise clé ; `401` session invalide ; `404` personnage non accessible |

Un personnage public contient `id` (GUID), `name`, `level`, `createdAt`, `appearance` et `realmId` (nullable). L'apparence complète est retournée par la création, la liste, la lecture individuelle et la résolution interne. Le nom est nettoyé des espaces autour, puis doit comporter de 3 à 20 lettres ASCII `A-Z`/`a-z`. Son unicité globale, insensible à la casse, est protégée par un index MongoDB ; deux créations concurrentes ne peuvent pas obtenir le même nom. Le niveau initial `1` est imposé côté service. Les champs JSON inconnus, par exemple `accountId` ou `level` dans une création, sont refusés, y compris dans `appearance`.

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

Pour préserver les anciens clients, `appearance` omis ou `null` donne le profil stable `human/a/175/warm/short/brown/green/sage`, version 1. MongoDB écrit les nouveaux personnages avec `SchemaVersion = 2` et un sous-document `Appearance`, ou le schéma 3 si un realm est choisi à la création. Les documents historiques de schéma 1 sans apparence sont lus avec ce même profil par défaut ; leur identité, propriétaire et niveau ne changent pas. La liaison atomique ultérieure ajoute `RealmId` sans réécrire les autres champs ni le schéma historique. Aucun lot de migration destructif n'est nécessaire ; les schémas inconnus ou les apparences corrompues d'un document actuel ne sont pas remplacés silencieusement.

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

La résolution interne historique vérifie le propriétaire, mais seule la consommation d'un ticket d'admission ci-dessous donne le droit de créer un acteur dans le monde.

## Admission et jeu web bêta

Le [contrat HTTP exact](../docs/WEB_BETA_CONTRACT.md) définit le catalogue authentifié `GET /v1/realms`, l'émission `POST /v1/characters/{id}/admissions`, puis les routes privées `/internal/game/...` utilisées par le dedicated. La gateway expose le catalogue et l'admission lorsque Characters est activé ; elle bloque les routes privées et retire la clé dedicated des requêtes publiques.

La création accepte un `realmId` facultatif. S'il est absent, la première admission lie atomiquement le personnage au realm choisi ; cette liaison reste ensuite immuable. Le catalogue vient du vrai service Realms et refuse une destination non compatible ou sans identité dedicated configurée. `GameAccess:StandaloneEntries` dans Realms permet un serveur réel lancé hors Kubernetes ; une entrée doit être activée explicitement par l'opérateur. Les profils Kubernetes conservent UDP par défaut et acceptent un profil `WebSocket` distinct avec une URL WSS approuvée.

Characters conserve seulement l'empreinte SHA-256 d'un ticket aléatoire de 256 bits, valable 60 secondes au maximum. La consommation atomique crée un bail exclusif de 90 secondes, borné par l'expiration de session, renouvelé par le serveur. `X-Arkya-Dedicated-Key` est lié dans Characters à un couple realm/serveur précis. L'époque d'autorité et la révision protègent la sauvegarde de position contre un ancien serveur ou une écriture concurrente. Seul le dedicated fournit position et direction ; le client ne peut pas appeler ces écritures. La collection `game_leases` conserve aussi la dernière position durable et n'a donc pas de TTL destructif.

Le [contexte d'entrée](../docs/CHARACTER_ENTRY.md) conserve également la dernière admission réussie du compte. Le dedicated enregistre `LastConnectedAt` dans la même écriture atomique que la consommation du ticket ; demander un ticket ou choisir un royaume ne modifie pas cet historique. Le client natif peut ainsi retrouver le dernier royaume après connexion, tout en proposant la sélection de royaume si le compte n'a pas de personnage, aucun historique ou une destination devenue indisponible.

Configurer dans Characters `Realms:BaseUrl`, `Realms:ServiceKey`, `GameAccess:DedicatedServers` et la liste exacte `WebClient:AllowedOrigins`, en complément d'Identity/Mongo. Les origines autorisées doivent inclure celle de la page qui héberge réellement Unity, qui peut différer de la vitrine. Le bearer Arkya reste en mémoire. Les clés dedicated et interservices restent dans les secrets serveur. Voir [la préparation WSS et des services](deploy/web-beta/README.md) pour les valeurs et les frontières réseau.

La déconnexion de la page ferme Unity et le dedicated libère son bail. Characters conserve une référence hachée de session calculée après authentification, puis la revalide auprès d'Identity à la consommation, au heartbeat et à la sauvegarde, sans cache et avec un timeout HTTP de cinq secondes. Un logout refuse ainsi un ticket en attente et les prochaines opérations du bail, même si le client a été modifié ; le dedicated déconnecte lors du refus définitif. Les expirations ne peuvent jamais dépasser la borne de session initiale. Release reste permis après révocation pour libérer proprement le personnage. Les anciens enregistrements sans référence doivent être libérés/expirés puis réadmis ; leur état sauvegardé est conservé. La rotation des clés demande actuellement un redémarrage des services concernés.

### Fixture locale avec les vrais services

PowerShell 7.4 et MongoDB réel sont requis. La fixture lance les vrais Identity, Characters et Realms ; elle sème explicitement un compte/session dans trois bases de test isolées, puis crée deux personnages par les routes authentifiées. Elle ne contacte pas Discord et n'ajoute aucun contournement d'authentification en production.

```powershell
# Depuis la racine du dépôt ; utiliser exclusivement une instance MongoDB de test.
$env:ARKYA_TEST_MONGO = 'mongodb://127.0.0.1:27029'
$fixtureDirectory = Join-Path $env:LOCALAPPDATA 'Arkya/WebBetaTests/my-run'
./backend/tools/Start-WebBetaFixture.ps1 -Action Start -Directory $fixtureDirectory
# Émettre les tickets juste avant le test Unity (validité 60 secondes).
./backend/tools/Start-WebBetaFixture.ps1 -Action Tickets -Directory $fixtureDirectory
$env:ARKYA_WEB_BETA_TICKETS_FILE = Join-Path $fixtureDirectory 'tickets.json'
# Après le test Unity et sa déconnexion : vérifier les deux sauvegardes et l'absence de bail actif.
./backend/tools/Start-WebBetaFixture.ps1 -Action VerifyState -Directory $fixtureDirectory
# Après arrêt du dedicated/client : révoquer la fixture et supprimer ses bases isolées.
./backend/tools/Start-WebBetaFixture.ps1 -Action Stop -Directory $fixtureDirectory
```

Le fichier `tickets.json` contient les vrais DTO et une clé serveur ; il reste hors dépôt dans un dossier privé et ne doit pas être affiché ou copié dans un résultat de test. L'endpoint local par défaut est `ws://127.0.0.1:17943/` ; définir `ARKYA_TEST_GAME_ENDPOINT` avant `Start` pour en choisir un autre. La fixture ne lance pas Unity et ne remplace pas le test WSS public. `Restart` recompile/redémarre les API en conservant les mêmes bases, personnages et session, après arrêt des clients. `Stop` vérifie l'identité des processus avant de les terminer, supprime uniquement les bases préfixées de cette fixture, puis ses fichiers de session et de tickets.

`VerifyState` lit seulement les deux états dans la base MongoDB isolée : révision et époque positives, coordonnées finies, aucun bail actif. Il ne réémet ni ne consomme de ticket et échoue si une assertion manque. Sa sortie contient uniquement le nombre d'états, les révisions, époques et positions non secrètes. Cette lecture de test ne crée aucune route de développement dans les API de production.

La session semée expire après deux heures. Pour un long scénario local, une prolongation explicite peut faire partie de la préparation du test : intervenir uniquement dans la base Identity portant le préfixe isolé de la fixture, vérifier simultanément l'empreinte du jeton, le compte et l'expiration précédente, puis exiger exactement une mise à jour conditionnelle. Conserver le jeton et les clés ; ne modifier ni leases ni personnages. Vérifier ensuite la nouvelle expiration par le vrai `GET /v1/auth/me` et recharger le navigateur. Consigner l'ancienne/nouvelle expiration et le nombre de documents modifiés sans afficher de secret. Cette opération de setup ne teste pas OAuth et n'est pas une fonction de renouvellement proposée par l'API ; elle n'augmente pas les bornes de session déjà enregistrées dans les leases existantes.

Pour vérifier la révocation, créer une **autre fixture isolée**, puis lancer `-Action VerifyRevocation`. Ce scénario consomme un ticket, sauvegarde, renouvelle, appelle le vrai logout et vérifie les refus du second ticket, du heartbeat, de la sauvegarde et des accès publics ; il exige aussi la libération du bail et la conservation de l'état. Il révoque volontairement la session de cette fixture, qu'il faut ensuite nettoyer avec `Stop`. Le paramètre `-BuildConfiguration Release` permet de construire/lancer cette seconde fixture sans verrouiller les binaires Debug d'un autre test ; utiliser la même configuration pour ses commandes suivantes.

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

Les tests automatisés n'autorisent pas une vraie application Discord. Une validation réelle du fournisseur nécessite ses identifiants, le callback déclaré et un passage manuel dans le navigateur. La fixture ci-dessus permet de vérifier Unity et un dedicated contre de vrais services, sans prétendre vérifier OAuth ni le TLS du domaine public.

Avant ouverture publique, provisionner et valider les destinations WSS, les identités et la rotation des clés de service, les politiques de compte et une confirmation utilisateur du challenge dans le navigateur pour limiter l'approbation d'un lien de connexion transmis par un tiers. Le cookie de corrélation lie le callback à un navigateur ; il ne prouve pas à lui seul que ce navigateur appartient à l'initiateur natif du challenge.

Le transport courant est HTTP JSON. Le passage à gRPC unary pour les appels internes dépendra de mesures sur le build Unity dédié ; le streaming attend un besoin d'événements concret. Les écritures de persistance ne doivent pas suivre chaque tick Netcode.

## Agents Windows et workers LFM

`Arkya.Inference.Api` ajoute un registre MongoDB de machines d'inférence. La page **Agents LFM** d'Admin fournit une clé d'installation temporaire : le script Windows enregistre automatiquement la machine et protège son jeton permanent localement. L'opérateur peut ajouter, supprimer ou modifier les workers, choisir LFM ou Qwen et régler leurs threads. L'agent reçoit les commandes par heartbeat sortant et remplace uniquement le worker modifié après son dialogue en cours. Le package comprend la démo PNJ locale et les scripts d'installation/démarrage automatique.

Les instructions, la configuration du service et la vérification se trouvent dans [INFERENCE_AGENTS.md](../docs/INFERENCE_AGENTS.md). Sources du package : [`tools/inference-agent/`](tools/inference-agent/README.md). Le composant GitOps [`deploy/gitops/inference-control/`](deploy/gitops/inference-control/README.md) complète le profil Admin existant ; la CI publie et promeut son image avec Admin/Gateway avant son activation par Argo.
