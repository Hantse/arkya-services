# Livraison bêta avec Argo CD

Pour le déploiement progressif **frontend et connexion Discord seulement**,
utiliser le [profil `frontend-discord`](frontend-discord/README.md), inspiré
de `arksaas-infra`. L'Application `arkya-services` pointe directement vers ce
profil avec une seule source et trois services dans `arkya-beta`.
Traefik reste géré séparément.
La suite de ce document décrit la bêta complète.

Ce dossier contient les ressources déclaratives des huit applications backend
et frontend, dont la vitrine et le backoffice. L'Application dans `bootstrap/` combine deux sources
Git : `arkya-services/master`, chemin `backend/deploy/gitops/beta`, et
`arkya-mmorpg/main`, chemin `deploy/gitops/beta`. Le second dépôt possède les
players Unity web et dedicated, leur configuration et leur route WSS. Ce
montage utilise les [sources multiples d'Argo CD](https://argo-cd.readthedocs.io/en/stable/user-guide/multiple_sources/).

**Le premier sync est manuel.** Les tags initiaux `sha-...` côté services et
`bootstrap-required` côté Unity servent uniquement
à amorcer les fichiers : ils ne certifient pas qu'une image existe. Attendre
la réussite des publications et la promotion des **huit digests services et
deux digests Unity** avant le premier sync. Les pipelines ne contactent pas
le cluster et ne possèdent aucun identifiant Argo CD.

## Propriété des ressources

| Namespace | Ressources gérées |
| --- | --- |
| `arkya-identity` | Identity, Characters, gateway YARP, frontend statique et ingress du site |
| `arkya-operations` | Admin, Content, Realms, API historique privée |
| `arkya-realm-beta-web` | Client Unity web et dedicated Linux de l'île, Service TCP et ingress WSS ; déclarés dans le dépôt Unity |

Argo CD possède le dedicated. `Deployment__Enabled=false` dans Realms
désactive son déployeur Kubernetes et aucun jeton de compte de service n'est
monté dans les applications. Le catalogue Realms annonce une entrée
`StandaloneEntries` pour `beta-web/island`, protocole `1`, build
`arkya-web-beta-v1`. Son activation est initialement **false** ; l'opérateur
la passe à **true** dans `beta/config/realms.env` pour une validation contrôlée,
après les contrôles de santé et de transport public. L'admission exige cette
activation ; suivre la procédure en fin de document avant l'ouverture publique.
Les anciens profils déployés depuis Admin restent un autre
mode d'exploitation : ne pas les utiliser pour les workloads de cette
Application.

L'API historique `Arkya.Backend.Api` est compilée, empaquetée et déployée en
`Production`, avec son authentification de développement désactivée. Elle
n'a aucune route publique et sa NetworkPolicy refuse toute entrée. Identity,
Characters, Content et Realms reçoivent chacun une connexion à leur base
MongoDB existante par Secret. Ce dossier ne crée ni instance MongoDB, ni
utilisateur de base, ni données d'amorçage.

Les PVC de clés Data Protection Identity/Admin et les namespaces portent
`Prune=false,Delete=false`. Les déploiements utilisant ces volumes RWO gardent
un seul réplica avec la stratégie `Recreate`. Le StorageClass par défaut du
cluster doit fournir des volumes persistants d'au moins 1 Gio. Les PVC sont
dans la même vague de sync que leurs premiers Deployments consommateurs :
avec `WaitForFirstConsumer`, Kubernetes attend un pod pour lier le volume,
et une vague PVC antérieure ferait attendre Argo indéfiniment avant de créer
ce pod. Pour reprendre
un déploiement existant, vérifier les noms/claims/volumes et leur propriété
avant sync ; ne pas supprimer les PVC pour résoudre une erreur de propriété.

## Paramètres non secrets

Relire les fichiers `beta/config/*.env`, `beta/ingress.yaml` et le dossier
Unity `deploy/gitops/beta` avant de créer l'Application. Le domaine confirmé
est `arkya.online`, avec les sous-domaines de cette bêta :

| Usage | Valeur |
| --- | --- |
| Site et origine CORS | `https://arkya.online` |
| API publique et callback Discord | `https://api.arkya.online` et `/v1/auth/discord/callback` |
| Backoffice | `https://admin.arkya.online` |
| Jeu | `wss://beta-game.arkya.online/` |
| Source du Traefik externe, confiance gateway | `192.168.100.0/24`, à réduire à la source réelle lorsque connue |
| CIDR des pods k3s, confiance Identity/Characters/Admin | `10.42.0.0/16`, à vérifier sur le cluster réel |
| Contrôleur ingress | classe `traefik`, namespace `kube-system`, label `app.kubernetes.io/name=traefik` |

L'ancienne origine Sites est également autorisée dans Identity/Characters.
La retirer si elle ne sert plus de client. Pour changer un domaine, modifier
ensemble les ingress, les listes CORS des deux API, les options gateway,
le callback déclaré chez Discord et le catalogue Realms pour WSS. Le fichier
`auth-config.js` de la vitrine doit conserver la même origine d'API publique.

L'API et le backoffice conservent la topologie documentée : le Traefik externe
termine TLS, puis joint **le même** Service `arkya-gateway` de type
LoadBalancer à **`192.168.100.218:80`**. `externalTrafficPolicy: Local` conserve
la source, utilisée pour la confiance des en-têtes et la NetworkPolicy.
Il ne faut créer ni deuxième LoadBalancer à cette adresse, ni route directe
publique vers Identity/Characters. Le profil reprend les noms de ressources
existants pour leur adoption ; vérifier le diff Argo avant ce premier sync.

Le site et WSS utilisent un Traefik **dans le cluster**. Depuis la VM Traefik,
router leurs domaines vers l'adresse privée joignable de ce contrôleur,
distincte de `.218`. L'exemple `external-traefik.example.yaml` garde le
gateway existant et laisse cette adresse à renseigner. Le transport TLS
vers le contrôleur vérifie le certificat et son nom ; aucun mode
`insecureSkipVerify` n'est utilisé. Ce dossier ne modifie ni la VM, ni le DNS,
ni l'allocation de cette seconde entrée du cluster. Un Traefik externe ne
peut pas joindre un ClusterIP sans routage adapté. Les ingress site/jeu
n'acceptent que `websecure`, avec terminaison TLS ; configurer la redirection
HTTP vers HTTPS sur le proxy externe si nécessaire.

Les annotations d'observabilité désactivent les access logs et traces des
routes publiques, notamment du callback OAuth. Elles requièrent une version
de Traefik qui [prend en charge ces annotations](https://doc.traefik.io/traefik/reference/routing-configuration/kubernetes/ingress/).
Conserver aussi la confidentialité des journaux du proxy externe.

Le frontend sert la vitrine et relaie `/game/*`, préfixe conservé, vers
`arkya-web-client.arkya-realm-beta-web.svc.cluster.local:8080`. Le DNS du
service est résolu à la requête ; l'image frontend peut démarrer avant le
player web. Les deux images doivent être publiées pour que Jouer fonctionne.

## Secrets et certificats hors Git

Les Secrets suivants doivent exister dans leurs namespaces avant les
workloads. Les applications refusent de démarrer lorsqu'une configuration
requise manque. Utiliser le gestionnaire de secrets habituel ou des fichiers
d'environnement privés ; ne jamais placer de valeur réelle dans ces manifests.
Créer les trois namespaces au préalable, ou synchroniser leurs seules
ressources Namespace avant ce provisioning ; Argo CD peut ensuite les adopter.

| Namespace | Secret | Clés requises |
| --- | --- | --- |
| `arkya-identity` | `arkya-identity-secrets` | `Mongo__ConnectionString`, `Discord__ClientId`, `Discord__ClientSecret`, `ServiceAuthentication__Key` |
| `arkya-identity` | `arkya-characters-secrets` | `Mongo__ConnectionString`, `Identity__ServiceKey`, `ServiceAuthentication__Key`, `Realms__ServiceKey`, `GameAccess__DedicatedServers__0__Key` |
| `arkya-operations` | `arkya-content-secrets` | `Mongo__ConnectionString`, `ContentAdmin__Key`, `ServiceAuthentication__Key` |
| `arkya-operations` | `arkya-realms-secrets` | `Mongo__ConnectionString`, `RealmsAdmin__Key`, `GameAccess__ServiceKey` |
| `arkya-operations` | `arkya-admin-secrets` | `Admin__Password`, `ContentApi__AdminKey`, `RealmsApi__AdminKey` |
| `arkya-realm-beta-web` | `arkya-beta-dedicated` | `ARKYA_DEDICATED_KEY` ; l'URL HTTPS Characters est fixée dans le Deployment Unity |
| Les trois namespaces | `arkya-ghcr` | Secret type `kubernetes.io/dockerconfigjson`, lecture des packages GHCR privés |
| `arkya-identity`, `arkya-realm-beta-web` | `arkya-public-tls` | `tls.crt`, `tls.key`, certificat public couvrant respectivement le site et le jeu ; les certificats API/backoffice restent sur la VM Traefik |
| `arkya-identity` | `arkya-identity-tls`, `arkya-characters-tls` | `tls.crt`, `tls.key`, certificats de serveur interne |
| `arkya-operations` | `arkya-content-tls`, `arkya-realms-tls` | `tls.crt`, `tls.key`, certificats de serveur interne |
| Les namespaces des clients HTTPS internes | `arkya-internal-ca` | `ca.crt`, bundle des CA de confiance, sans clé privée |

Les clés interservices ont 32 à 512 caractères, aléatoires. Le mot de passe
Admin en a 16 à 512. Utiliser ces égalités, en gardant des clés distinctes
entre fonctions :

- `Identity.ServiceAuthentication__Key` = `Characters.Identity__ServiceKey` ;
- `Realms.GameAccess__ServiceKey` = `Characters.Realms__ServiceKey` ;
- `Realms.RealmsAdmin__Key` = `Admin.RealmsApi__AdminKey` ;
- `Content.ContentAdmin__Key` = `Admin.ContentApi__AdminKey`, différente de la
  clé de lecture `Content.ServiceAuthentication__Key` ;
- `Characters.GameAccess__DedicatedServers__0__Key` =
  `dedicated.ARKYA_DEDICATED_KEY`, réservée au couple `beta-web/island`.

Chaque utilisateur MongoDB doit être limité à sa base (`arkya_identity`,
`arkya_characters`, `arkya_content`, `arkya_realms`) et autorisé à créer ses
index. Les URI doivent joindre l'installation existante avec le TLS et
l'authSource adaptés. Les anciennes bases locales ne sont pas migrées par
Argo CD.

Les certificats internes couvrent les noms DNS complets utilisés dans les
configs, notamment `arkya-characters.arkya-identity.svc.cluster.local`.
Kestrel charge les fichiers PEM `tls.crt`/`tls.key` sur 8443, suivant sa
[configuration de certificats](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).
Inclure les intermédiaires dans `tls.crt`. Characters/Admin montent la CA
privée et définissent `SSL_CERT_FILE` ; .NET sous Linux consulte les
[emplacements OpenSSL de confiance](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography).
Laisser `RealmsApi__CertificateAuthorityFile` absent ou vide dans Admin : son
transport spécifique charge uniquement le premier certificat du fichier,
alors que `SSL_CERT_FILE` conserve la confiance du bundle complet pour
Realms et Content. Ne pas réintroduire cet override sur le bundle partagé.
Le player Unity doit aussi faire confiance à cette CA selon son image et son
runtime ; aucune validation de certificat n'est désactivée. Préserver les CA
publiques si le bundle remplace le magasin par défaut.

Les services ne publient jamais leurs ports internes 8443 par ingress. Les
NetworkPolicies autorisent seulement les pairs nommés nécessaires. Les
sorties restent ouvertes pour MongoDB, DNS, Discord et les appels internes ;
une politique egress globale existante doit permettre ces flux. Les Secrets
sont relus au démarrage : redémarrer les workloads après rotation.

## Construire, promouvoir, puis synchroniser

Les pipelines de chaque dépôt testent et publient leurs images, puis
committent les digests dans **leur propre** `kustomization.yaml` avec leur
`GITHUB_TOKEN`. Le compte Actions doit pouvoir écrire le contenu et les
packages du dépôt. Aucun PAT entre dépôts n'est nécessaire. Les références
peuvent donc évoluer séparément ; garder compatibles protocole et `BuildId`
entre deux promotions. Pour une version coordonnée, attendre les deux
publications puis synchroniser les deux révisions ensemble dans Argo CD.

Vérification hors cluster, depuis la racine des services :

```powershell
python -m pip install PyYAML==6.0.3
python backend/deploy/gitops/Test-Manifests.py
python backend/deploy/gitops/Test-Manifests.py --require-digests --unity-path Unity3D/Arkya/deploy/gitops/beta
kubectl kustomize backend/deploy/gitops/bootstrap
```

Le contrôle rend réellement Kustomize, vérifie les huit services, les
destinations TLS, les probes, les sélecteurs, la confidentialité des routes,
la protection du stockage et les deux sources Argo. Avec `--unity-path`, il
vérifie aussi les noms/ports des Services Unity, la route WSS annoncée et
l'absence de ressources concurrentes. `--require-digests` refuse les tags
d'amorçage. Ce contrôle hors ligne ne prouve ni la disponibilité des images,
ni les droits Kubernetes, ni la résolution DNS/TLS de l'environnement réel.

L'installation observée utilise le namespace de contrôle `argocd`.
Les objets Application et AppProject du bootstrap s'y trouvent ; les
workloads restent dans `arkya-identity`, `arkya-operations` et
`arkya-realm-beta-web`.

Deux projets Argo ont des rôles distincts :

| Application | Projet | Source / destination |
| --- | --- | --- |
| Parent existant `arkya-services` | `arkya-bootstrap` | Dépôt services, `backend/deploy/gitops/bootstrap`, destination `argocd` |
| Enfant `arkya-beta` | `arkya-beta` | Deux sources de workloads, trois namespaces applicatifs |

`bootstrap-project.yaml` doit être appliqué par un opérateur avant le sync
du parent. Il est hors de `bootstrap/` pour ne pas dépendre de sa propre
autorisation. Ce projet autorise uniquement les types Application/AppProject
dans `argocd`, depuis le dépôt services (URL HTTPS ou SSH exacte), sans
ressource cluster. Il reste administratif : l'écriture d'AppProjects dans
Argo doit être réservée aux mainteneurs de confiance du dépôt.

Le projet runtime `arkya-beta` autorise les Namespaces et les types de
workloads nécessaires. Il n'autorise pas Application/AppProject et ne doit
pas être affecté au parent. L'ancien message « resource argoproj.io:AppProject
is not permitted in project arkya-beta » révèle cette confusion de rôles.
Le projet runtime est créé en vague -1, avant l'application enfant en vague 0.

Enregistrer ces deux dépôts dans Argo avec leurs deploy keys en lecture seule :

- `git@github.com:Hantse/arkya-services.git`
- `git@github.com:Hantse/arkya-mmorpg.git`

Les connexions doivent être Successful et disponibles au projet `arkya-beta`.
Le credential du dépôt services doit aussi être disponible à `arkya-bootstrap`
si le parent utilise une connexion limitée à un projet. Les URL SSH des
sources doivent correspondre aux connexions enregistrées ; une connexion
HTTPS distincte n'utilise pas automatiquement une deploy key SSH.

Après provisioning des prérequis et promotion des dix digests, depuis le
dépôt services et une session Argo connectée au bon serveur :

```bash
kubectl -n argocd apply --dry-run=server -f backend/deploy/gitops/bootstrap-project.yaml
kubectl -n argocd apply -f backend/deploy/gitops/bootstrap-project.yaml
kubectl -n argocd patch application arkya-services --type=merge \
  -p '{"spec":{"project":"arkya-bootstrap","destination":{"namespace":"argocd"}}}'
argocd app get arkya-services --app-namespace argocd --hard-refresh
argocd app diff arkya-services --app-namespace argocd
argocd app sync arkya-services --app-namespace argocd --timeout 300
argocd app get arkya-beta --app-namespace argocd --hard-refresh
argocd app diff arkya-beta --app-namespace argocd
argocd app sync arkya-beta --app-namespace argocd --timeout 900
argocd app wait arkya-beta --app-namespace argocd --sync --health --timeout 900
```

Choisir le bon contexte Kubernetes avant exécution. Examiner les diffs avant
les syncs ; ces commandes ne demandent pas de prune. Le namespace `argocd`
existe déjà : le parent n'a pas à créer un namespace de contrôle.
L'application enfant reste en synchronisation manuelle. Si les ressources
bootstrap sont créées directement avec `kubectl apply -k bootstrap/`, il
faut aussi disposer des droits administratifs ; aucun sync workload n'en découle.

Avec le catalogue encore désactivé, exécuter dans l'environnement réel le
`dry-run=server`, le sync manuel, puis vérifier :

1. santé Argo des huit services et des deux workloads Unity ;
2. DNS, certificats, callback Discord et CORS depuis l'origine du site ;
3. chargement du vrai WebGL via `/game/` et accès au transport WSS public.

Passer ensuite `GameAccess__StandaloneEntries__0__Enabled=true` par commit et
sync pour effectuer le smoke d'admission réelle. Ce commutateur rend l'entrée
disponible pour tous les joueurs authentifiés ; prévoir cette validation
avant l'ouverture publique. Vérifier alors :

1. authentification, choix de personnage et admission dans le dedicated ;
2. déplacement, sauvegarde/reconnexion, révocation et refus d'un ticket rejoué ;
3. arrêt du dedicated avec sauvegarde/libération de la lease avant redémarrage.

Si cette validation échoue, remettre immédiatement le commutateur à `false`
par commit et sync, puis corriger avant l'ouverture. La readiness du dedicated
repose sur le fichier écrit par Unity après terrain/collisions
et écoute ; elle ne certifie pas son accessibilité WSS publique. Revenir aux
digests précédents dans Git puis sync permet un retour applicatif ; cela
n'annule aucune mutation déjà persistée dans MongoDB.
