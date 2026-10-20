# Étape 1 — frontend et connexion Discord

Ce profil reprend le modèle de `arksaas-infra` : Argo CD lit un environnement
Kustomize directement depuis l'Application `arkya-services`, sans parent
ni application enfant. Le frontend et la gateway exposent chacun un LoadBalancer privé,
Identity reste en ClusterIP et MongoDB est fourni par l'infrastructure.
Traefik est déjà en place et reste géré par l'exploitant ; ce profil ne crée
ni ingress, ni configuration Traefik, ni certificat public.

| Composant | Exposition | Rôle |
| --- | --- | --- |
| `arkya-frontend` | LoadBalancer, port 80 → 8080 | Site `https://arkya.online` |
| `arkya-gateway` | LoadBalancer `192.168.100.218`, port 80 → 8080 | API `https://api.arkya.online` |
| `arkya-identity` | ClusterIP, ports 8080 et 8443 | Discord, comptes et sessions MongoDB |

Le rendu contient **13 ressources, dont trois Deployments**, dans le seul
namespace `arkya-beta`. Les routes Characters et Admin sont désactivées.
Les services de jeu et le backoffice ne sont pas déployés ; le bouton Jouer
ne permet pas encore de lancer une partie.

Comme dans `arksaas-infra`, aucun objet `Namespace` n'est versionné dans ce
profil. L'option Argo `CreateNamespace=true` crée la destination au besoin ;
un namespace déjà présent est réutilisé. Le profil ne gère pas sa suppression.

Le profil hérite de `../beta` et de ses digests publiés par la CI. Il ne
duplique ni les images, ni les workloads. Aucun ImageUpdater supplémentaire
n'est nécessaire. Les noms, le PVC Identity et les protections des pods
restent ceux de la bêta, dans le namespace `arkya-beta`. Les DNS internes,
les sondes et les sélecteurs réseau sont adaptés à ce namespace.

## 1. Relever l'état depuis le terminal d'administration

```bash
kubectl config current-context
kubectl -n argocd get applications
kubectl -n arkya-beta get deployments,pods,services,ingresses,pvc
kubectl get services -A -o wide
```

Ces commandes sont en lecture seule. Relever les LoadBalancers existants,
notamment celui d'Arkya et les `.210`/`.211` utilisés par ArkSaas dans le dépôt
de référence. Ne pas réutiliser leurs adresses.

Le frontend n'impose pas d'IP : le contrôleur LoadBalancer du cluster doit
lui allouer une **adresse privée joignable depuis le proxy existant**. Si le
cluster exige une réservation, ajouter l'adresse libre confirmée au Service
dans ce profil avant le sync. Ne pas inventer une adresse à partir du dépôt
de référence. Le seul ServiceLB de k3s ne garantit pas une VIP dédiée ;
vérifier le contrôleur et le pool réellement utilisés.

Les NetworkPolicies frontend/gateway autorisent actuellement le LAN
`192.168.100.0/24`, comme la confiance proxy de la gateway. Adapter ces trois
valeurs ensemble si la source réseau réelle diffère. `externalTrafficPolicy:
Local` préserve cette source.

Ce profil sert au premier déploiement progressif. Si la bêta complète ou un
ancien ingress est déjà actif, examiner son état avant de changer de source :
un sync sans prune laisse les anciennes ressources présentes, et l'ancienne
route ingress n'est plus autorisée par la politique frontend de ce profil.
Le passage à la bêta complète change aussi l'exposition du frontend ; ce
sera une étape distincte, à coordonner avec l'exploitant du proxy.

## 2. Fournir les prérequis hors Git

Dans `arkya-beta`, prévoir :

| Ressource | Contenu / condition |
| --- | --- |
| Secret `arkya-identity-secrets` | `Mongo__ConnectionString`, `Discord__ClientId`, `Discord__ClientSecret`, `ServiceAuthentication__Key` |
| Secret `arkya-ghcr` | Lecture des packages GHCR privés |
| Secret `arkya-identity-tls` | `tls.crt` et `tls.key` pour le listener HTTPS interne déjà déclaré dans Identity |
| Stockage par défaut | PVC `arkya-identity-keys`, 1 Gio RWO, créé par le profil |

Créer le namespace s'il manque avant de provisionner les Secrets. Le compte
MongoDB est limité à `arkya_identity`, avec les droits de création des index.
S'il existe déjà, ne pas le recréer : les Secrets sont provisionnés dans
le namespace existant.
La clé de service a entre 32 et 512 caractères. Le certificat interne reste
nécessaire au démarrage d'Identity même si la connexion web passe par le
listener HTTP privé depuis la gateway.
Son nom DNS interne est `arkya-identity.arkya-beta.svc.cluster.local`.
Les Secrets et PVC sont propres au namespace : des ressources éventuellement
créées dans `arkya-identity` ne sont pas déplacées automatiquement. Vérifier
leur état avant toute migration et conserver les volumes existants.

