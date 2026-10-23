# Administration, quêtes, realms et joueurs

Ce profil ajoute `Arkya.Admin`, `Arkya.Content.Api`, `Arkya.Realms.Api` et
`Arkya.Characters.Api` au frontend, à Identity et à YARP déjà déployés.
Les sept applications cœur restent dans **`arkya-beta`**, sous l'Application Argo
existante **`arkya-beta`**. Le composant `../realm-control` prépare
**`arkya-beta-realm`** pour l'île dédiée.
Les composants `../inference-control` et `../reports-control` déjà actifs
portent le total à neuf services ; leurs activations et pins d'image sont
conservées.
Le composant `../web-client-control` ajoute le serveur statique WebGL dans
`arkya-beta`, portant le total à dix applications. Le frontend configure
`ARKYA_UNITY_UPSTREAM=arkya-web-client.arkya-beta.svc.cluster.local:8080` pour
servir `/game/`. Un realm actif ne remplace pas ce téléchargement. Le digest
du client provient d'une livraison Unity publiée ; voir les
[règles de mise à jour et vérifications](../web-client-control/README.md).
Le profil initial `frontend-discord` conserve son rendu et reste utilisable.

```text
Traefik existant → http://192.168.100.218:80 → YARP
  arkya.online       → frontend → /game/ → client WebGL statique
  api.arkya.online   → Identity
    /v1/realms      → Characters → catalogue Realms
    /v1/characters  → Characters (session joueur, droits et propriété)
    /game/beta-web/  → YARP WebSocket → île beta-web/island
  admin.arkya.online → Admin → Content (arkya_content)
                            → Realms  (arkya_realms)
                            → Identity (comptes, bans, droits bêta)
                            → Characters (personnages, présence)
  {id}.arkya.online       → frontend → fiche publique du realm
  {id}.admin.arkya.online → Admin → administration du realm
```

Un seul LoadBalancer est utilisé. Les autres Services sont en ClusterIP.
Traefik termine le HTTPS public ; les communications internes de ce profil
utilisent HTTP privé. Realms vérifie le TLS Kubernetes avec la CA projetée
du cluster. Aucun certificat TLS applicatif n'est requis. Le profil prépare
le namespace du realm et son RBAC ; le serveur Unity est lancé depuis
l'admin. Aucun Ingress, Secret ou endpoint de développement n'est ajouté.
`Gateway__CharactersEnabled=true` active l'allowlist exacte des routes joueur
Characters : catalogue, personnages et demandes d'admission authentifiées.
Les routes administrateur et `/internal/game` restent absentes de la gateway.

L'admin utilise **Discord**, un cookie sécurisé et des formulaires antiforgery.
L'export des modèles Unity possède une API HTTPS distincte de la session web :
`PUT https://admin.arkya.online/admin/content/item-assets/{guid}`, activée par
`ItemVisualExport__Enabled=true`. Elle exige la clé `ContentApi__AdminKey`
dans `X-Arkya-Admin-Key` et ne relaie que l'inscription d'un modèle et de sa
vignette, avec un corps JSON de 512 Kio maximum. Les autres routes Content
restent privées. Voir le [pipeline items](../../../../docs/ITEM_CONTENT.md).

Seul le compte `hatya_leash`, identifié par son ID Discord permanent
**`195861708248449024`**, est autorisé par la configuration de ce profil.
Le pseudo affiché ne sert jamais à accorder les droits. La connexion Discord
des joueurs ne confère aucun droit d'administration et le mode mot de passe
est désactivé pour ce profil. Son volume `arkya-admin-keys` conserve les clés
Data Protection ; le PVC Identity existant est préservé. Ces deux PVC ne
doivent pas être supprimés pour résoudre un problème de synchronisation.

Les modules Admin sont activés :

- **Quêtes** : lecture, édition et publication via Content, dans sa base
  MongoDB `arkya_content`.
