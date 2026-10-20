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
realm/serveur. Seule cette clé dérivée est injectée dans le pod Unity ; elle ne
figure ni dans le profil enregistré en MongoDB ni dans les réponses Admin.
Characters utilise `RealmRuntime:RealmId` et `RealmRuntime:ServerId` pour vérifier
la même identité. L'admission conserve les contrôles de session et d'accès bêta.

Le HTTP interne exige les opt-ins de chaque service et les NetworkPolicies.
Les accès publics restent en TLS. Le pod Unity ne possède aucun jeton Kubernetes,
fonctionne sous UID 1654 avec un système de fichiers en lecture seule et un `/tmp`
en mémoire. Realms seul reçoit un compte de service limité à `get/create` pour
Deployments et Services dans le namespace cible, avec accès au seul Secret
de registre géré par Realms. Le Docker config du Secret `arkya-ghcr` existant
est monté dans Realms ; son entrée `ghcr.io` est copiée dans le namespace cible
avant de lancer le pod. Aucun identifiant de registre n'est versionné.

La livraison suit Git puis Argo CD. Les manifests et le correctif de permissions
de l'AppProject sont décrits dans
[realm-control](../backend/deploy/gitops/realm-control/README.md).
