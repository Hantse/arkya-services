# Construire et publier Gateway avec GitHub Actions

Le workflow [Gateway image](../../../.github/workflows/gateway-image.yml) teste `Arkya.Gateway`, construit [Dockerfile.gateway](../../Dockerfile.gateway) et publie une image **Linux AMD64 et ARM64** dans GitHub Container Registry. Il utilise les mêmes versions épinglées de .NET et des actions que le workflow Identity, avec un cache de build séparé. La gateway ne nécessite pas de MongoDB pour ses tests.

## Lancer le workflow

Versionner le workflow à la racine du dépôt GitHub qui contient `backend/`, ainsi que le Dockerfile, `.dockerignore`, les sources Gateway/ServiceDefaults et les tests Gateway. Après son arrivée sur la branche par défaut, ouvrir **Actions → Gateway image → Run workflow** et sélectionner cette branche.

| Événement | Résultat |
| --- | --- |
| Push modifiant le backend ou le workflow sur la branche par défaut | Tests, build et publication GHCR |
| Pull request ou push sur une autre branche | Tests et build, sans publication |
| Lancement manuel sur la branche par défaut | Tests, build et publication GHCR |
| Lancement manuel sur une autre branche | Tests et build, sans publication |

La branche par défaut est détectée automatiquement. Les tags Git ne déclenchent pas le workflow. Les filtres couvrent aussi les configurations .NET/NuGet à la racine ; une PR limitée à Unity ne lance pas cette CI, qui ne doit donc pas être une vérification obligatoire pour toutes les PR.

Le job de publication reçoit `packages: write` et utilise le `GITHUB_TOKEN` temporaire fourni par GitHub. **Aucun secret GitHub personnalisé n’est nécessaire.** La politique de l’organisation doit autoriser les actions et la publication de packages. Les PR n’ont ni connexion GHCR ni permission de publication, et n’écrivent pas le cache partagé. Aucun secret Discord, MongoDB ou Kubernetes n’entre dans l’image.

## Utiliser l’image dans k3s

Le nom découle du dépôt GitHub, en minuscules :

```text
ghcr.io/<proprietaire>/<depot>-gateway:sha-<commit-complet>
```

Le résumé du job **Publish to GHCR** fournit le tag et la référence `image@sha256:…`. Copier cette dernière dans le champ `image` de `backend/deploy/gateway/.local/cluster.json`, puis suivre le [guide de déploiement](README.md). Préférer le digest : relancer le même commit peut remplacer son tag SHA. Aucun tag `latest` n’est produit.

Un nouveau package GHCR est privé par défaut. Pour une image privée, créer ou réutiliser un Secret `kubernetes.io/dockerconfigjson` dans le namespace **`arkya-identity`**, avec un compte autorisé à lire le package et un PAT classique limité à `read:packages`. Reporter son nom dans `imagePullSecretName` ; le Secret déjà utilisé pour Identity peut servir si son compte a accès aux deux packages. Le `GITHUB_TOKEN` de la CI expire et ne convient pas pour les téléchargements permanents du cluster. Ne pas enregistrer le PAT dans Git ni dans le JSON du cluster.

Pour une image volontairement publique, rendre le package public dans GitHub puis laisser `imagePullSecretName` vide. Si la publication répond `denied` pour un package préexistant, autoriser le dépôt dans **Manage Actions access** de ce package.

Le workflow publie uniquement l’image Gateway ; il ne déploie pas le cluster et ne modifie ni DNS ni Traefik. Le workflow Identity reste indépendant.

Références : [publication Docker avec GitHub Actions](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images), [accès à GHCR](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).
