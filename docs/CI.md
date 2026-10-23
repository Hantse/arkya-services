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

Les tests backend utilisent temporairement `ubuntu-22.04` : le runner
`ubuntu24/20261018.339` emploie un noyau `7.0.0-1017-azure` sur lequel
MongoDB `8.0.34` refuse de démarrer ([incident MongoDB](https://jira.mongodb.org/browse/SERVER-125742)).
Le runner 22.04 utilise le noyau 6.8 compatible. Le job conserve MongoDB 8
et toutes les suites ; réévaluer ce choix lorsque l'image MongoDB publiée
prend en charge le noyau du runner 24.04.

Les dix images Linux AMD64/ARM64 sont :

- `ghcr.io/hantse/arkya-services-identity`
- `ghcr.io/hantse/arkya-services-gateway`
- `ghcr.io/hantse/arkya-services-characters`
- `ghcr.io/hantse/arkya-services-content`
- `ghcr.io/hantse/arkya-services-admin`
- `ghcr.io/hantse/arkya-services-realms`
- `ghcr.io/hantse/arkya-services-backend` (API historique privée)
- `ghcr.io/hantse/arkya-services-frontend`
- `ghcr.io/hantse/arkya-services-inference`
- `ghcr.io/hantse/arkya-services-reports`

Seule la branche par défaut publie ; les autres branches et PR construisent
sans publication. Le job final exige dix digests du même commit et met à
jour les fichiers `kustomization.yaml` de `backend/deploy/gitops/beta`,
`inference-control` et `reports-control`. Une publication partielle
ou devenue obsolète n'est pas promue. Les tags `sha-<commit>` identifient les
sources ; Argo CD utilise les digests immuables. Les preuves de publication
sont conservées 30 jours. Les commits de promotion ne rebouclent pas la CI.

## Unity et vraie bêta web

Les runs Unity vont jusqu'au bout : un nouveau push n'annule plus le run
actif ni le précédent run en attente. Le groupe de branche et les verrous de
promotion utilisent `cancel-in-progress: false` avec `queue: max`. GitHub
permet ainsi jusqu'à 100 exécutions en attente par groupe ; au-delà, sa limite
s'applique ([syntaxe officielle](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#example-queueing-multiple-pending-runs)).
Les promotions restent sérialisées et refusent les sources devenues obsolètes.
Les agents ne doivent annuler aucun run ou job sans demande explicite de
l'utilisateur, y compris pour libérer un runner ou privilégier un commit récent.

Le workflow Unity exécute EditMode et PlayMode sans filtre, puis construit le
client Windows, le client WebGL et le vrai dedicated Linux de l'île via
`CharacterWebBetaBuilder`. Le prototype AreaPrototype n'est plus cette cible.

Unity 6000.6.6f1 utilise un runner Windows x64 licencié avec les modules Windows
Mono, WebGL IL2CPP et Linux Dedicated Server Mono. La cible Linux est compilée
sur Windows, puis empaquetée et contrôlée sur Ubuntu Actions. La variable
`UNITY_WINDOWS_EDITOR_PATH` indique l'exécutable. Le [guide Unity](https://github.com/Hantse/arkya-mmorpg/blob/main/ci/README.md)
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
est désactivé pour éviter une double propriété. Le catalogue est initialement
désactivé ; après les contrôles de santé, TLS et WSS, l'opérateur l'active par
Git et sync pour valider l'admission et la persistance avant ouverture. La gateway conserve
son LoadBalancer `192.168.100.218:80`. Le site et WSS utilisent le Traefik du
cluster, avec une route joignable depuis la VM externe.

## Vérifications des 18 et 19 octobre 2026

Les sept suites backend ont passé 502 tests, aucun ignoré, avec MongoDB
temporaire. `actionlint`, les tests de promotion et le rendu Kustomize vérifient
la configuration. Les runs distants et les commits de digests sont les preuves
de livraison. Un build vert ne valide pas l'OAuth public, les certificats du
cluster ni une admission WSS réelle après déploiement.

- [Services : livraison réussie](https://github.com/Hantse/arkya-services/actions/runs/39126104197) :
  502 tests backend, 94 tests frontend, huit images GHCR AMD64/ARM64 et
  leurs digests promus dans Git.
- [Unity : tests, trois builds et publications réussis](https://github.com/Hantse/arkya-mmorpg/actions/runs/39130130384) :
  421 tests EditMode et trois PlayMode passent ; une fixture API réelle est
  explicitement ignorée faute de `ARKYA_WEB_BETA_TICKETS_FILE`. Windows,
  WebGL et le vrai dedicated Linux Server sont construits sans erreur.
  Le dedicated a nécessité une relance avec un seul worker Burst pour
  respecter la mémoire engagée du runner. Des warnings Unity subsistent.
  Les deux conteneurs ont passé leur smoke et sont publiés.
- [Unity : reprise de promotion réussie](https://github.com/Hantse/arkya-mmorpg/actions/runs/39138336158) :
  le run précédent reste rouge pour sa seule étape de promotion, qui
  cherchait les JSON à la racine au lieu de `release/`. Le correctif et la
  reprise vérifiée sont poussés. Les huit jobs réussis et l'absence de
  changement des entrées de compilation sont contrôlés avant de promouvoir
  les images du SHA original `24b2f84c6763559d01b63918621f87a425d46b31`.

Les dix références GHCR ont été contrôlées par digest. Les 45 ressources
rendues passent le contrat combiné services/Unity avec `--require-digests`.
Le namespace de contrôle Argo est `argocd` et le site cible `arkya.online`.
Le bootstrap et les commandes de sync sont prêts ; aucun sync de cluster ni
test public WSS/admission n'a été effectué depuis cette session.

Le parent `arkya-services` utilise le projet administratif `arkya-bootstrap`,
appliqué séparément depuis `backend/deploy/gitops/bootstrap-project.yaml`.
Il crée le projet runtime et l'application multi-source `arkya-beta` dans
`argocd`. Le projet runtime reste limité aux workloads et ne peut pas créer
ces objets Argo. Les sources enfants utilisent les deux URL SSH GitHub pour
les deploy keys enregistrées dans Argo CD. Voir la procédure GitOps avant
de relancer le parent puis l'enfant.

La vitrine est intégrée au dépôt des services. Son ancien Git local est
sauvegardé sous `artifacts/ci-validation/site-repository-backup-20261018/git`,
hors versionnement. La publication Sites manuelle reste distincte de cette
livraison GHCR/Argo CD.
