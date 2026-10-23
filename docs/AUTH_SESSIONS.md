# Sessions renouvelables du site et de l’admin

Implémentation du 22 octobre 2026. Les sessions joueur et administrateur restent
séparées ; un compte du site ne donne aucun droit d’administration.

## Site public

La connexion Discord conserve le challenge S256 et le retour dans le même onglet.
L’échange navigateur ajoute `useRefreshCookie: true`, `credentials: include`
et l’en-tête `X-Arkya-Refresh: 1`. Identity retourne le bearer d’accès habituel
et `refreshExpiresAt`, puis pose `arkya.refresh` : HttpOnly, Secure, SameSite=Lax,
sans Domain et avec le chemin `/v1/auth`. Aucun refresh token n’est retourné au
JavaScript ni stocké dans sessionStorage/localStorage.

| Opération | Contrat navigateur |
| --- | --- |
| `POST /v1/auth/discord/exchange` | Challenge, vérificateur et `useRefreshCookie: true` ; cookie initial après échange réussi |
| `POST /v1/auth/refresh` | Corps `{}`, cookie et `X-Arkya-Refresh: 1` ; `200` avec nouveau bearer/cookie, ou `401` |
| `POST /v1/auth/logout` | Cookie et même en-tête ; bearer facultatif ; révocation de toute la famille et suppression du cookie |

Chaque opération utilisant le cookie exige un `Origin` présent exactement dans
`WebClient:AllowedOrigins`. CORS autorise les credentials et l’en-tête dédié
uniquement pour ces origines. Les réponses d’authentification sont `no-store`.
Le site renouvelle à une minute de l’expiration, au retour visible et à la
restauration d’un nouvel onglet. Un `/me` refusé peut également déclencher un
renouvellement. Les appels simultanés d’un onglet partagent leur résultat ;
Web Locks sérialise échange, rotation et déconnexion entre onglets de même origine.

Les cookies SameSite=Lax fonctionnent pour `https://arkya.online` et
`https://api.arkya.online`. Une vitrine sur un domaine tiers, notamment l’ancienne
adresse `chatgpt.site`, ne bénéficie pas de cette persistance intersite. Ne pas
remplacer silencieusement le cookie par un refresh token dans le stockage JS.

Identity conserve une famille dans `refresh_sessions`, seulement les empreintes
des secrets et les métadonnées nécessaires. Une rotation compare et remplace
atomiquement l’empreinte courante dans MongoDB. La réutilisation d’un ancien
secret révoque la famille, y compris ses bearers encore valables. Un secret
inventé ne permet pas de révoquer une famille. Bans, version de session du compte,
révocation et expiration sont vérifiés lors de l’accès, avant le nettoyage TTL.
Une famille est limitée à 4 096 rotations puis demande une nouvelle connexion.
Ce choix suit le principe de rotation/détection de rejeu de
[RFC 9700, section 4.14](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14).

| Option Identity | Valeur par défaut |
| --- | --- |
| `Sessions:Lifetime` | 8 heures, clients natifs et échanges historiques |
| `Sessions:RefreshAccessLifetime` | 8 heures, accès navigateur renouvelable |
| `Sessions:RefreshLifetime` | 30 jours absolus depuis la connexion Discord |

Une rotation ne repousse jamais la limite absolue. Le client natif existant reste
compatible sans demander le cookie. Le Social SDK Discord possède un autre
protocole de renouvellement, indépendant.

### Session du jeu web

Unity et la lease Characters restent liés au bearer remis lors du chargement.
Le site peut renouveler son propre accès, mais le jeu chargé s’arrête à l’échéance
de son bearer initial (8 heures par défaut). Recharger Jouer utilise la session
renouvelée sans repasser par Discord. La déconnexion révoque aussi l’ancien bearer
du jeu via sa famille. Cette livraison ne modifie ni l’admission ni les contrats
Unity pour prolonger une partie en cours ; ne pas raccourcir l’accès à quelques
minutes avant de traiter cette intégration.