- **Realms** : accès à l'API et au catalogue opérateur persistant dans
  `arkya_realms`. `Deployment__Enabled=true` active la cible `beta-web`,
  réutilisable par tout realm valide dans `arkya-beta-realm`. Le profil technique `beta-web`
  épingle l'image Linux et lance `island` sur TCP `17943`. La route WSS
  passe par `wss://api.arkya.online/game/beta-web/` ; le catalogue ne la
  marque prête qu'après la disponibilité réelle du dedicated.
  Une seule définition active réserve cette cible ; après suppression complète,
  un nouveau realm peut l’occuper. Characters résout ses identifiants depuis
  le catalogue privé, sans ajout manuel de clé.
- **Dashboard et joueurs** : état des services, realms et présence,
  recherche des comptes et personnages, bans/unbans et droits bêta.
  Identity possède les mutations de compte ; Characters conserve les
  personnages et vérifie les droits bêta à l'admission.

## Mise à jour de l'installation existante par Git

L'Application vivante `arkya-beta` suit déjà `backend/deploy/gitops/admin` sur
`master`, avec synchronisation automatique. Publier d'abord le code, attendre
la CI et la promotion des digests dans `beta/kustomization.yaml`, puis pousser
l'activation de ce profil. Argo CD applique les changements sans commande
`kubectl apply`, sans nouvelle Application et sans modifier les PVC existants.
Les étapes d'installation ci-dessous restent utiles pour un nouveau cluster.

