# Déploiement du realm bêta

Le service Realms crée les Deployments et Services dans la cible autorisée
`arkya-beta-realm`. Le profil bêta est réservé à `beta-web` et lance le serveur
`island` sur TCP 17943. L'image Unity doit être publiée avec le support explicite
de `ARKYA_ALLOW_CLUSTER_HTTP=1` avant d'activer ce profil.

Le client utilise `wss://api.arkya.online/game/beta-web/`. Gateway retire le
préfixe `/game/beta-web` et transmet la connexion WebSocket au Service privé
stable `arkya-beta-web-island`. Ce Service sélectionne les pods du realm par
leurs labels ; les ressources créées par Realms gardent leurs noms propres.

`RealmRuntime:DedicatedKeySeed` est injecté dans Realms et Characters depuis un
Secret existant. Une clé HMAC distincte est dérivée pour chaque couple
realm/serveur. Realms crée le Secret Opaque `arkya-dedicated` dans le namespace
cible, avec une entrée par identifiant de serveur. Le Deployment utilise un
`secretKeyRef` : la clé ne figure ni dans son manifeste, ni dans le profil
enregistré en MongoDB, ni dans les réponses Admin.
Characters utilise `RealmRuntime:RealmId` et `RealmRuntime:ServerId` pour vérifier
la même identité. L'admission conserve les contrôles de session et d'accès bêta.

Le HTTP interne exige les opt-ins de chaque service et les NetworkPolicies.
Les accès publics restent en TLS. Le pod Unity ne possède aucun jeton Kubernetes,
fonctionne sous UID 1654 avec un système de fichiers en lecture seule et un `/tmp`
en mémoire. Realms seul reçoit un compte de service limité à `get/create` pour
Deployments et Services dans le namespace cible, avec création des Secrets et
lecture limitée aux noms `arkya-ghcr` et `arkya-dedicated` gérés par Realms.
Le Docker config du Secret `arkya-ghcr` existant
est monté dans Realms ; son entrée `ghcr.io` est copiée dans le namespace cible
avant de lancer le pod. Les Secrets existants sont vérifiés sans écrasement ;
une ressource étrangère ou une dérive empêche le lancement. Aucun identifiant
de registre ni aucune clé dedicated n'est versionné.

La livraison suit Git puis Argo CD. Les manifests et le correctif de permissions
de l'AppProject sont décrits dans
[realm-control](../backend/deploy/gitops/realm-control/README.md).