## Administration

Admin possède `arkya_admin.admin_sessions`, avec index TTL et schéma versionné.
Son cookie d’accès `Arkya.Admin` dure 30 minutes par défaut. Le cookie
`Arkya.Admin.Refresh` est HttpOnly, Secure en production et SameSite=Strict,
avec une échéance absolue de 30 jours. Le middleware renouvelle après la moitié
de la durée d’accès, ou si l’accès a expiré, sans nouvelle navigation Discord.
Le rôle reste vérifié contre l’allowlist Discord actuelle ou la version du mot
de passe opérateur ; sa modification invalide les sessions concernées.

La rotation MongoDB est atomique. Une tolérance de 10 secondes permet aux requêtes
concurrentes portant le dernier cookie consommé de terminer sans recevoir de
nouveaux identifiants. Un rejeu plus ancien révoque la session. Les formulaires
gardent leur antiforgery. Les sessions MCP de service restent indépendantes.

| Option Admin | Valeur par défaut / rôle |
| --- | --- |
| `Mongo:ConnectionString` | Obligatoire ; secret, jamais versionné |
| `Mongo:DatabaseName` | `arkya_admin` |
| `AdminSessions:AccessLifetime` | `00:30:00` |
| `AdminSessions:RefreshLifetime` | `30.00:00:00` |

Il n’existe aucun remplacement silencieux de MongoDB par un stockage mémoire.
Les anciens cookies admin demandent une nouvelle connexion après migration.

## Activation

1. Le profil GitOps `deploy/gitops/admin` référence la clé existante
   `Mongo__ConnectionString` du Secret `arkya-content-secrets`, comme les autres
   services du profil. Admin sélectionne sa propre base `arkya_admin`. Aucun
   nouveau Secret n’est nécessaire pour cette migration ; l’isolation actuelle
   porte sur les bases, la connexion MongoDB restant partagée. Une future
   séparation des comptes MongoDB devra être livrée par le même circuit GitOps.
2. Pour le déploiement autonome `deploy/realms`, créer le Secret
   `arkya-admin-mongo`, clé `connection-string`. La configuration générée le référence.
3. Déployer Identity et Gateway avec la nouvelle route publique `/v1/auth/refresh`,
   puis le frontend et Admin. Conserver les volumes Data Protection existants.
4. Utiliser le site et l’API sous le même site HTTPS ; conserver l’origine exacte
   du frontend dans `WebClient:AllowedOrigins`. Vérifier les en-têtes HTTPS du proxy.

La publication passe par GitHub Actions puis la réconciliation GitOps existante.
Aucune commande directe Kubernetes ne fait partie de cette livraison.

## Vérification

Les tests Identity/Admin couvrent renouvellement, expiration, rotation, rejeu,
déconnexion, refus d’origine et révocation de droits. Les tests MongoDB utilisent
`ARKYA_TEST_MONGO` et des bases temporaires, jamais une base applicative.
Le frontend couvre restauration, appels concurrents et réponses tardives ;
les tests de gateway contrôlent cookies, Origin, en-tête CSRF et `no-store`.
Les scénarios navigateur utilisent des réponses Identity/Unity de test ; ils
ne constituent pas une validation de l’application Discord publique.

Bilan local de cette livraison : Identity **230/230**, Admin **306/306**
(MongoDB réel jetable dans les deux suites), Gateway **179/179**, frontend
**108/108**. Edge/Playwright : **70** contrôles Jouer, **17** contrôles Compte,
**15** contrôles refresh avec deux onglets et un vrai cookie HttpOnly de test.
Les validateurs de manifests ont passé hors ligne ; aucun cluster n’a été modifié.
Le MCP Unity n’exposait aucune instance pendant cette intervention ; aucun nouveau
build Unity ni test du jeu actuel n’a donc été réalisé. Le launcher est vérifié
séparément dans son [guide de distribution](WINDOWS_LAUNCHER.md).
