# Administration, quêtes, realms et joueurs

Ce profil ajoute `Arkya.Admin`, `Arkya.Content.Api`, `Arkya.Realms.Api` et
`Arkya.Characters.Api` au frontend, à Identity et à YARP déjà déployés.
Les sept applications restent
dans **`arkya-beta`**, sous l'Application Argo existante **`arkya-services`**.
Le profil initial `frontend-discord` conserve son rendu et reste utilisable.

```text
Traefik existant → http://192.168.100.218:80 → YARP
  arkya.online       → frontend
  api.arkya.online   → Identity
  admin.arkya.online → Admin → Content (arkya_content)
                            → Realms  (arkya_realms)
                            → Identity (comptes, bans, droits bêta)
                            → Characters (personnages, présence)
```

Un seul LoadBalancer est utilisé. Les autres Services sont en ClusterIP.
Traefik termine le HTTPS public ; les communications internes de ce profil
utilisent HTTP privé. Aucun certificat ni CA n'est monté dans les pods.
Le profil ne crée pas d'Ingress, de Namespace, de rôle Kubernetes, de
serveur Unity ni d'API de développement. Characters reste privé :
`Gateway__CharactersEnabled=false` conserve son absence du routage public.

L'admin utilise **Discord**, un cookie sécurisé et des formulaires antiforgery.
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
  `arkya_realms`. Le lancement des dedicateds reste une étape distincte :
  `Deployment__Enabled=false`, aucun profil/image/cible Kubernetes n'est
  inventé, et aucune destination de jeu n'est annoncée active. Sans cible
  et profil configurés, l'interface ne permet pas encore de créer un realm.
- **Dashboard et joueurs** : état des services, realms et présence,
  recherche des comptes et personnages, bans/unbans et droits bêta.
  Identity possède les mutations de compte ; Characters conserve les
  personnages et vérifie les droits bêta à l'admission.

## Mise à jour de l'installation existante par Git

L'Application `arkya-services` suit déjà `backend/deploy/gitops/admin` sur
`master`, avec synchronisation automatique. Publier d'abord le code, attendre
la CI et la promotion des digests dans `beta/kustomization.yaml`, puis pousser
l'activation de ce profil. Argo CD applique les changements sans commande
`kubectl apply`, sans nouvelle Application et sans modifier les PVC existants.
Les étapes d'installation ci-dessous restent utiles pour un nouveau cluster.

L'activation utilise uniquement des Secrets déjà provisionnés. Aucun Secret
ni identifiant n'est écrit dans Git ; aucun Job ni droit Kubernetes nouveau
n'est nécessaire. Le code dérive des clés HMAC distinctes par usage à partir
de deux clés administrateur existantes, avec les mêmes graines côté client
Admin et côté API :

| Configuration injectée | Secret existant | Champ source |
| --- | --- | --- |
| `PlayerAdministration__IdentityKeySeed` dans Admin et Identity | `arkya-admin-secrets` | `ContentApi__AdminKey` |
| `PlayerAdministration__CharactersKeySeed` dans Admin et Characters | `arkya-admin-secrets` | `RealmsApi__AdminKey` |
| `Identity__ServiceKey` dans Characters | `arkya-identity-secrets` | `ServiceAuthentication__Key` |
| `Mongo__ConnectionString` dans Characters | `arkya-content-secrets` | `Mongo__ConnectionString` |

Les clés administrateur Identity/Characters et la clé interne Characters
restent distinctes de leurs graines et entre elles. Elles sont stables tant
que les graines restent identiques ; une rotation des clés source exige donc
le redémarrage coordonné des consommateurs. Aucune valeur n'est affichée.
Characters utilise la base `arkya_characters` ; la connexion MongoDB partagée
doit disposer des droits d'écriture et de création d'index dans cette base.

Admin appelle les deux API sur leurs noms complets
`arkya-identity.arkya-beta.svc.cluster.local:8080` et
`arkya-characters.arkya-beta.svc.cluster.local:8080`. L'exception HTTP est
activée explicitement sur les clients et vérifie le pair réseau direct côté
API. Les NetworkPolicies conservent Gateway → Identity et ajoutent seulement
Admin → Identity/Characters et Characters → Identity. Characters autorise
aussi les sondes kubelet du réseau `192.168.100.0/24`. Les identités dedicated
héritées du profil bêta sont retirées, et le catalogue joueur Realms reste
désactivé tant que ses cibles et dedicateds ne sont pas réellement déployés.

Contrôle hors cluster avant le push :

```powershell
python D:\Repositories\arkya\backend\deploy\gitops\admin\Validate-PlayerManagement.py
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

## 4. Basculer l'Application existante

Attendre la réussite de la CI et la promotion des nouvelles images dans
`beta/kustomization.yaml`. Valider le rendu hors cluster :

```powershell
python D:\Repositories\arkya\backend\deploy\gitops\Test-Manifests.py --require-digests
kubectl kustomize D:\Repositories\arkya\backend\deploy\gitops\admin
```

Dans **`arkya-services`**, conserver le dépôt, le projet `arkya-beta` et
la destination `arkya-beta`. Changer uniquement la source avec ce patch,
qui conserve aussi la politique de synchronisation existante :

```powershell
kubectl -n argocd patch application arkya-services --type merge --patch-file D:\Repositories\arkya\backend\deploy\gitops\admin\activate.patch.yaml
```

Ou modifier ces champs dans l'interface Argo :

```yaml
path: backend/deploy/gitops/admin
targetRevision: master
```

Le fichier `../admin.application.example.yaml` documente cette configuration.
Il ne faut pas créer une seconde Application qui posséderait les mêmes
ressources. Si la synchronisation automatique est active, le changement
de chemin peut lancer immédiatement le déploiement : fournir les secrets
avant cette bascule.

Le rendu ajoute quatre Deployments, leurs ConfigMaps/Services/NetworkPolicies
et le PVC Admin. Le LB et les noms des ressources déjà en service sont
conservés. Examiner le diff, puis synchroniser. Content et Realms restent
strictement internes ; YARP n'expose pas leurs API administrateur.

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
n'acceptent les appels applicatifs que des pods Admin du même namespace.
Les autres profils conservent leurs exigences HTTPS par défaut.
