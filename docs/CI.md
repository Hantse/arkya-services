# Livraison GitHub → GHCR → Argo CD

Deux dépôts possèdent la livraison : `arkya-services` pour toutes les API,
la documentation et la vitrine `sites/arkya/`, et `arkya-mmorpg` pour Unity.
Chaque dépôt publie ses images puis promeut leurs digests dans son propre Git
avec son `GITHUB_TOKEN`. Aucun jeton personnel entre dépôts n'est nécessaire.

## Services et frontend

Le workflow `.github/workflows/services-images.yml` appelle les contrôles
réutilisables `backend-ci.yml` et `frontend-ci.yml`, puis valide le packaging,
les manifests et la sécurité des promotions. Il remplace les trois anciens
workflows d'images.

Le backend découvre tous les `.csproj` versionnés, compile une solution
Release temporaire et exécute toutes les suites avec MongoDB jetable. Le
frontend vérifie les sources et tous les tests Node des viewers et du site
avec Node.js 24 LTS. Les rapports TRX/JUnit sont conservés 14 jours.

Les huit images Linux AMD64/ARM64 sont :

- `ghcr.io/hantse/arkya-services-identity`
- `ghcr.io/hantse/arkya-services-gateway`
- `ghcr.io/hantse/arkya-services-characters`
- `ghcr.io/hantse/arkya-services-content`
- `ghcr.io/hantse/arkya-services-admin`
- `ghcr.io/hantse/arkya-services-realms`
- `ghcr.io/hantse/arkya-services-backend` (API historique privée)
- `ghcr.io/hantse/arkya-services-frontend`

Seule la branche par défaut publie ; les autres branches et PR construisent
sans publication. Le job final exige huit digests du même commit et met à
jour `backend/deploy/gitops/beta/kustomization.yaml`. Une publication partielle
ou devenue obsolète n'est pas promue. Les tags `sha-<commit>` identifient les
sources ; Argo CD utilise les digests immuables. Les preuves de publication
sont conservées 30 jours. Les commits de promotion ne rebouclent pas la CI.

## Unity et vraie bêta web

Le workflow Unity exécute EditMode et PlayMode sans filtre, puis construit le
client Windows, le client WebGL et le vrai dedicated Linux de l'île via
`CharacterWebBetaBuilder`. Le prototype AreaPrototype n'est plus cette cible.

Unity 6000.6.6f1 utilise un runner Windows x64 licencié avec les modules Windows
Mono, WebGL IL2CPP et Linux Dedicated Server Mono. La cible Linux est compilée
sur Windows, puis empaquetée et contrôlée sur Ubuntu Actions. La variable
`UNITY_WINDOWS_EDITOR_PATH` indique l'exécutable. Le [guide Unity](../Unity3D/Arkya/ci/README.md)
décrit le runner éphémère, les commandes et les artefacts. Les contributions
de forks doivent être revues puis importées sur une branche de confiance.

Après tous les builds, les conteneurs sont testés au démarrage et à l'arrêt,
puis publiés sous `ghcr.io/hantse/arkya-mmorpg-dedicated` et
`ghcr.io/hantse/arkya-mmorpg-web-client`. Un commit unique promeut les deux
digests dans `deploy/gitops/beta/kustomization.yaml` du dépôt Unity. La vitrine
relaie `/game/*` au Service du client WebGL, sur la même origine publique.

## Argo CD

L'[Application multi-source](../backend/deploy/gitops/bootstrap/application.yaml)
réunit les ressources des deux dépôts. Le [guide GitOps](../backend/deploy/gitops/README.md)
décrit les namespaces, les secrets, les certificats, les routes et l'adoption
des ressources existantes. Le premier sync reste manuel et attend les dix
digests promus. Les pipelines ne possèdent pas de kubeconfig et ne modifient
ni le cluster, ni le DNS, ni les secrets.

Argo possède les workloads de cette bêta ; le déployeur Kubernetes de Realms
est désactivé pour éviter une double propriété. Le catalogue reste désactivé
jusqu'au contrôle public WSS/admission par l'opérateur. La gateway conserve
son LoadBalancer `192.168.100.218:80`. Le site et WSS utilisent le Traefik du
cluster, avec une route joignable depuis la VM externe.

## Vérification locale du 18 octobre 2026

Les sept suites backend ont passé 502 tests, aucun ignoré, avec MongoDB
temporaire. `actionlint`, les tests de promotion et le rendu Kustomize vérifient
la configuration. Les runs distants et les commits de digests sont les preuves
de livraison. Un build vert ne valide pas l'OAuth public, les certificats du
cluster ni une admission WSS réelle après déploiement.

La vitrine est intégrée au dépôt des services. Son ancien Git local est
sauvegardé sous `artifacts/ci-validation/site-repository-backup-20261018/git`,
hors versionnement. La publication Sites manuelle reste distincte de cette
livraison GHCR/Argo CD.
