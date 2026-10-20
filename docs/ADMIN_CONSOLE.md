# Console d'administration

`Arkya.Admin` sert une console technique distincte de la vitrine. L'accueil `/`
est le dashboard ; l'éditeur de quêtes est conservé sous `/Quests`.

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
| Characters | `GET /admin/characters?accountId=&search=&onlineOnly=false&page=1&pageSize=25` | Personnages, présence et total filtré |
| Characters | `GET /admin/characters/overview` | Totaux et présence par realm |

Les contrats partagés sont dans `Arkya.ServiceDefaults/AccountAdminContracts.cs`
et `CharacterAdminContracts.cs`. Les clés administrateur des deux services sont
distinctes des clés de session/introspection et des clés dédiées.

## Configuration

| Application | Réglages |
| --- | --- |
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
nécessaires. Ce profil ne requiert ni nouveau Secret ni permission Kubernetes.
Les routes publiques Characters et les dedicateds restent désactivés. Le
profil complet ne doit pas être appliqué à l'aveugle par-dessus l'installation.

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
