# Console d'administration

`Arkya.Admin` sert une console technique distincte de la vitrine. L'accueil `/`
est le dashboard ; l'éditeur de quêtes est conservé sous `/Quests`.

L'interface est entièrement en anglais, avec une culture de présentation
`en-GB` fixe : navigation, formulaires, validations, erreurs, tableaux et
messages JavaScript. Les titres, descriptions et rapports écrits par les
opérateurs ou les joueurs conservent leur langue. Les routes et identifiants
techniques restent compatibles. Le [runtime multi-Area](MULTI_AREA_RUNTIME.md)
décrit les contenus et portails associés au plan du realm.

La fiche de chaque realm propose aussi le redémarrage, la mise à jour approuvée, les annonces en jeu, la déconnexion, la sauvegarde des joueurs et les multiplicateurs XP/récolte modifiables en direct : voir [actions, suivi et configuration](REALM_OPERATIONS.md).

La liste des royaumes `/Realms` ouvre un formulaire séparé sous `/CreateRealm`.
Chaque fiche utilise six sections : Aperçu, Joueurs, Réglages, Maintenance, En jeu
et Historique, réunies dans le menu latéral du realm. Ce menu remplace la navigation
générale sur sa fiche, y compris en accès local `/Realms/{realmId}` ; la création
et les pages centrales conservent le menu général. Les liens vers la liste des
realms, l’administration générale et la page publique disponible sont regroupés
dans ce même menu, accessible sur mobile. Une seule section est affichée ; les formulaires d’action et les
historiques s’ouvrent à la demande pour limiter le défilement.

L’aperçu présente les Areas du realm, chacune étant une carte entière comme
l'île de départ, avec une seule carte sélectionnée et les
pools de donjons/raids séparés. Le formulaire de création permet de choisir
les colonnes X et les lignes Y avec une prévisualisation immédiate ; Y désigne
le Z du monde Unity. Une partition d'Area correspond à un dédié. Voir
[le plan et ses limites](REALM_DEPLOYMENT.md).

## Présentation de la console

L’admin reprend la référence sombre fournie le 23 octobre 2026 : fond anthracite,
panneaux sobres, séparations fines et accent orange. Un bandeau supérieur porte
la marque, le retour au site et l’identité de l’opérateur. La navigation latérale
compacte utilise des icônes SVG et des groupes repliables. Les tableaux restent
denses ; les états conservent leur texte et une couleur sémantique.

Le menu peut être masqué sur ordinateur et ouvert sur mobile sans script.
Les pages centrales, l’espace dédié au realm, la connexion, les formulaires et
les aperçus des Areas utilisent le même thème. Les fonctions et règles d’accès
restent celles des pages existantes ; aucune donnée de démonstration n’est
ajoutée à l’application.

La section Joueurs et personnages présente uniquement les personnages rattachés
au realm courant, avec recherche, pagination et filtre des joueurs en ligne.
Le service Characters applique ce périmètre ; le formulaire de recherche ne
permet pas de sélectionner un autre realm. La section En jeu conserve les
annonces et commandes prises en charge par les serveurs.

## Fiche personnage

Les noms de personnages dans `/Characters`, `/Players`, la fiche compte et les
joueurs d'un realm ouvrent `/Character/{id}`. Cette fiche en lecture seule garde
le thème sombre de la console et propose sept sections : Overview, Statistics,
Quest history, Bags, Bank, Equipment et Appearance. Depuis un realm, le lien
conserve `realmId` et rejoint l'administration centrale si les sites dédiés sont
activés. Le service Characters applique le périmètre avant de retourner la fiche.

L'aperçu affiche l'identité, le propriétaire, la présence, les dates disponibles,
le niveau historique du document et la dernière position sauvegardée. Les
statistiques proviennent exclusivement du bloc `gameplay` du snapshot autoritaire
persisté : vie/mana/endurance, classe active, niveaux/XP des cinq classes,
rating et nombres de compétences apprises par rang. Le niveau historique du
document ne remplace pas les niveaux de classe. La date affichée est l'heure de
lecture de la fiche ; elle ne prétend pas dater la dernière sauvegarde et les
valeurs ne constituent pas une lecture en direct du dedicated.

