# Déploiement du realm bêta

Le service Realms crée les Deployments et Services dans la cible autorisée
`arkya-beta-realm`. Cette cible accepte tout identifiant de realm valide et
lance le serveur `island` sur TCP 17943. Un seul realm actif peut occuper cette
cible et ce namespace ; une suppression terminée libère la destination pour
un autre identifiant. L'image Unity doit être publiée avec le support explicite
de `ARKYA_ALLOW_CLUSTER_HTTP=1` avant d'activer ce profil.

Le client utilise `wss://api.arkya.online/game/beta-web/`. Gateway retire le
préfixe `/game/beta-web` et transmet la connexion WebSocket au Service privé
stable `arkya-beta-web-island`. Ces noms historiques désignent la destination,
pas l’identifiant du realm. Le Service sélectionne le serveur `island` géré
par Realms dans le namespace réservé ; les ressources créées par Realms gardent
leurs noms propres.

`RealmRuntime:DedicatedKeySeed` est injecté dans Realms et Characters depuis un
Secret existant. Une clé HMAC distincte est dérivée pour chaque couple
realm/serveur. Realms crée le Secret Opaque `arkya-dedicated` dans le namespace
cible, avec une entrée par identifiant de serveur. Le Deployment utilise un
`secretKeyRef` : la clé ne figure ni dans son manifeste, ni dans le profil
enregistré en MongoDB, ni dans les réponses Admin.
Avec `RealmRuntime:DynamicRealmsEnabled=true`, Characters résout les couples
realm/serveur du catalogue privé Realms et vérifie leur clé avec le seed
existant. Les identités sont disponibles avant la readiness pour le démarrage
du dedicated ; un realm retiré du catalogue n’est plus authentifié. Le seed
lui-même n’est jamais une clé de serveur. Le mode statique `RealmRuntime:RealmId`
et `RealmRuntime:ServerId` reste disponible hors de ce profil. L’admission
conserve les contrôles de session et d’accès bêta.

Le HTTP interne exige les opt-ins de chaque service et les NetworkPolicies.
Les accès publics restent en TLS. Le pod Unity ne possède aucun jeton Kubernetes,
fonctionne sous UID 1654 avec un système de fichiers en lecture seule et un `/tmp`
en mémoire. Realms seul reçoit un compte de service limité à `get/create` pour
Deployments et Services dans le namespace cible, avec création des Secrets et
lecture limitée aux noms `arkya-ghcr` et `arkya-dedicated` gérés par Realms.
Le Secret source `arkya-ghcr` dans **`arkya-beta` est un prérequis à provisionner**.
Une référence `imagePullSecrets` ne prouve pas son existence. Son Docker config
est monté dans Realms ; son entrée `ghcr.io` est copiée dans le namespace cible
avant de lancer le pod. Les Secrets existants sont vérifiés sans écrasement ;
une ressource étrangère ou une dérive empêche le lancement. Aucun identifiant
de registre ni aucune clé dedicated n'est versionné.

## Provisionner le registre source

Le contrat est fourni par le [modèle YAML du Secret GHCR](../backend/deploy/gitops/realm-control/registry-secret.example.yaml).
Il fixe le nom `arkya-ghcr`, le namespace source `arkya-beta`, le type
`kubernetes.io/dockerconfigjson` et la clé `.dockerconfigjson`. Le JSON
contient l'entrée `auths.ghcr.io.auth`, encodage base64 UTF-8 de
`utilisateur:PAT` avec un droit de lecture `read:packages`.

La valeur `auth` reste vide dans le modèle, qui est exclu du rendu Kustomize.
La gestion des secrets de l'infrastructure doit fournir le Secret renseigné
par son flux de livraison ; aucun identifiant, même encodé en base64, ne doit
être commité ici. Aucun mécanisme de secrets chiffrés n'est déclaré dans ce
dépôt : le modèle ne suppose pas l'installation d'un contrôleur supplémentaire.

Les manifests applicatifs sont validés hors cluster, poussés dans Git puis
appliqués par Argo. Aucune procédure locale de connexion au cluster n'est
requise pour préparer cette livraison. Le Secret source reste nécessaire au
rollout Realms ; un modèle vide ou une synchronisation des workloads ne
prouve pas que l'image privée est téléchargeable ni qu'un joueur peut entrer.

La livraison suit Git puis Argo CD. Les manifests et le correctif de permissions
de l'AppProject sont décrits dans
[realm-control](../backend/deploy/gitops/realm-control/README.md).