Les sites de realm sont activés directement dans ce profil. Le chemin Argo
reste `backend/deploy/gitops/admin` ; aucune bascule vers un autre profil n'est
requise. Gateway accepte les deux suffixes publics et Admin partage ses
cookies sous `admin.arkya.online`. Une première reconnexion Admin est nécessaire
après le rollout. Les prérequis DNS/TLS externes sont décrits dans
[Pages publiques des realms](#pages-publiques-des-realms).

Les refresh tokens Admin utilisent la connexion MongoDB déjà fournie par
`arkya-content-secrets/Mongo__ConnectionString`, selon le même mécanisme que
Characters, Inference et Reports. Le Deployment la référence explicitement ;
il n'est pas nécessaire d'ajouter un champ à `arkya-admin-secrets`, de créer un
Secret ou un utilisateur MongoDB, ni de modifier les credentials existants.
La base reste propre à Admin : `Mongo__DatabaseName=arkya_admin`, collection
`admin_sessions`. L'application crée son index TTL au démarrage, puis renouvelle
les accès de 30 minutes jusqu'à la limite absolue de 30 jours. Les anciennes
sessions demandent une reconnexion. Voir les [sessions Admin](../../../../docs/ADMIN_SESSIONS.md).

Cette réutilisation conserve les privilèges du compte MongoDB partagé existant,
qui sont plus larges qu'un compte dédié à `arkya_admin` ; la séparation des bases
ne constitue donc pas une isolation des credentials. Une future réduction des
droits devra coordonner la distribution d'un compte dédié avec la gestion des
secrets de l'installation. La présente mise à jour passe uniquement par le
dépôt, les tests CI, la publication/promotion des images et la synchronisation
automatique existante, sans intervention directe sur le cluster.

Le profil réutilise les Secrets interservices déjà provisionnés et requiert
aussi le Secret de registre `arkya-ghcr` dans `arkya-beta`. Une référence
dans un Deployment ne prouve pas que ce Secret existe ; son absence a été
constatée pendant le rollout décrit ci-dessous. Aucun Secret ni identifiant
n'est écrit dans Git. Seul Realms reçoit le compte de service
`arkya-realm-deployer` et un jeton projeté renouvelable, sans montage
automatique. Le rôle cible autorise seulement `get/create` sur les
Deployments et Services du namespace du realm. Il permet aussi de lire
les seuls Secrets `arkya-ghcr` et `arkya-dedicated`, et de les créer : Kubernetes ne peut
limiter `create` par nom, ce contrôle est fixé dans Realms. Aucun droit
de liste, modification ou suppression des Secrets n'est accordé.
Le projet Argo vivant doit
autoriser cette destination, `ServiceAccount`, `Role` et `RoleBinding`.
Ces permissions ont été corrigées et vérifiées dans l'interface Argo le
**20 octobre 2026 à 21:15 UTC** (23:15 à Bruxelles). L'interdiction globale
cluster du projet vivant a été retirée ; son seul type cluster autorisé
est maintenant Namespace avec le nom exact `arkya-beta-realm`. Aucun
propriétaire Git actif de ce projet n'a été observé. Le modèle générique
`../bootstrap/project.yaml` reste destiné à une installation complète avec
plusieurs namespaces ; il ne remplace pas cette configuration réelle.
Le patch historique gardé dans `../realm-control/project-access.patch.yaml`
contient huit opérations et teste l'état initial avant sa modification.
Il échoue volontairement si la correction est déjà appliquée ou si l'état
diffère. Aucun AppProject n'est ajouté aux ressources des workloads.

Le code dérive des clés HMAC distinctes par usage à partir
de deux clés administrateur existantes, avec les mêmes graines côté client
Admin et côté API :

| Configuration injectée | Secret existant | Champ source |
| --- | --- | --- |
| `PlayerAdministration__IdentityKeySeed` dans Admin et Identity | `arkya-admin-secrets` | `ContentApi__AdminKey` |
| `PlayerAdministration__CharactersKeySeed` dans Admin et Characters | `arkya-admin-secrets` | `RealmsApi__AdminKey` |
| `Identity__ServiceKey` dans Characters | `arkya-identity-secrets` | `ServiceAuthentication__Key` |
| `Mongo__ConnectionString` dans Characters | `arkya-content-secrets` | `Mongo__ConnectionString` |
| `Mongo__ConnectionString` dans Admin | `arkya-content-secrets` | `Mongo__ConnectionString` |
| `RealmRuntime__DedicatedKeySeed` dans Realms et Characters | `arkya-admin-secrets` | `RealmsApi__AdminKey` |
| `Realms__ServiceKey` dans Characters | `arkya-realms-secrets` | `GameAccess__ServiceKey` |

Les clés administrateur Identity/Characters, la clé interne Characters et
les clés dédiées liées aux couples realm/serveur du catalogue privé
restent distinctes de leurs graines et entre elles. Elles sont stables tant
que les graines restent identiques ; une rotation des clés source exige donc
le redémarrage coordonné des consommateurs. Aucune valeur n'est affichée.
Characters utilise la base `arkya_characters` et Admin la base `arkya_admin` ;
la connexion MongoDB partagée doit disposer des droits de lecture, d'écriture
et de création d'index dans ces bases. Les collections métier restent propres
à chaque service.

Admin appelle les deux API sur leurs noms complets
`arkya-identity.arkya-beta.svc.cluster.local:8080` et
`arkya-characters.arkya-beta.svc.cluster.local:8080`. L'exception HTTP est
activée explicitement sur les clients et vérifie le pair réseau direct côté
API. Les NetworkPolicies conservent Gateway → Identity et ajoutent seulement
Gateway → Characters, Admin → Identity/Characters, Characters → Identity/Realms et
dedicated → Characters. Gateway seul peut joindre le port `17943` du realm.
Characters autorise aussi les sondes kubelet du réseau `192.168.100.0/24`.
Les identités dedicated explicites héritées sont retirées ; Characters
dérive à la place la clé dédiée du couple configuré. Avant les workloads,
Realms provisionne le Secret Opaque fixe `arkya-dedicated`, avec une entrée
`island`. Le Deployment référence cette entrée via `secretKeyRef` ; sa
configuration ne contient aucune clé dédiée en clair. Cette clé n'est
persistée ni dans Git, ni dans les logs, ni dans le profil MongoDB et reste
absente des DTO administrateur. La nouvelle image Unity
doit accepter l'opt-in HTTP privé de cluster ; l'ancien digest ne doit pas
être utilisé pour lancer ce profil. Aucun Secret n'est requis dans le
namespace cible à copier manuellement. Realms attend le fichier
`.dockerconfigjson` du Secret source `arkya-ghcr`, monté en lecture seule,
puis provisionne ce même nom dans le namespace approuvé avant le Deployment.
Le profil référence `ImagePullSecretName=arkya-ghcr`. Les identifiants du
registre n'apparaissent ni dans Git, ni dans les logs, DTO ou profils MongoDB.

Contrôle hors cluster avant le push :

```powershell
python D:\Repositories\arkya\backend\deploy\gitops\admin\Validate-PlayerManagement.py
python D:\Repositories\arkya\backend\deploy\gitops\realm-control\Validate-Manifests.py
python D:\Repositories\arkya\backend\deploy\gitops\Test-Manifests.py --require-digests
```

## 1. Enregistrer le callback Discord

Dans l'application Discord déjà utilisée pour les joueurs, section
**OAuth2 → Redirects**, ajouter exactement :

```text
https://admin.arkya.online/signin-discord
```

Conserver le callback Identity existant
`https://api.arkya.online/v1/auth/discord/callback`. L'admin utilise son propre
parcours OAuth, avec le même Client ID et Client Secret ; il vérifie l'ID
renvoyé par Discord avant de créer sa session administrateur.

## 2. Préparer les secrets

Prévoir deux connexions MongoDB valides, avec droits de lecture/écriture et
création des index sur leurs bases respectives. L'instance MongoDB existante
peut héberger ces bases ; ce profil ne crée ni serveur ni utilisateur MongoDB.

Depuis PowerShell :

```powershell
& D:\Repositories\arkya\backend\deploy\gitops\admin\Prepare-Secrets.ps1
```

Le script demande les deux URI MongoDB puis le Client ID et le Client Secret
de l'application Discord existante. La saisie est masquée et les valeurs ne passent
pas dans l'historique des commandes. Il génère quatre clés interservices
aléatoires distinctes et conserve les correspondances Admin/API.
Il écrit trois fichiers privés dans `admin/.local/`, ignorés par Git ;
il refuse de remplacer des fichiers existants et ne contacte pas Kubernetes.
Ne pas coller leur contenu dans un chat ni les commiter.

| Secret dans `arkya-beta` | Fichier privé | Clés |
| --- | --- | --- |
| `arkya-admin-secrets` | `admin.env` | `Discord__ClientId`, `Discord__ClientSecret`, `ContentApi__AdminKey`, `RealmsApi__AdminKey` |
| `arkya-content-secrets` | `content.env` | `Mongo__ConnectionString`, `ContentAdmin__Key`, `ServiceAuthentication__Key` |
| `arkya-realms-secrets` | `realms.env` | `Mongo__ConnectionString`, `RealmsAdmin__Key`, `GameAccess__ServiceKey` |

`ContentApi__AdminKey` et `ContentAdmin__Key` doivent être identiques ;
même règle pour `RealmsApi__AdminKey` et `RealmsAdmin__Key`.
Les clés de lecture internes restent distinctes des clés administrateur.
Les secrets Identity existants ne changent pas.

## 3. Provisionner les secrets depuis le terminal du cluster

Vérifier d'abord le contexte et les noms existants, sans afficher les valeurs :

```powershell
kubectl config current-context
kubectl -n arkya-beta get secrets
```

Pour une première installation, exécuter chaque ligne séparément :

```powershell
kubectl -n arkya-beta create secret generic arkya-admin-secrets --from-env-file=D:\Repositories\arkya\backend\deploy\gitops\admin\.local\admin.env
kubectl -n arkya-beta create secret generic arkya-content-secrets --from-env-file=D:\Repositories\arkya\backend\deploy\gitops\admin\.local\content.env
kubectl -n arkya-beta create secret generic arkya-realms-secrets --from-env-file=D:\Repositories\arkya\backend\deploy\gitops\admin\.local\realms.env
```

Ces commandes refusent d'écraser un Secret existant. Si l'un existe déjà,
examiner sa provenance avant toute rotation ; les deux côtés d'une clé
interservices doivent être mis à jour ensemble. Le Secret de registre
`arkya-ghcr`, si les images sont privées, doit également être disponible
dans `arkya-beta`.

## 4. Source Argo CD et première activation

Attendre la réussite de la CI et la promotion des nouvelles images dans
`beta/kustomization.yaml`. Valider le rendu hors cluster :

```powershell
python D:\Repositories\arkya\backend\deploy\gitops\Test-Manifests.py --require-digests
kubectl kustomize D:\Repositories\arkya\backend\deploy\gitops\admin
```

L'Application vivante **`arkya-beta`** suit déjà
`backend/deploy/gitops/admin` sur `master` : conserver son chemin, son dépôt,
son projet et sa destination. Sa synchronisation existante applique les
mises à jour du profil, y compris les sites de realm.

Pour une première activation sur une installation qui suit encore
`frontend-discord`, le patch historique ci-dessous sélectionne `admin` en
conservant la politique de synchronisation :

```powershell
kubectl -n argocd patch application arkya-beta --type merge --patch-file D:\Repositories\arkya\backend\deploy\gitops\admin\activate.patch.yaml
```

Pour cette première activation uniquement, les champs correspondants dans
l'interface Argo sont :

```yaml
path: backend/deploy/gitops/admin
targetRevision: master
```

Le fichier `../admin.application.example.yaml` reste un exemple optionnel
nommé `arkya-services`, distinct du nom réel `arkya-beta`. Ne pas le créer
dans l'installation existante : une seconde Application posséderait les mêmes
ressources. Si la synchronisation automatique est active, le changement
de chemin peut lancer immédiatement le déploiement : fournir les secrets
avant cette bascule.

Le rendu contient neuf Deployments applicatifs, dont Inference et Reports conservés,
deux PVC de clés préservés et neuf ressources de contrôle du realm, soit
48 objets. Le LB et les noms
des ressources déjà en service sont conservés. Examiner le diff, puis
laisser Argo synchroniser. Content et Realms restent strictement internes ;
YARP n'expose aucune API administrateur des services. Les routes joueur
Characters exigent une session et les contrôles de propriété ; l'admission
exige aussi l'accès bêta, un compte non banni et un dedicated prêt.

## 5. Vérifier les applications

```powershell
kubectl -n arkya-beta rollout status deployment/arkya-content --timeout=180s
kubectl -n arkya-beta rollout status deployment/arkya-realms --timeout=180s
kubectl -n arkya-beta rollout status deployment/arkya-characters --timeout=180s
kubectl -n arkya-beta rollout status deployment/arkya-identity --timeout=180s
kubectl -n arkya-beta rollout status deployment/arkya-admin --timeout=180s
kubectl -n arkya-beta rollout status deployment/arkya-gateway --timeout=180s
kubectl -n arkya-beta get deployments,pods,services,pvc
```

Le DNS `admin.arkya.online` doit rejoindre le Traefik existant et son
routeur doit couvrir ce sous-domaine, vers le même service YARP
`http://192.168.100.218:80`. Si le routeur wildcard existant couvre déjà
ce domaine, il n'y a pas de nouvelle entrée à créer.

Ouvrir `https://admin.arkya.online`, se connecter avec le compte Discord
autorisé, puis vérifier le Dashboard, Comptes, Personnages, Quêtes et Realms.
Un autre compte doit être refusé, même si son pseudo ressemble à celui
de l'administrateur. Les droits sont revérifiés lors des requêtes suivantes ;
retirer un ID de la configuration invalide son autorisation après le rollout.
Un pod Admin prêt ne prouve pas que ses API sont disponibles : leurs
écrans doivent charger sans erreur. La création/publication de contenu
modifie MongoDB ; utiliser du contenu voulu pour ce contrôle.
Vérifier également que le site et une connexion Discord fonctionnent toujours.

## Contrôle du transport interne

Les clients Admin autorisent HTTP uniquement avec
`ContentApi__AllowClusterHttp=true`, `RealmsApi__AllowClusterHttp=true`,
`IdentityAdminApi__AllowClusterHttp=true` et
`CharactersAdminApi__AllowClusterHttp=true`,
vers les noms Kubernetes complets configurés. Leurs redirections HTTP
restent refusées et HTTPS conserve sa validation de certificat.

Content et Realms n'acceptent HTTP qu'avec une allowlist explicite
`InternalHttp__AllowedNetworks` : réseau pods `10.42.0.0/16` et réseau
nœuds `192.168.100.0/24` pour les sondes kubelet de cette installation.
Cette décision utilise le pair réseau réel, pas une IP déclarée dans
un en-tête. Les clés API restent obligatoires. Leurs NetworkPolicies
n'acceptent les appels applicatifs que des pods Admin du même namespace,
avec un accès supplémentaire Characters → Realms pour le catalogue joueur.
Les autres profils conservent leurs exigences HTTPS par défaut.

## Lancer l'île bêta

Dans **Infrastructure → Realms**, choisir un nom et un identifiant de realm,
la cible **Serveur principal** et le profil **Île de départ · serveur WebSocket**,
puis lancer le déploiement. La cible n’impose plus `beta-web` comme identifiant ;
son namespace reste exclusif à un seul realm actif. Le dedicated utilise
`ARKYA_ALLOW_CLUSTER_HTTP=1` vers l'origine Characters Kubernetes complète,
avec une clé liée à son realm et serveur. Characters limite ce transport
privé aux routes `/internal/game`, au réseau direct autorisé et à la clé
dédiée. Les bans et droits bêta restent appliqués à l'admission et aux leases.

Le suivi attend jusqu'à 900 secondes pour le téléchargement et le démarrage
de l'image. La readiness Unity attend le terrain et le listener réels.
La gateway transmet uniquement `/game/beta-web/` et retire le préfixe avant
le listener WebSocket Unity. Elle réutilise le certificat et le LoadBalancer
existants ; aucun nouveau domaine, certificat ou Ingress n'est nécessaire.
Voir le [composant realm](../realm-control/README.md) pour le RBAC et les
sélecteurs réseau. Une route prête ne valide pas à elle seule l'admission
d'un client réel.

Dans Argo CD 3.3.9, `namespaceResourceWhitelist` filtre également les enfants
de l'arbre : autoriser Deployment seul masque ReplicaSet et Pod. L'ajout
diagnostic du 21 octobre 2026 vers 00:33 UTC autorise ces deux types dans le
projet existant, sans modifier le rôle Realms, les Secrets, les applications
ou les namespaces autorisés. Il est conservé séparément dans
`../realm-control/project-observability.patch.yaml`, avec un test exact de
l'ancienne liste de neuf types avant deux ajouts. Le patch historique du
20 octobre reste intact.

Vérifier la révision courante dans le manifeste **Live** du Deployment :
`observedGeneration`, `updatedReplicas` et conditions `Progressing` et
`ReplicaFailure`, puis le ReplicaSet et le Pod courants. Un ancien replica
disponible peut encore répondre pendant qu'une nouvelle révision échoue.

## État observé le 21 octobre 2026 à 00:40 UTC

Argo a synchronisé les 48 ressources de `a635317` avec le résultat
**SyncSucceeded**. Les nouvelles configurations Characters et Gateway sont
**Healthy**. Le nouveau pod Realms est bloqué en **FailedMount** : le Secret
source **`arkya-ghcr` est absent de `arkya-beta`**. Les références de registre
déjà présentes dans les manifests ne prouvaient pas son provisionnement.
L'ancien pod Realms reste Ready ; cette disponibilité ne valide pas le
nouveau déployeur. L'activation complète et le lancement du dedicated
restent à vérifier après provisionnement du Secret source et réussite du
rollout Realms.

## Pages publiques des realms

Ce profil active `GET /v1/public/realms` et `GET /v1/public/realms/{id}` sur le
site principal et sur le domaine API. Gateway appelle Realms en privé sans
transmettre de session, cookie ou clé du navigateur. La liste publique ne
confère aucun accès au realm ni droit opérateur.

Ce même profil active aussi `{id}.arkya.online` vers le frontend et
`{id}.admin.arkya.online` vers Admin, avec les contrôles de session, de rôle,
de révision et antiforgery existants. Aucun workload supplémentaire n'est
créé par realm. La connexion Discord conserve son callback central
`https://admin.arkya.online/signin-discord` et les cookies partagés restent
limités à `admin.arkya.online` et ses sous-domaines.

Préparer les DNS `*.arkya.online` et `*.admin.arkya.online` vers le Traefik
public, ainsi que les certificats couvrant ces deux suffixes. Le routeur
wildcard existant pour les sous-domaines à un niveau couvre déjà les pages
publiques ; ajouter les sous-domaines Admin imbriqués à sa règle et le SAN
`*.admin.arkya.online` au certificat. Conserver le resolver DNS et le service
YARP existants vers `http://192.168.100.218:80`. Le certificat
`*.arkya.online` seul ne couvre pas `{id}.admin.arkya.online`.

Le [guide des sites de realm](../../../../docs/REALM_SITES.md) détaille la
configuration et les contrôles après synchronisation. Le rendu local ne
provisionne ni DNS ni certificats publics et ne modifie pas la source Argo.