Une absence de snapshot, un ancien snapshot sans statistiques, un format inconnu
et une donnée invalide restent distingués. Aucun profil initial ni réserve pleine
n'est généré par l'admin ; une vie sauvegardée à zéro est affichée à zéro.

L'historique des quêtes lit `character_quests`, avec les objectifs acceptés,
compteurs, état en cours/terminé, version du catalogue et révision. Il est paginé
par groupes de 25, jusqu'à 100 résultats par page. Les dates d'acceptation et de
fin ainsi qu'un journal des transitions ne sont pas enregistrés actuellement.

Les sacs, la banque et l'équipement ne disposent pas encore d'un contrat de
persistance autoritaire dans ce parcours. Ces sections affichent donc
**Unavailable**, avec l'explication correspondante ; elles ne présentent ni
inventaire vide ni objets fictifs. Leur contenu réel nécessitera le raccordement
des sauvegardes serveur. La fiche n'ajoute aucune mutation de progression,
d'objet ou d'économie.

## Navigation

| Menu | Écrans et actions |
| --- | --- |
| Dashboard | Disponibilité des API, comptes, personnages, joueurs actifs, réplicas et population par realm |
| Communauté | Recherche/pagination des comptes, fiche compte et personnages associés, ban/unban, accès bêta, joueurs en ligne, personnages |
| Infrastructure | Cibles k3s, profils immuables et transports, création des realms, déploiement et suivi des dedicateds, agents LFM |
| Contenu et support | Quêtes, publication des catalogues et signalements existants |

Les groupes du menu peuvent être repliés sans JavaScript. Les pages héritent du
rôle `Administrator`, de la session opérateur et de l'antiforgery ASP.NET pour
chaque formulaire. Les clés privées restent dans le serveur Admin.

La [session administrateur](ADMIN_SESSIONS.md) associe un ticket d'accès de
30 minutes à un refresh token HttpOnly, renouvelé automatiquement et limité à
30 jours. Son état persiste dans la base MongoDB `arkya_admin`. La déconnexion,
le rejeu d'un ancien refresh token et le retrait de l'allowlist révoquent l'accès.
La mise à jour impose une reconnexion aux anciennes sessions sans refresh token.

## Dashboard et présence

Chaque chargement exécute les lectures métier indépendamment, avec un délai
maximal de quatre secondes par source. « Disponible » signifie qu'une lecture
authentifiée a réussi, sans constituer un diagnostic complet des dépendances.
Les sources désactivées et les erreurs sont distinguées ; une valeur inconnue
est `—`. L'heure est affichée en UTC et Actualiser effectue un nouveau relevé.

Characters possède la présence : un joueur en ligne correspond à un personnage
avec lease actif, epoch positif, identité de serveur renseignée, expiration du
lease et de la session futures. Après révocation Identity, un lease peut rester
compté jusqu'à son expiration (90 secondes par défaut) ; ses nouvelles
validations/renouvellements sont déjà refusés. La connexion au site web seule
ne compte pas comme joueur en ligne.

## Modération et accès bêta

Identity possède les décisions. Chaque action exige un motif de 1 à 500
caractères et la révision attendue, y compris la révision initiale zéro. Un
conflit recharge la fiche plutôt que d'écraser une modification concurrente.
L'acteur vient de l'identité opérateur, jamais du formulaire. Identity conserve
les 100 dernières modifications dans le document du compte. Le mot de passe
partagé conserve un acteur commun ; Discord identifie l'opérateur par son ID.

Un ban bloque la connexion et invalide les sessions existantes. Le déban ne
réactive pas les anciens jetons : le joueur doit se reconnecter. Les anciens
documents de comptes sont lus avec des valeurs par défaut compatibles.
L'introspection relit les droits du compte à chaque validation.

