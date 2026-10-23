# Déploiement des realms bêta

Ce composant GitOps prépare le namespace **`arkya-beta-realm`**. Il est
inclus par le profil Admin de l'Application Argo existante **`arkya-beta`**.
Argo possède le namespace, le RBAC, les règles réseau et le Service stable.
`Arkya.Realms.Api` possède les Deployments et Services dédiés qu'un opérateur
lance depuis l'admin. Aucun Deployment Unity ni Secret n'est créé ici.

Le compte de service `arkya-realm-deployer` reste dans `arkya-beta`. Son rôle
dans `arkya-beta-realm` autorise `get/create/patch` sur les Deployments et
`get/create` sur les Services et `get/list` sur les Pods de ce seul namespace,
la lecture des seuls Secrets `arkya-ghcr` et `arkya-dedicated`, leur création
et le patch du seul Secret nommé `arkya-dedicated`. Kubernetes
ne permet pas de limiter `create` par nom : le code Realms fixe et valide le
nom fixe et le namespace avant cet appel. Le patch est limité dans le code
à une transition d'image versionnée ou à l'annotation de redémarrage d'un
Deployment possédé, ou à son retrait pendant une migration approuvée, avec
tests de concurrence ; voir les
[releases GitOps](../../../../docs/REALM_DEPLOYMENT.md#mettre-à-jour-une-image-par-gitops)
et les [actions d'exploitation](../../../../docs/REALM_OPERATIONS.md).
La [migration de runtime](../../../../docs/REALM_RUNTIME_RELEASES.md) remplace
les clés dédiées seulement après vérification de l'ancien contenu exact et de
`resourceVersion`. Elle vérifie aussi l'absence de Pods par sélecteurs exacts
realm/serveur/identité de déploiement, avec pagination bornée. Cette lecture
inclut les Pods en terminaison même quand les compteurs du Deployment ne les
exposent pas. Les Pods ne sont jamais modifiés par Realms.
Aucun droit de lister les Secrets, ni `update` ou `delete`, autre namespace
ou objet du cluster n'est accordé.
Le pod Realms reçoit explicitement un jeton projeté et la CA Kubernetes ;
les autres services ne reçoivent aucun jeton.

La route **`wss://api.arkya.online/game/beta-web/`** passe par le Traefik
externe et la gateway YARP existants, puis rejoint le Service privé
`arkya-beta-web-island.arkya-beta-realm.svc.cluster.local:17943`.
YARP retire le préfixe `/game/beta-web` avant de joindre le listener Unity.
Le sélecteur du Service suit les labels Realms
`beta-web/island`, sans dépendre du nom aléatoire généré pour le Deployment.
L'accès TCP au dedicated est réservé aux pods `arkya-gateway` de `arkya-beta`.
Les autres pods du namespace n'acceptent aucune connexion entrante.
Le dedicated peut joindre Characters pour l'admission et les sauvegardes ;
Characters peut consulter le catalogue Realms.

## Conditions de synchronisation

Le projet Argo `arkya-beta` doit autoriser la destination
`arkya-beta-realm`, le type cluster `Namespace`, et les types namespacés
`ServiceAccount`, `Role`, `RoleBinding`, `Service`, `NetworkPolicy`.
Ces autorisations ont été corrigées et vérifiées dans l'interface Argo le
**20 octobre 2026 à 21:15 UTC** (23:15 à Bruxelles). L'Application vivante
`arkya-beta` suit `backend/deploy/gitops/admin` sur `master`, dans le projet
`arkya-beta`. Aucun propriétaire Git actif de ce projet n'a été observé.
Son interdiction globale des ressources cluster empêchait toute création
de Namespace ; elle a été retirée. L'autorisation cluster est maintenant
limitée à `group: ''`, `kind: Namespace`, **`name: arkya-beta-realm`**.
Les autres ressources cluster restent refusées. Argo documente cette
[restriction par nom](https://argo-cd.readthedocs.io/en/stable/user-guide/projects/#restrict-cluster-scoped-resources-by-name).

`project-access.patch.yaml` conserve les huit opérations de cette correction
historique : deux tests de l'état initial, la restriction du nom Namespace,
le retrait de la seule interdiction globale observée, puis quatre ajouts
de destination et de types RBAC. Les tests échouent si le projet a déjà été
corrigé ou si sa blacklist diffère ; ne pas le rejouer aveuglément. Les
autres destinations et whitelists sont conservées. Aucun AppProject ni
Application Argo ne doit être ajouté aux ressources des workloads.

Le modèle `../bootstrap/project.yaml` reste générique pour la variante
complète qui crée plusieurs namespaces de services. Son autorisation
Namespace sans nom est volontairement plus large que celle du projet
vivant. Modifier ce modèle ne met pas à jour le projet vivant en l'absence
de propriétaire Git correspondant ; ne pas l'appliquer pour remplacer la
configuration réelle.

## Observation des rollouts dans Argo

Argo CD **3.3.9** filtre aussi les enfants du Deployment selon
`namespaceResourceWhitelist`. Sans `Pod` et `apps/ReplicaSet`, l'arbre et
la vue Pods restent vides même si Kubernetes les a créés. Ouvrir un nom de
ReplicaSet directement ne contourne pas ce filtre : l'API exige qu'il
appartienne à l'arbre autorisé de l'Application.

L'ajout de ces deux types a été appliqué et relu dans Argo pour le diagnostic
du **21 octobre 2026 vers 00:33 UTC**, séparément de la correction du 20 octobre.
`project-observability.patch.yaml` conserve trois opérations : test de la
liste vivante exacte de neuf types, puis ajout de `Pod` et `ReplicaSet`.
La liste vivante compte désormais onze types, dans cet ordre d'ajout.
Il n'altère ni les autres listes, ni la limite Namespace, ni le rôle Realms,
ni les droits sur les Secrets. L'allowlist du projet n'est pas un droit de
lecture distinct : elle autorise également Argo à appliquer ces types.
Ce composant ne déclare aucun Pod ou ReplicaSet ; ils restent créés par
les contrôleurs Kubernetes des Deployments.

Pour diagnostiquer un rollout, examiner le manifeste **Live** du Deployment
et ses événements : `metadata.generation`, `status.observedGeneration`,
`updatedReplicas`, puis les conditions `ReplicaFailure` et `Progressing`.
Un replica ancien disponible ou une API répondant ne prouve pas que la
nouvelle révision est prête. Une fois les enfants visibles, consulter le
ReplicaSet courant, l'état des conteneurs et les événements/logs du Pod.
Voir le [filtrage dans le contrôleur Argo 3.3.9](https://github.com/argoproj/argo-cd/blob/v3.3.9/controller/appcontroller.go#L580-L595).

La clé dédiée est dérivée par Realms à partir d'une graine existante.
Avant les workloads, Realms crée le Secret Opaque fixe `arkya-dedicated`
dans le namespace du realm ; ses entrées sont indexées par ID de serveur
(`island` pour ce profil). Le Deployment reçoit seulement un `secretKeyRef`
pour `ARKYA_DEDICATED_KEY`, jamais une valeur de clé en clair dans son YAML.
Characters dérive la même clé pour `beta-web/island`. La valeur ne figure
ni dans Git, ni dans les logs, ni dans les DTO, ni dans le profil MongoDB ;
aucun Secret de clé dédiée n'est à recopier manuellement dans la cible.

Le profil fixe `ARKYA_CHARACTERS_URL` sur Characters en HTTP privé, avec
une autorisation explicite du transport dans le nouveau dedicated. La
validation se limite aux origines Kubernetes autorisées ; les autres
destinations conservent HTTPS. L'image privée est épinglée par digest.
Le profil requiert le Secret de registre source `arkya-ghcr` dans
`arkya-beta`, à monter en lecture seule uniquement dans Realms, via
`RealmRuntime__RegistryDockerConfigFile=/var/run/secrets/arkya-registry/config.json`.
Avant de créer le Deployment, Realms valide puis provisionne le Secret
`arkya-ghcr` dans le namespace autorisé et le référence comme
`ImagePullSecretName`. Les identifiants ne passent ni dans Git, ni dans les
logs, API ou profils MongoDB. Le Secret cible reste détenu par Realms ;
aucun Secret déclaratif ni droit Argo sur le type Secret n'est ajouté.

## Secret GHCR et livraison GitOps

Le flux de livraison est **modification des YAML → push Git → synchronisation
automatique Argo CD**. Aucun script local de création de Secret, kubeconfig
ou accès SSH n'est demandé pour préparer et publier ces manifests.

[`registry-secret.example.yaml`](registry-secret.example.yaml) décrit le Secret
source attendu : `arkya-beta/arkya-ghcr`, type `kubernetes.io/dockerconfigjson`,
clé `.dockerconfigjson`, entrée JSON `auths.ghcr.io.auth`. La valeur `auth` est
la représentation base64 UTF-8 de `utilisateur:PAT`, avec un droit de lecture
`read:packages`. Elle reste vide dans l'exemple.

Ce modèle est exclu de Kustomize et ne doit pas être synchronisé tel quel.
La valeur réelle doit être fournie par la gestion des secrets de l'infrastructure,
sans commiter un jeton ou son encodage base64. Aucun contrôleur de secrets
chiffrés n'est déclaré dans ce dépôt ; ce modèle ne suppose donc pas la présence
de Sealed Secrets ou d'External Secrets. Le namespace cible du realm conserve
les Secrets créés par Realms. Le rendu applicatif reste à 48 ressources.

### État observé le 21 octobre 2026 à 00:40 UTC

Argo a terminé la synchronisation des 48 ressources de `a635317`
(`SyncSucceeded`) ; Characters et Gateway utilisent leur nouvelle
configuration Healthy. Le nouveau pod Realms est toutefois en FailedMount,
car **`arkya-ghcr` n'existe pas dans le namespace source `arkya-beta`**.
Ses références antérieures dans les Deployments ne prouvaient pas sa
présence. L'ancien pod Realms reste Ready. La nouvelle copie des Secrets
vers la cible et le lancement du dedicated ne sont donc pas encore validés ;
le Secret source doit être provisionné puis le rollout Realms réussir.

Le TLS public utilise le certificat existant de `api.arkya.online`.
Le namespace cible ne requiert aucun certificat, Ingress, Secret à copier
manuellement, adresse LoadBalancer ou changement DNS. L'ancienne variante Unity déclarant
`beta-game.arkya.online` reste indépendante de cette route.

## Contrôle hors cluster

```powershell
python D:\Repositories\arkya\backend\deploy\gitops\realm-control\Validate-Manifests.py
kubectl kustomize D:\Repositories\arkya\backend\deploy\gitops\admin
```

Après synchronisation Argo et disponibilité du Secret source,
vérifier la route TLS puis
créer et lancer **`beta-web`** avec le profil de l'île depuis l'admin.
Le Service stable n'a aucun endpoint tant que le dedicated n'a pas
terminé le chargement du terrain et créé `/tmp/arkya-ready`.
