# Construire et publier Identity avec GitHub Actions

Le workflow [Identity image](../../../.github/workflows/identity-image.yml) construit `backend/Dockerfile.identity` et publie dans **GitHub Container Registry (GHCR)**. Le contexte Docker est `backend/` ; le checkout sélectionne ce dossier.

## Déclenchement et résultat

| Événement | Comportement |
| --- | --- |
| Push sur la branche par défaut, avec modification du backend ou du workflow | Tests, build et publication GHCR |
| Pull request ou push sur une autre branche, avec les mêmes modifications | Tests et build, sans publication |
| **Actions → Identity image → Run workflow**, branche par défaut | Tests, build et publication GHCR |
| Lancement manuel sur une autre branche | Tests et build, sans publication |

La branche par défaut est détectée automatiquement : aucun nom `main` ou `master` n’est imposé. Les tags Git ne déclenchent pas le workflow. Les filtres couvrent aussi les fichiers de configuration .NET/NuGet à la racine. Ne pas rendre ce workflow filtré obligatoire pour toutes les pull requests : une PR ne touchant que Unity ne le déclenche pas.

Les tests Identity s’exécutent en Release avec un MongoDB 8.0 jetable, ce qui active aussi les trois tests de persistance. Ils n’accèdent ni au MongoDB de production ni à Discord. Un échec empêche le build et la publication. Buildx construit ensuite **Linux AMD64 et ARM64** par compilation croisée, sans QEMU.

L’image prend automatiquement ce nom, en minuscules :

```text
ghcr.io/<proprietaire>/<depot>-identity:sha-<commit-complet>
```

Par exemple, un dépôt `MonOrganisation/Arkya` publie `ghcr.io/monorganisation/arkya-identity`. Le résumé du job **Publish to GHCR** donne le tag et la référence **`image@sha256:…`** à copier dans le champ `image` de `.local/cluster.json`. Préférer le digest pour déployer exactement l’image vérifiée : un relancement sur le même commit peut remplacer son tag SHA. Aucun tag `latest` n’est utilisé.

## Première exécution

1. Versionner `.github/workflows/identity-image.yml` à la **racine du dépôt GitHub contenant `backend/`**, avec le Dockerfile, `.dockerignore`, les sources Identity/ServiceDefaults et les tests Identity. Le dépôt séparé `sites/arkya` héberge la vitrine et n’est pas la destination de ce workflow.
2. Autoriser GitHub Actions et les actions GitHub/Docker utilisées si l’organisation applique une liste d’autorisation. Le job de publication demande `contents: read` et `packages: write` ; la politique de l’organisation doit permettre la publication de packages.
3. Une fois le workflow présent sur la branche par défaut, ouvrir **Actions → Identity image → Run workflow**, sélectionner cette branche et lancer. Un push modifiant les chemins surveillés déclenche aussi la publication.
4. Après réussite, récupérer le digest dans le résumé et l’utiliser avec le profil Identity `gateway` décrit dans le [guide YARP](../gateway/README.md). L’image Gateway est publiée par son propre workflow.

**Aucun secret GitHub personnalisé n’est nécessaire pour publier.** Le workflow utilise le `GITHUB_TOKEN` temporaire fourni par GitHub. Les secrets Discord, la connexion MongoDB, les clés de service et le kubeconfig ne sont pas transmis au build : ils restent dans la configuration de déploiement.

Le checkout ne conserve pas ses identifiants Git. Les actions sont épinglées à des commits complets, avec leur version en commentaire ; actualiser ces références lors des mises à jour. Seul le job de publication dispose de `packages: write`. Les pull requests utilisent l’événement `pull_request`, sans connexion à GHCR et sans écriture du cache partagé.

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

Le workflow publie l’image ; l’application des manifests et la vérification de la connexion Discord suivent le [guide de déploiement YARP](../gateway/README.md). Il ne change ni Traefik, ni le DNS, ni le cluster.

## Vérification de la préparation

Le 16 octobre 2026, le workflow passe `actionlint 1.7.12` et les tests Identity en Release donnent **144 réussites, trois tests MongoDB ignorés localement** faute de `ARKYA_TEST_MONGO`. Ces trois tests sont activés dans la CI grâce au service MongoDB jetable. Les références des actions ont été vérifiées auprès de leurs dépôts officiels. Le build Docker complet, les tests MongoDB sur le runner et le push GHCR restent à confirmer lors de la première exécution GitHub : aucun workflow distant ni push d’image n’a été exécuté pendant cette préparation.

Références : [publication avec GitHub Actions](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images), [accès au Container Registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry), [compilation Docker multiplateforme](https://docs.docker.com/build/building/multi-platform/).