Le callback de l'application Discord reste :
`https://api.arkya.online/v1/auth/discord/callback`.
L'origine CORS `https://arkya.online` et les URL du frontend sont déjà
configurées. La connexion utilise seulement le scope `identify`.

## 3. Valider et sélectionner le profil Argo

Depuis la racine du dépôt :

```bash
python backend/deploy/gitops/Test-Manifests.py --require-digests
kubectl kustomize backend/deploy/gitops/frontend-discord
```

Ces contrôles sont hors cluster. Ils ne prouvent pas que les images sont
accessibles depuis les nœuds, ni que les Secrets existent.

Les fichiers doivent être commités et poussés sur `arkya-services/master`
avant qu'Argo puisse les lire. Configurer directement l'Application existante
**`arkya-services`** avec le [manifest d'exemple](../frontend-discord.application.example.yaml) :

| Champ | Valeur |
| --- | --- |
| Project | `arkya-beta` |
| Repository | `git@github.com:Hantse/arkya-services.git` |
| Path | `backend/deploy/gitops/frontend-discord` |
| Revision | `master` |
| Destination server | `https://kubernetes.default.svc` |
| Destination namespace | `arkya-beta` |
| Sync | Manuel pour la première configuration |

Le projet Argo `arkya-beta` autorise ce dépôt et ce namespace ; appliquer son
[manifest actualisé](../bootstrap/project.yaml) pour ajouter cette destination
si le projet avait déjà été créé :

```powershell
kubectl -n argocd apply -f D:\Repositories\arkya\backend\deploy\gitops\bootstrap\project.yaml
```

L'URL SSH conserve la connexion du dépôt déjà enregistrée
dans Argo. Le namespace de destination correspond aux namespaces explicites
des manifests ; changer seulement ce champ ne renomme pas leurs ressources.

Le chemin `frontend-discord` rend uniquement les workloads. Les chemins
`bootstrap` et `bootstrap-frontend-discord` restent des alternatives pour
une organisation parent/enfant ; **ils ne sont pas utilisés dans ce mode direct**.
L'objet Application est conservé par Argo dans son namespace de contrôle,
mais sa destination et tous les pods applicatifs sont dans `arkya-beta`.

Si l'ancienne configuration bootstrap a déjà été synchronisée, vérifier
avant le premier sync direct :

```bash
kubectl -n argocd get appproject arkya-beta
kubectl -n argocd get application arkya-beta --ignore-not-found
```

Une ancienne application enfant doit rester sans synchronisation pendant
l'examen de ses ressources. Ne pas laisser deux applications gérer les
mêmes workloads. `FailOnSharedResource=true` bloque une propriété concurrente.
Les anciens objets Application/AppProject peuvent apparaître comme candidats
au prune : ne pas les supprimer pendant cette transition, notamment le projet
`arkya-beta` utilisé par l'application directe.

Enregistrer d'abord la configuration directe avec l'auto-sync désactivé.
Examiner ensuite le diff : trois Deployments, aucun workload Unity, aucun
namespace operations. Synchroniser manuellement **sans prune** après
provisioning des prérequis. Les volumes ne doivent jamais être supprimés
pour résoudre une adoption. L'auto-sync avec prune/self-heal peut être activé
après validation du premier déploiement et résolution des anciennes propriétés.

Après mise à jour depuis la révision qui déclarait explicitement le Namespace,
faire un Hard Refresh. L'état désiré contient les 13 ressources ci-dessus.
Si Argo conserve une ancienne entrée Namespace suivie, ne pas la supprimer
par prune ; le namespace existant reste nécessaire aux services.

```bash
kubectl -n arkya-beta rollout status deployment/arkya-identity --timeout=180s
kubectl -n arkya-beta rollout status deployment/arkya-gateway --timeout=180s
kubectl -n arkya-beta rollout status deployment/arkya-frontend --timeout=180s
kubectl -n arkya-beta get services arkya-frontend arkya-gateway -o wide
```

## 4. Valider la connexion

Depuis un poste PowerShell 7.2+ avec le dépôt :

```powershell
./backend/deploy/identity/Test-Deployment.ps1 -Origin https://api.arkya.online -SiteOrigin https://arkya.online
```

Le script teste HTTPS et CORS, le refus des routes privées, puis la création
et le polling d'un challenge temporaire dans MongoDB. Il ne connecte aucun
compte Discord. Terminer par une vraie connexion sur
`https://arkya.online/account.html`, un rechargement qui restaure le compte
et une déconnexion qui révoque la session.

La configuration Traefik reste hors de cette procédure. Si l'accès public
échoue alors que les pods sont prêts, transmettre les adresses des Services
et le résultat du contrôle à l'exploitant de l'entrée existante.
