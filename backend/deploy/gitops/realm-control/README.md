# Déploiement des realms bêta

Ce composant GitOps prépare le namespace **`arkya-beta-realm`**. Il est
inclus par le profil Admin de l'Application Argo existante **`arkya-beta`**.
Argo possède le namespace, le RBAC, les règles réseau et le Service stable.
`Arkya.Realms.Api` possède les Deployments et Services dédiés qu'un opérateur
lance depuis l'admin. Aucun Deployment Unity ni Secret n'est créé ici.

Le compte de service `arkya-realm-deployer` reste dans `arkya-beta`. Son rôle
dans `arkya-beta-realm` autorise `get/create` sur les Deployments et Services,
la lecture des seuls Secrets `arkya-ghcr` et `arkya-dedicated`, et leur création. Kubernetes
ne permet pas de limiter `create` par nom : le code Realms fixe et valide le
nom fixe et le namespace avant cet appel. Aucun droit `list`, `update`, `patch` ou
`delete`, autre namespace ou objet du cluster n'est accordé.
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
Le Secret de registre existant `arkya-ghcr` de `arkya-beta` est monté en
lecture seule uniquement dans Realms, via
`RealmRuntime__RegistryDockerConfigFile=/var/run/secrets/arkya-registry/config.json`.
Avant de créer le Deployment, Realms valide puis provisionne le Secret
`arkya-ghcr` dans le namespace autorisé et le référence comme
`ImagePullSecretName`. Les identifiants ne passent ni dans Git, ni dans les
logs, API ou profils MongoDB. Le Secret cible reste détenu par Realms ;
aucun Secret déclaratif ni droit Argo sur le type Secret n'est ajouté.

Le TLS public utilise le certificat existant de `api.arkya.online`.
Le namespace cible ne requiert aucun certificat, Ingress, Secret à copier
manuellement, adresse LoadBalancer ou changement DNS. L'ancienne variante Unity déclarant
`beta-game.arkya.online` reste indépendante de cette route.

## Contrôle hors cluster

```powershell
python D:\Repositories\arkya\backend\deploy\gitops\realm-control\Validate-Manifests.py
kubectl kustomize D:\Repositories\arkya\backend\deploy\gitops\admin
```

Après synchronisation, vérifier le rôle réel avec `kubectl auth can-i`,
la validité de la route TLS sans afficher de secret, puis
créer et lancer **`beta-web`** avec le profil de l'île depuis l'admin.
Le Service stable n'a aucun endpoint tant que le dedicated n'a pas
terminé le chargement du terrain et créé `/tmp/arkya-ready`.
