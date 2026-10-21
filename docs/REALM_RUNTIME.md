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
Le Secret source `arkya-ghcr` dans **`arkya-beta` est un prérequis à provisionner**.
Une référence `imagePullSecrets` ne prouve pas son existence. Son Docker config
est monté dans Realms ; son entrée `ghcr.io` est copiée dans le namespace cible
avant de lancer le pod. Les Secrets existants sont vérifiés sans écrasement ;
une ressource étrangère ou une dérive empêche le lancement. Aucun identifiant
de registre ni aucune clé dedicated n'est versionné.

## Provisionner le registre source

Le [script opérateur](../backend/deploy/gitops/realm-control/Provision-RegistrySecret.ps1)
fonctionne avec Windows PowerShell 5.1 et PowerShell 7. Il requiert `kubectl` et un contexte Kubernetes explicite disposant
des accès nécessaires. Il vérifie les deux namespaces `arkya-beta` et
`arkya-beta-realm`, ainsi que le nom, le label et l'image du Deployment
`arkya-realms`. La connexion Kubernetes doit vérifier TLS. Il refuse de
remplacer un Secret existant.

Utiliser un **PAT GitHub classique limité exactement à `read:packages`**,
autorisé à lire le package privé `hantse/arkya-mmorpg-dedicated`. Toute autre
portée, y compris une portée supplémentaire de lecture, est refusée. Le script
contrôle les portées GitHub puis lit uniquement les métadonnées du digest
dedicated épinglé. Il crée exclusivement `arkya-beta/arkya-ghcr` en envoyant
le JSON en mémoire à l'entrée standard de `kubectl create` ; aucune valeur
privée ne passe par ses arguments, un fichier temporaire ou sa sortie.

Exemple à adapter avec le contexte et le digest approuvés :

```powershell
& ./backend/deploy/gitops/realm-control/Provision-RegistrySecret.ps1 `
  -Contexte 'CONTEXTE_ARKYA' `
  -KubeConfig 'C:\prive\kubeconfig.yaml' `
  -GitHubUser 'COMPTE_GITHUB' `
  -DedicatedImageDigest 'sha256:REMPLACER_PAR_64_CARACTERES_HEXADECIMAUX' `
  -TokenFilePath 'C:\prive\ghcr-read-packages.txt'
```

Le fichier du PAT fourni doit être déjà présent, privé et hors dépôt ; le script
accepte UTF-8 avec ou sans BOM ainsi que UTF-16 avec BOM, et ne crée aucun
fichier d'identifiant. Sans `-TokenFilePath`, le
script demande une saisie masquée. `-KubeConfig` est facultatif si le fichier
habituel contient le contexte choisi. Ne pas réutiliser un credential Docker
ou Git plus large. La création du Secret source ne prouve pas le rollout
Realms, la copie vers le realm ni l'admission d'un joueur : vérifier ensuite
ces étapes dans l'environnement réel.

La livraison suit Git puis Argo CD. Les manifests et le correctif de permissions
de l'AppProject sont décrits dans
[realm-control](../backend/deploy/gitops/realm-control/README.md).
