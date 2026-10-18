# Construire et publier Gateway avec GitHub Actions

Le workflow [Services images](../../../.github/workflows/services-images.yml) remplace les publications séparées. Après les tests backend/frontend et les vérifications des manifests et de la vitrine conteneurisée, il construit huit images **Linux AMD64 et ARM64**. La gateway utilise [Dockerfile.service](../../Dockerfile.service) avec `APPLICATION=Arkya.Gateway` et un cache de build séparé. Ses tests propres ne nécessitent pas MongoDB ; la suite commune utilise un MongoDB 8.0 jetable.

## Lancer le workflow

Versionner les workflows et les sources à la racine de `Hantse/arkya-services`, qui contient le backend et la vitrine. Après leur arrivée sur la branche par défaut, ouvrir **Actions → Services images → Run workflow** et sélectionner cette branche.

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
ghcr.io/hantse/arkya-services-gateway:sha-<commit-complet>
```

Le résumé du job **Image gateway** fournit la référence `image@sha256:…`. Après publication de toutes les images, le job de promotion épingle leurs digests dans `backend/deploy/gitops/beta/kustomization.yaml`. Les preuves JSON restent disponibles 30 jours dans `service-image-gateway` et `services-release`. Le `GITHUB_TOKEN` permet ce commit ciblé dans le même dépôt, sans PAT. Un déploiement manuel peut toujours copier le digest dans `backend/deploy/gateway/.local/cluster.json` et suivre le [guide historique](README.md). Aucun tag `latest` n'est produit.

Un nouveau package GHCR est privé par défaut. Pour une image privée, créer ou réutiliser un Secret `kubernetes.io/dockerconfigjson` dans le namespace **`arkya-identity`**, avec un compte autorisé à lire le package et un PAT classique limité à `read:packages`. Reporter son nom dans `imagePullSecretName` ; le Secret déjà utilisé pour Identity peut servir si son compte a accès aux deux packages. Le `GITHUB_TOKEN` de la CI expire et ne convient pas pour les téléchargements permanents du cluster. Ne pas enregistrer le PAT dans Git ni dans le JSON du cluster.

Pour une image volontairement publique, rendre le package public dans GitHub puis laisser `imagePullSecretName` vide. Si la publication répond `denied` pour un package préexistant, autoriser le dépôt dans **Manage Actions access** de ce package.

La [source GitOps bêta](../gitops/README.md) est ensuite suivie par Argo CD. Les Secrets, les certificats et les accès au registre doivent être provisionnés dans le cluster ; publier une image ne confirme pas le fonctionnement du parcours Discord.

Références : [publication Docker avec GitHub Actions](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images), [accès à GHCR](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).
