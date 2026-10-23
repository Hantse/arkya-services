# Vérification continue de la vitrine

Le job `site` du workflow `../../.github/workflows/frontend-ci.yml`, à la racine
du dépôt `arkya-services`, vérifie le JavaScript de `dist/`, de `tests/` et du
serveur local `serve.mjs`, puis exécute tous les tests
`tests/**/*.test.mjs` avec Node.js 24 LTS. Les nouveaux tests du client de jeu
sont découverts automatiquement. Aucun paquet npm n'est nécessaire.

Le workflow central `../../.github/workflows/services-images.yml` l'appelle sur
les push et les pull requests modifiant le backend, la vitrine, les documents ou
les scripts de CI. `frontend-ci.yml` reste aussi lançable à la demande. Les journaux
de syntaxe et de tests, ainsi qu'un rapport JUnit, restent disponibles pendant
14 jours dans l'artifact `site-frontend-results`, y compris après un échec.
Les contrôles Node ne remplacent pas les essais de navigation dans le navigateur
ou la validation d'un build Unity WebGL distribué séparément.

Depuis ce dossier, sous PowerShell :

```powershell
$sources = Get-ChildItem dist, tests -Recurse -File | Where-Object Extension -In '.js', '.mjs', '.cjs'
$sources += Get-Item serve.mjs
foreach ($source in $sources) {
    node --check $source.FullName
    if ($LASTEXITCODE -ne 0) { throw "Syntax check failed: $($source.FullName)" }
}
$tests = Get-ChildItem tests -Recurse -Filter *.test.mjs -File
node --test $tests.FullName
if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed.' }
```

## Dépôt et publication

`sites/arkya/` est versionné dans le même dépôt `arkya-services` que les API et
services. Les modifications de la vitrine et de sa CI se commitent et se poussent
depuis la racine de ce dépôt. GitHub Actions exécute ce job dans `sites/arkya/`,
après un checkout limité à ce dossier ; il n'a besoin d'aucun dépôt supplémentaire.

Après les tests backend et frontend, la CI démarre également l'image Nginx de la
vitrine en utilisateur 101 avec un système de fichiers en lecture seule. Elle
vérifie les types MIME JavaScript et le maintien du site lorsque le serveur de
jeu manque. `scripts/ci/Test-FrontendProxy.py` vérifie aussi le vrai proxy sur
un réseau Docker isolé : destination configurable, contenu JSON et binaire,
types MIME, cache et suppression des cookies et du bearer vers l'upstream.
Le contexte Docker contient seulement `dist/` et la configuration
Nginx ; les médias Git LFS sont téléchargés et contrôlés avant le build.

Sur la branche par défaut, `services-images.yml` publie
`ghcr.io/hantse/arkya-services-frontend:sha-<commit>` pour Linux AMD64 et ARM64.
Après publication réussie des huit images services/vitrine, les digests sont
promus ensemble dans `backend/deploy/gitops/beta/kustomization.yaml`. Argo CD
utilise cette source Git. Les preuves JSON sont conservées dans les artifacts
`service-image-frontend` et `services-release` pendant 30 jours. Cette chaîne
utilise le `GITHUB_TOKEN` temporaire, sans secret de publication supplémentaire.

L'image sert la vitrine sur le port 8080 et transmet `/game/` au service
`ARKYA_UNITY_UPSTREAM` (`host:port`), en conservant le chemin. Le défaut est
`arkya-web-client.arkya-realm-beta-web.svc.cluster.local:8080` pour la variante
Unity historique ; le profil Admin sélectionne son client dans `arkya-beta`.
Seules cette variable et `NGINX_LOCAL_RESOLVERS` sont substituées dans la
configuration Nginx ; les variables de requête Nginx restent intactes.
Le DNS est relu à l'exécution ; un serveur de jeu absent ne bloque pas
le démarrage du site. Les en-têtes de cache du build Unity sont conservés ; les
pages/scripts/configurations de la vitrine sont revalidés et ses images mises
en cache pendant une heure. Les secrets et l'authentification restent dans les API.

La publication historique sur Sites reste disponible séparément. La configuration
`.openai/hosting.json` sélectionne uniquement `dist/` comme répertoire statique ;
les workflows, les tests et les rapports CI ne font pas partie du site servi.
La publication de l'image dans GHCR et sa promotion GitOps n'actualisent pas
automatiquement cet hébergement Sites.