`GameAccess:RequireBetaAccess=true` dans Characters active le contrôle bêta sur
la demande d'admission, sa consommation, le renouvellement du lease et la
sauvegarde autoritaire. Désactivé par défaut pour les parcours locaux, ce
contrôle est activé par le profil k3s `admin-console`. Le droit bêta ne
contourne jamais un bannissement.

## API privées

Les routes exigent `X-Arkya-Admin-Key` et un transport admis. Les mutations de
compte exigent aussi `X-Arkya-Admin-Actor`. Ces endpoints restent absents des
routes publiques de la gateway.

| Propriétaire | Route | Résultat / corps |
| --- | --- | --- |
| Identity | `GET /admin/accounts?search=&page=1&pageSize=25` | Comptes et total filtré |
| Identity | `GET /admin/accounts/{accountId}` | Fiche sans jetons ni documents internes |
| Identity | `POST /admin/accounts/{accountId}/ban` ou `/unban` | `{expectedRevision, reason}` |
| Identity | `POST /admin/accounts/{accountId}/beta-access` | `{expectedRevision, hasBetaAccess, reason}` |
| Characters | `GET /admin/characters?accountId=&realmId=&search=&onlineOnly=false&page=1&pageSize=25` | Personnages, présence et total filtré ; `realmId` facultatif, appliqué par le serveur |
| Characters | `GET /admin/characters/overview` | Totaux et présence par realm |
| Characters | `GET /admin/characters/{characterId}?realmId=&questPage=1&questPageSize=25` | Fiche privée, snapshot de statistiques projeté, historique de quêtes paginé et disponibilité des possessions ; `404` si absent ou hors realm |

Les contrats partagés sont dans `Arkya.ServiceDefaults/AccountAdminContracts.cs`
et `CharacterAdminContracts.cs`. Les clés administrateur des deux services sont
distinctes des clés de session/introspection et des clés dédiées.

Le détail n'expose ni JSON de simulation brut, ni lease ID, ni identifiant de
session, ni clé dédiée. Les paramètres de quêtes acceptent une page de 1 à
100 000 et une taille de 1 à 100. Le navigateur reçoit une page `no-store` ; les
réponses de dépendances invalides sont refusées et leurs détails internes ne
sont pas réaffichés.

## Configuration

| Application | Réglages |
| --- | --- |
| Sessions Admin | `Mongo:ConnectionString`, `Mongo:DatabaseName=arkya_admin`, `AdminSessions:AccessLifetime`, `AdminSessions:RefreshLifetime` ; [durées et garanties](ADMIN_SESSIONS.md) |
| Admin → Identity | `IdentityAdminApi:Enabled`, `BaseUrl`, `AdminKey`, `CertificateAuthorityFile`, `AllowClusterHttp` |
| Admin → Characters | `CharactersAdminApi:Enabled`, `BaseUrl`, `AdminKey`, `CertificateAuthorityFile`, `AllowClusterHttp` |
| Identity | `Administration:Enabled=true`, `Administration:AdminKey` identique à la clé cliente Identity |
| Characters | `AdminAuthentication:Key` identique à la clé cliente Characters ; vide désactive l'accès |
| Admission bêta | `GameAccess:RequireBetaAccess=true` dans Characters |

Les clés ont 32 à 512 caractères et viennent des Secrets, variables
d'environnement ou user secrets. `CertificateAuthorityFile` est un chemin
absolu de CA PEM ; le nom et la chaîne TLS restent vérifiés. Les redirections
sont refusées. HTTP local est admis uniquement en Development. HTTP de cluster
exige un opt-in client vers un nom Kubernetes complet ; Identity exige aussi
`Administration:AllowClusterHttp=true`. Les deux API contrôlent le pair réseau
direct via `InternalHttp:AllowedNetworks` pour cet accès privé.

