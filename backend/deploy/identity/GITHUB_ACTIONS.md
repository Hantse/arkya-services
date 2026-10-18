# Construire et publier Identity avec GitHub Actions

Le workflow [Services images](../../../.github/workflows/services-images.yml) teste l'ensemble du backend et de la vitrine, puis construit huit images dans **GitHub Container Registry (GHCR)**. Identity utilise `backend/Dockerfile.service` avec `APPLICATION=Arkya.Identity.Api` et le contexte `backend/`. L'ancien workflow Identity autonome est remplacé par cette chaîne commune.

## Déclenchement et résultat

| Événement | Comportement |
| --- | --- |
| Push sur la branche par défaut, avec modification du backend ou du workflow | Tests, build et publication GHCR |
| Pull request ou push sur une autre branche, avec les mêmes modifications | Tests et build, sans publication |
| **Actions → Services images → Run workflow**, branche par défaut | Tests, build, publication GHCR et promotion GitOps |
| Lancement manuel sur une autre branche | Tests et build, sans publication |

La branche par défaut est détectée automatiquement : aucun nom `main` ou `master` n’est imposé. Les tags Git ne déclenchent pas le workflow. Les filtres couvrent aussi les fichiers de configuration .NET/NuGet à la racine. Ne pas rendre ce workflow filtré obligatoire pour toutes les pull requests : une PR ne touchant que Unity ne le déclenche pas.

Les tests de tous les projets backend s'exécutent en Release avec un MongoDB 8.0 jetable ; les tests Identity n'accèdent ni au MongoDB de production ni à Discord. Les tests frontend, les contrats des manifests et le démarrage du conteneur de vitrine sont aussi obligatoires. Un échec empêche la publication. Buildx construit ensuite **Linux AMD64 et ARM64** par compilation croisée, sans QEMU.

L’image prend automatiquement ce nom, en minuscules :

```text
ghcr.io/hantse/arkya-services-identity:sha-<commit-complet>
```

Le résumé du job **Image identity** donne la référence **`image@sha256:…`**. Après publication des huit images, la promotion remplace ensemble leurs digests dans `backend/deploy/gitops/beta/kustomization.yaml`, source suivie par Argo CD. Les artifacts `service-image-identity` et `services-release` conservent la preuve JSON pendant 30 jours. Les tags SHA servent de repères ; seuls les digests sont déployés. Aucun tag `latest` n'est utilisé.

## Première exécution

1. Versionner `.github/workflows/services-images.yml`, ses workflows réutilisables et les sources dans le dépôt `Hantse/arkya-services`. La vitrine `sites/arkya/` appartient au même dépôt.
2. Autoriser GitHub Actions et les actions GitHub/Docker utilisées si l’organisation applique une liste d’autorisation. Le job de publication demande `contents: read` et `packages: write` ; la politique de l’organisation doit permettre la publication de packages.
3. Une fois le workflow présent sur la branche par défaut, ouvrir **Actions → Services images → Run workflow**, sélectionner cette branche et lancer. Un push modifiant les chemins surveillés déclenche aussi la publication.
4. Vérifier les huit jobs d'images puis le job de promotion GitOps. La [source bêta](../gitops/README.md) décrit les prérequis Argo CD, certificats et Secrets ; leur présence dans le cluster reste nécessaire au démarrage des services.

**Aucun secret GitHub personnalisé n’est nécessaire pour publier.** Le workflow utilise le `GITHUB_TOKEN` temporaire fourni par GitHub. Les secrets Discord, la connexion MongoDB, les clés de service et le kubeconfig ne sont pas transmis au build : ils restent dans la configuration de déploiement.

Les checkouts de test et de build ne conservent pas leurs identifiants Git. Les actions sont épinglées à des commits complets. Les jobs d'images reçoivent `packages: write` ; seul le job final de promotion reçoit `contents: write` pour son commit ciblé, attribué au bot GitHub. Les pull requests n'ouvrent aucune session GHCR et ne publient ni image ni cache partagé. Aucun PAT n'est requis pour publier ou promouvoir dans ce dépôt.

Si un package du même nom existe déjà et que le push répond `denied`, vérifier ses paramètres **Manage Actions access** : ce dépôt doit avoir le droit d’écrire le package. Une publication initiale depuis ce workflow associe automatiquement le package au dépôt ; le label OCI `source` indique aussi cette association.

## Autoriser le téléchargement depuis k3s

Un nouveau package GHCR est **privé par défaut**, même si le dépôt source est public. Choisir l’un des deux modes :

- **Image publique** : rendre volontairement le package public dans ses paramètres GitHub, puis garder `imagePullSecretName` vide.
- **Image privée** : créer un Secret Kubernetes `kubernetes.io/dockerconfigjson` pour `ghcr.io`, dans le namespace `arkya-identity`, au moyen du gestionnaire de secrets habituel. Utiliser un compte autorisé à lire le package et un PAT classique limité à `read:packages` ; autoriser le SSO de l’organisation si nécessaire. Indiquer le nom de ce Secret dans `imagePullSecretName`.

Le `GITHUB_TOKEN` du workflow expire et ne convient pas comme identifiant de téléchargement permanent pour k3s. Ne pas enregistrer le PAT dans le JSON du cluster ou dans Git.

Depuis `backend/deploy/identity`, après avoir renseigné le digest et les autres paramètres :

```powershell
./Render-Manifests.ps1 -Configuration .local/cluster.json
```

Pour un déploiement manuel historique, utiliser le digest dans le champ `image` de `.local/cluster.json`, puis suivre le [guide YARP](../gateway/README.md). La chaîne actuelle publie puis actualise GitOps ; elle ne crée pas les secrets du cluster et ne vérifie pas une connexion réelle Discord.

## Vérification de la préparation

Consulter le [guide CI commun](../../../docs/CI.md) et les runs GitHub Actions pour distinguer les vérifications locales de la publication distante. La promotion refuse les releases partielles, les digests de commits différents et une génération dépassée par de nouveaux changements de source.

Références : [publication avec GitHub Actions](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images), [accès au Container Registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry), [compilation Docker multiplateforme](https://docs.docker.com/build/building/multi-platform/).
