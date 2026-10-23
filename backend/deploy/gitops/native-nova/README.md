# Nova natif : configuration de quatre partitions

Ce composant prépare le profil `nova-native` et le transport WSS du realm
`nova` dans la cible existante `beta-web` (`arkya-beta-realm`). Il utilise
uniquement le serveur natif `3 / arkya-multi-area-v1`, publié depuis
`cf850219963432742a91938f47a60223be912a90` par la CI Unity `39992052485`,
avec le digest immuable épinglé dans `realms-config.yaml`.

Le périmètre provisionné est **Starter Island, quatre partitions** :
`island-starter-island-01` à `island-starter-island-04`. Le plan Nova observé
est `starter-island`, contenu `starter-island`, dimensions 1536 × 1536,
origine X/Z 0/0 et grille 2 × 2, avec zéro instance et zéro raid. Aucun portail
vers un autre contenu n'est provisionné ici. Un autre nombre de partitions,
une autre Area ou un autre realm exige ses propres routes, Services et règles
réseau avant déploiement. Le modèle d'endpoint générique ne les crée pas.

Les quatre chemins `/game/nova/{serverId}/` de la gateway existante rejoignent
quatre Services privés sur TCP 17943. Leurs sélecteurs lient exactement le
realm et le serveur. Les trois NetworkPolicies autorisent seulement la gateway
vers ces listeners, puis ces dedicateds vers Characters et Realms sur TCP 8080.
Le TLS public et le LoadBalancer existants sont réutilisés.

Le profil historique `beta-web`, ses intentions de release et le client WebGL
restent inchangés. Le composant historique `partition-handoff` reste séparé et
inactif : les deux composants réservent le même index de profil et les mêmes
indices de routes ; leur activation simultanée est refusée par la validation.

Ce composant ne possède **aucun Deployment dédié, Secret, RuntimeRelease,
ImageRelease, namespace ou RBAC**. Il n'active aucun realm. Characters garde
ses identités dynamiques fondées sur le catalogue privé et la graine existante,
commune avec Realms. Les Secrets dédiés et les workloads seront provisionnés
par Realms seulement lors d'une demande de déploiement autorisée. Les
ConfigMaps générés changent de nom avec leur contenu, ce qui actualise les
références des pods Gateway et Realms sans annotation de rollout supplémentaire.

## Adoption et action opérateur distincte

Ajouter `../native-nova` à `components` dans `../admin/kustomization.yaml`,
valider les manifests, puis publier les YAML par le flux GitOps habituel.
La synchronisation expose le profil dans Admin mais conserve le snapshot
historique de tout realm déjà enregistré.

Pour Nova encore **Draft**, réenregistrer explicitement le plan existant avec
le profil `nova-native` et sa révision attendue courante. Cette action fige le
nouveau snapshot compatible ; elle ne demande pas le déploiement. Relire le
plan, les quatre serveurs et la compatibilité, puis utiliser **Deploy** comme
action séparée et autorisée. Ne pas recréer le realm ni migrer automatiquement
les personnages. Le namespace cible reste exclusif à un seul realm actif.

Un déploiement ultérieur nécessite encore le téléchargement de l'image,
la readiness réelle des quatre dedicateds et les contrôles publics d'admission,
de handoff et de persistance. Une validation YAML ne démontre aucun de ces
parcours réseau.

## Vérification hors ligne

Depuis la racine du dépôt services, avant adoption :

```powershell
python backend/deploy/gitops/native-nova/Validate-Manifests.py
```

Après ajout du composant :

```powershell
python backend/deploy/gitops/native-nova/Validate-Manifests.py --adopted
python backend/deploy/gitops/Test-Manifests.py --require-digests
python -m unittest discover -s scripts/ci -p test_native_nova_manifests.py -v
python -m unittest discover -s scripts/ci -p test_partition_handoff_manifests.py -v
```

Ces contrôles utilisent seulement `kubectl kustomize` et des copies temporaires.
Le validateur exige le delta complet exact, puis applique les gardes existantes
au rendu historique normalisé. Une route, policy, identité, paire réseau ou
release supplémentaire est refusée ; aucune garde historique n'est désactivée.