Le profil progressif `admin` utilise les Secrets déjà présents :
`PlayerAdministration:IdentityKeySeed` référence le champ administrateur
Content et `PlayerAdministration:CharactersKeySeed` le champ administrateur
Realms. `PlayerAdministrationKeys` dérive des clés HMAC-SHA256 avec des usages
fixes distincts pour Identity, Characters et les appels de service Characters.
Les valeurs sources ne sont jamais utilisées comme clés sur le réseau. Les
profils sans seed conservent les clés explicites existantes ; une seed et une
clé explicite simultanées font échouer le démarrage. La rotation d'une source
administrateur exige le redémarrage coordonné de ses consommateurs.

## k3s et déploiement d'un realm

Le profil facultatif [admin-console](../backend/deploy/gitops/admin-console/README.md)
étend la topologie bêta complète en HTTPS interne. Il raccorde les nouveaux
clients et autorise les seuls pods Admin vers les ports privés Identity et
Characters. Préparer les Secrets et certificats avant activation.

Le profil progressif `backend/deploy/gitops/admin` conserve `arkya-beta`, les
deux PVC de clés et Discord. Il ajoute Characters, les connexions privées
Admin vers Identity/Characters et les règles réseau correspondantes. La
connexion MongoDB Content existante est utilisée avec la base distincte
`arkya_characters` ; le principal de cette installation dispose déjà des droits
nécessaires. Le profil active l'allowlist publique exacte Characters pour
le catalogue, les personnages et l'admission : session joueur, propriété,
ban et accès bêta restent contrôlés par les services. Les routes `/admin`
et `/internal/game` restent absentes du routage public. Il active également
le déployeur Realms pour `beta-web`, dans le namespace réservé
`arkya-beta-realm`, avec un seul dedicated `island` en WebSocket/TCP `17943`.
La gateway existante publie uniquement la route de jeu
`wss://api.arkya.online/game/beta-web/` vers son Service privé stable.

Le compte de service projeté est monté seulement dans Realms. Son rôle dans
le namespace cible autorise `get/create` sur Deployments et Services,
`get` sur les seuls Secrets `arkya-ghcr` et `arkya-dedicated`, et `create` sur les Secrets. Kubernetes
ne limite pas `create` par nom ; Realms valide le nom fixe et le namespace.
Il n'a aucun droit de liste, modification ou suppression de Secret ni accès
à un autre namespace. Les ressources de contrôle sont
déclarées dans [realm-control](../backend/deploy/gitops/realm-control/README.md).
L'Application Argo vivante s'appelle `arkya-beta`, suit le chemin `admin`
sur `master` et utilise le projet `arkya-beta`. Les permissions du projet
ont été corrigées et vérifiées dans Argo le 20 octobre 2026 à 21:15 UTC
(23:15 à Bruxelles) : destination du realm et types RBAC autorisés,
interdiction globale cluster retirée et seul Namespace `arkya-beta-realm`
autorisé par nom. Aucun propriétaire Git actif du projet n'a été observé.
Le modèle bootstrap générique reste plus large pour son installation
multi-namespace et n'actualise pas automatiquement ce projet réel. Le patch
historique de huit opérations garde des préconditions et ne doit pas être
rejoué sur la configuration déjà corrigée.

