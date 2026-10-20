# Étape 1 — frontend et connexion Discord

Ce profil reprend le modèle de `arksaas-infra` : Argo CD lit un environnement
Kustomize, le frontend et la gateway exposent chacun un LoadBalancer privé,
Identity reste en ClusterIP et MongoDB est fourni par l'infrastructure.
Traefik est déjà en place et reste géré par l'exploitant ; ce profil ne crée
ni ingress, ni configuration Traefik, ni certificat public.

| Composant | Exposition | Rôle |
| --- | --- | --- |
| `arkya-frontend` | LoadBalancer, port 80 → 8080 | Site `https://arkya.online` |
| `arkya-gateway` | LoadBalancer `192.168.100.218`, port 80 → 8080 | API `https://api.arkya.online` |
| `arkya-identity` | ClusterIP, ports 8080 et 8443 | Discord, comptes et sessions MongoDB |

Le rendu contient **14 ressources, dont trois Deployments**, dans le seul
namespace `arkya-identity`. Les routes Characters et Admin sont désactivées.
Les services de jeu et le backoffice ne sont pas déployés ; le bouton Jouer
ne permet pas encore de lancer une partie.

Le profil hérite de `../beta` et de ses digests publiés par la CI. Il ne
duplique ni les images, ni les workloads. Aucun ImageUpdater supplémentaire
n'est nécessaire. Les noms, le PVC Identity et les protections des pods
restent ceux de la bêta.

## 1. Relever l'état depuis le terminal d'administration

```bash
kubectl config current-context
kubectl -n argocd get applications
kubectl -n arkya-identity get deployments,pods,services,ingresses,pvc
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

Dans `arkya-identity`, prévoir :

| Ressource | Contenu / condition |
| --- | --- |
| Secret `arkya-identity-secrets` | `Mongo__ConnectionString`, `Discord__ClientId`, `Discord__ClientSecret`, `ServiceAuthentication__Key` |
| Secret `arkya-ghcr` | Lecture des packages GHCR privés |
| Secret `arkya-identity-tls` | `tls.crt` et `tls.key` pour le listener HTTPS interne déjà déclaré dans Identity |
| Stockage par défaut | PVC `arkya-identity-keys`, 1 Gio RWO, créé par le profil |

Créer le namespace s'il manque avant de provisionner les Secrets. Le compte
MongoDB est limité à `arkya_identity`, avec les droits de création des index.
La clé de service a entre 32 et 512 caractères. Le certificat interne reste
nécessaire au démarrage d'Identity même si la connexion web passe par le
listener HTTP privé depuis la gateway.

Le callback de l'application Discord reste :
`https://api.arkya.online/v1/auth/discord/callback`.
L'origine CORS `https://arkya.online` et les URL du frontend sont déjà
configurées. La connexion utilise seulement le scope `identify`.

## 3. Valider et sélectionner le profil Argo

Depuis la racine du dépôt :

```bash
python backend/deploy/gitops/Test-Manifests.py --require-digests
kubectl kustomize backend/deploy/gitops/frontend-discord
kubectl kustomize backend/deploy/gitops/bootstrap-frontend-discord
```

Ces contrôles sont hors cluster. Ils ne prouvent pas que les images sont
accessibles depuis les nœuds, ni que les Secrets existent.

Les fichiers doivent être commités et poussés sur `arkya-services/master`
avant qu'Argo puisse les lire. Dans le parent existant **`arkya-services`**,
sélectionner le chemin **`backend/deploy/gitops/bootstrap-frontend-discord`**
au lieu de `backend/deploy/gitops/bootstrap`. Le projet du parent reste
`arkya-bootstrap`, destination `argocd`, selon le [guide bootstrap](../README.md).

Le bootstrap alternatif configure **la même Application `arkya-beta`** avec
une seule source, `backend/deploy/gitops/frontend-discord`, branche `master`.
Il ne faut pas créer une seconde Application propriétaire des mêmes ressources.
Modifier uniquement l'enfant serait temporaire : un sync du parent remettrait
la bêta complète. L'URL SSH de la source doit correspondre à la connexion
du dépôt déjà enregistrée dans Argo.

Examiner le diff du parent puis le synchroniser. Examiner ensuite celui
d'`arkya-beta` : trois Deployments, aucun workload Unity, aucun namespace
operations. Synchroniser manuellement **sans prune** pour ce premier passage.
Les volumes ne doivent jamais être supprimés pour résoudre une adoption.

```bash
kubectl -n arkya-identity rollout status deployment/arkya-identity --timeout=180s
kubectl -n arkya-identity rollout status deployment/arkya-gateway --timeout=180s
kubectl -n arkya-identity rollout status deployment/arkya-frontend --timeout=180s
kubectl -n arkya-identity get services arkya-frontend arkya-gateway -o wide
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