Realms et Characters reçoivent la graine dédiée depuis un Secret existant et
dérivent une clé liée au couple `beta-web/island`. Avant les workloads, Realms
provisionne le Secret Opaque fixe `arkya-dedicated`, indexé par ID de serveur.
Le Deployment reçoit `ARKYA_DEDICATED_KEY` par `secretKeyRef` vers l'entrée
`island`, sans valeur de clé dans sa configuration. La clé reste absente de
Git, des logs, du profil MongoDB et des DTO.
Le nouveau dedicated autorise explicitement HTTP privé vers l'origine
Characters Kubernetes complète. Characters exige sa clé, contrôle le pair
direct et réserve ce transport aux routes internes de jeu. Aucun nouveau
Secret à copier manuellement, certificat, Ingress ou LoadBalancer n'est
requis dans le realm. Le **Secret source `arkya-ghcr` dans `arkya-beta` doit
être provisionné avant le rollout Realms** ; une référence de registre dans
un Deployment ne prouve pas sa présence. Le [guide runtime](REALM_RUNTIME.md#provisionner-le-registre-source)
décrit le modèle YAML du Secret source, à alimenter par la gestion des secrets
de l'infrastructure avec un PAT de lecture `read:packages`. Le déploiement
applicatif reste un push Git suivi de la synchronisation Argo. Realms reçoit en lecture seule son Docker
config, le valide et crée `arkya-ghcr` dans la cible
avant le Deployment. Les identifiants ne passent ni dans Git, ni dans les
logs, API ou profils MongoDB. Aucun droit Argo sur les Secrets n'est ajouté.

Infrastructure utilise le déployeur existant : choisir cible/profil, créer la
définition, relire la fiche, puis déclencher le déploiement. Tous les dedicateds
du profil et leurs Services sont créés par le worker Realms. Les demandes sont
persistantes, reprenables et contrôlées par révision ; le namespace est réservé
à un realm et l'image figée par digest. Les profils affichent UDP ou
WebSocket/TCP et la destination publique lorsqu'elle existe.

Cluster, namespaces autorisés, droits, Secrets et routes publiques sont des
prérequis d'exploitation. Identity, Characters et MongoDB restent partagés.
L'écran ne reçoit ni kubeconfig, ni YAML libre, ni shell. Voir le
[guide de déploiement](../backend/deploy/realms/README.md) pour images Linux,
RBAC et TLS. Des réplicas prêts ne prouvent pas l'admission d'un client réel.

## Vérification

La fiche personnage du 23 octobre 2026 a été vérifiée avec **825 tests Admin
réussis, 1 ignoré**, puis **287 tests Characters réussis, 14 ignorés**, sans échec.
Les tests MongoDB nécessitent `ARKYA_TEST_MONGO` ; ils n'ont pas été exécutés
faute d'instance locale disponible. Les tests couvrent la projection privée,
les clés séparées, le périmètre realm, les formats historiques/invalides de
statistiques, les quêtes paginées, l'échappement HTML, les liens des quatre
listes et l'absence de faux inventaires vides. Le rendu des pages Razor a été
inspecté dans Edge via Playwright avec des réponses de test explicites, sur
ordinateur et à 390 px ; ce contrôle ne constitue pas une lecture de données
de production. Les rapports et captures locaux sont sous
`artifacts/character-details/` et `output/playwright/character-details/`.

Le 23 octobre 2026, la suite complète Admin a terminé avec **682 réussites,
0 échec et 1 test MongoDB ignoré** sur 683 cas. Après le dernier correctif du
compteur anglais des joueurs en ligne, les deux cas existants
`DirectoriesForwardOwnershipSearchPaginationAndPresenceFilter` ont été
recompilés et exécutés : **2 réussites, 0 échec, 0 test ignoré**. La suite
complète précède ce seul correctif de texte et son assertion. Les rapports
locaux sont `artifacts/admin-english/test-results/admin-global-final.trx`
et `artifacts/admin-english/test-results/admin-online-presence-final.trx`.

Les tests HTTP couvrent authentification/antiforgery, recherche, pagination,
erreurs partielles, absence de secrets, modération et conflits. Avec
`ARKYA_TEST_MONGO`, les tests exercent aussi la persistance, les anciens
documents, les invalidations, les modifications concurrentes et la présence.

```powershell
$env:ARKYA_TEST_MONGO = 'mongodb://127.0.0.1:27028/'
dotnet test backend/Arkya.Backend.slnx
python backend/deploy/gitops/admin-console/Validate-Manifests.py
```

Le contrôle des manifests est hors cluster. Le déploiement k3s et l'admission
d'un client Unity réel restent à vérifier dans l'environnement d'exploitation.
