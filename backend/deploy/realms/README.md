# Backoffice et déploiement k3s des realms

Cette tranche ajoute `Arkya.Realms.Api` et l'écran **Realms** d'`Arkya.Admin`. Elle crée et observe des serveurs depuis des profils contrôlés, dans des namespaces préautorisés. La [conception réseau et les limites](../../../docs/REALM_DEPLOYMENT.md) distinguent le déploiement de l'admission des joueurs.

## Images

Le workflow commun [Services images](../../../.github/workflows/services-images.yml) teste tout le backend, la vitrine et les contrats de déploiement, puis construit huit images. Sur la branche par défaut, il publie notamment `ghcr.io/hantse/arkya-services-admin` et `-realms`, pour Linux AMD64 et ARM64. Après réussite de toutes les publications, leurs digests sont épinglés ensemble dans la [source GitOps bêta](../gitops/README.md) pour Argo CD. Les autres branches et pull requests construisent sans publication.

Construction locale depuis `backend/`, avec un moteur Docker Linux :

```powershell
docker build -f Dockerfile.service --build-arg APPLICATION=Arkya.Admin -t arkya-admin:local .
docker build -f Dockerfile.service --build-arg APPLICATION=Arkya.Realms.Api -t arkya-realms:local .
```

Les images ASP.NET sont distinctes de l'image Unity dedicated. Cette dernière doit être publiée séparément ; aucun chemin vers un build Windows n'est accepté comme image de conteneur Linux.

## Configuration et profils

Depuis `backend/deploy/realms` :

```powershell
New-Item -ItemType Directory -Path .local -Force
Copy-Item cluster.example.json .local/cluster.json
```

Renseigner les digests Admin, Realms et dedicated. `operationsNamespace` héberge les deux applications ; `gatewayNamespace` désigne le namespace existant de YARP. `trustedGatewayPodNetworks` doit correspondre aux véritables sources des pods gateway. Le CIDR `10.42.0.0/16` est un exemple k3s à vérifier.

Un `deployment.targets` associe `id`, `displayName` et `namespace`. Chaque namespace cible est réservé à un realm. `deployment.profiles` décrit l'image par digest et jusqu'à 16 serveurs nommés avec leur port non privilégié et leurs arguments. Le transport est `Udp` par défaut. Le profil distinct `WebSocket` utilise TCP et exige `publicEndpoint` WSS, protocole `1` et build `arkya-web-beta-v1` ; voir [la préparation web bêta](../web-beta/README.md). `environmentSecretName` et `imagePullSecretName` sont facultatifs ; ces Secrets doivent déjà exister dans chaque namespace cible utilisé.

Le profil est figé lors de la création du realm. Modifier le JSON ne met pas à jour les realms existants. Ajouter une autre cible pour créer un autre realm ; ne pas réaffecter le namespace d'un realm existant.

Le contrat fourni au conteneur est :

```text
ARKYA_REALM_ID=<identifiant stable>
ARKYA_SERVER_ID=<identifiant du processus/area>
ARKYA_GAME_PORT=<port UDP ou TCP>
ARKYA_GAME_TRANSPORT=<Udp ou WebSocket>
ARKYA_BUILD_ID=<identifiant compatible>
```

L'entrypoint de l'image doit consommer ces valeurs ou traduire les `args` du profil vers sa configuration Unity. Le prototype local actuel ne consomme pas automatiquement ce nouveau contrat. La communication entre areas, les versions de contenu, l'accès aux services et les secrets de jeu doivent être pris en charge par l'image approuvée. Le déployeur ne fabrique pas une topologie de simulation à partir du nombre de réplicas.

Le worker utilise l'API Kubernetes HTTPS et le jeton projeté du compte de service, relu à chaque requête. La CA du cluster sert à vérifier la chaîne TLS et le nom du serveur. La configuration par défaut vise le cluster dans lequel Realms s'exécute. `ReconcileIntervalSeconds` vaut 10, `DeploymentTimeoutSeconds` 300 ; leurs bornes sont respectivement 2–300 et 30–3600 secondes.

## Secrets et TLS à préparer

Aucun secret n'est généré ou écrit dans les manifests. Dans `operationsNamespace`, provisionner par le mécanisme habituel de l'exploitation :

| Secret | Clés requises |
| --- | --- |
| `arkya-operations-secrets` | `admin-password` (16–512 caractères), `realms-admin-key` (32–512 caractères aléatoires) |
| `arkya-admin-mongo` | `connection-string` : utilisateur limité à la base `arkya_admin`, avec lecture, écriture et création d'index |
| `arkya-realms-mongo` | `connection-string` : utilisateur limité à la base `arkya_realms` |
| `arkya-realms-tls` | `tls.crt`, `tls.key`, `ca.crt` pour le lien Admin → Realms |
| Secret de registre facultatif | Nom fixé par `imagePullSecretName`, credentials de lecture des images |

Le certificat Realms doit couvrir `arkya-realms.<operationsNamespace>.svc` et être signé par la CA de `ca.crt`. Fournir la chaîne intermédiaire nécessaire dans `tls.crt`. La CA est montée dans Admin et définie par `RealmsApi:CertificateAuthorityFile` ; le nom du serveur reste vérifié. Sans cette option, le client utilise les autorités système. La CA privée n'emploie pas de vérification CRL/OCSP ; gérer sa rotation et des certificats à durée bornée. Les clés TLS ne sont montées que dans Realms.

Admin possède un PVC de clés Data Protection, un réplica et une stratégie `Recreate`. Les Secrets sont lus au démarrage : redémarrer les applications lors de leur rotation. Les ConfigMaps montées par `subPath` nécessitent également un redémarrage après modification. Les Secrets de jeu et de registre référencés par les profils appartiennent aux namespaces cibles ; le déployeur n'a pas le droit de les lire via l'API Kubernetes.

Admin conserve ses sessions dans `arkya_admin`, collection `admin_sessions`.
Préparer `arkya-admin-mongo` avant la première mise à jour qui active les refresh
tokens : le démarrage vérifie MongoDB et crée l'index TTL. L'accès dure 30 minutes,
avec renouvellement automatique jusqu'à la limite absolue de 30 jours ; une
panne de stockage refuse l'accès. Les anciens cookies demandent une reconnexion.
Voir le [contrat des sessions administrateur](../../../docs/ADMIN_SESSIONS.md)
pour la rotation, la révocation et les durées configurables.

## Générer et appliquer

```powershell
./Render-Manifests.ps1 -Configuration .local/cluster.json
```

Le fichier `.local/operations.json` est une List Kubernetes : namespace d'exploitation, compte de service, ConfigMaps, PVC, deux Deployments, deux Services ClusterIP, NetworkPolicies, namespaces cibles et leurs Role/RoleBinding. Le script ne contacte pas le cluster. Le rôle du déployeur autorise seulement `get/create` sur Services et Deployments ; aucune permission cluster-wide, suppression ou modification de workload existant.

Les NetworkPolicies limitent Admin aux pods gateway et Realms aux pods Admin. L'option `charactersNamespace` autorise aussi les pods Characters explicitement identifiés pour le catalogue et référence le secret `arkya-operations-secrets/realms-catalog-key`. L'egress n'est pas restreint par ces manifests : MongoDB, DNS et l'API Kubernetes doivent rester joignables. Les namespaces dédiés doivent recevoir les règles réseau adaptées à leurs flux UDP/TCP et interservices avant une utilisation publique.

Après avoir choisi le contexte, relu le fichier et provisionné les secrets dans les namespaces correspondants :

```powershell
$arkyaContext = 'CONTEXTE_K3S'
kubectl --context $arkyaContext apply --dry-run=server -f .local/operations.json
kubectl --context $arkyaContext apply -f .local/operations.json
kubectl --context $arkyaContext -n arkya-operations rollout status deployment/arkya-realms --timeout=120s
kubectl --context $arkyaContext -n arkya-operations rollout status deployment/arkya-admin --timeout=120s
```

Adapter `arkya-operations` aux valeurs choisies. Si les namespaces n'existent pas encore, les créer avant de provisionner leurs Secrets. Le premier `dry-run=server` de toute la List exige lui aussi ces namespaces : un dry-run ne les crée pas pour les requêtes suivantes.

La readiness Realms vérifie MongoDB ; sa liveness vérifie le processus. Les sondes Admin sont TCP et ne valident pas la disponibilité de Realms. Un backend indisponible apparaît comme une erreur dans l'écran des realms.

## Brancher Traefik et YARP

Dans la configuration du [renderer gateway](../gateway/README.md), renseigner :

```json
{
  "adminHost": "backoffice.arkya.online",
  "adminAddress": "http://arkya-admin.arkya-operations.svc.cluster.local:8080/"
}
```

Régénérer puis appliquer la gateway avec une image contenant ce routage. Utiliser [backoffice.dynamic.example.yaml](../gateway/traefik/backoffice.dynamic.example.yaml) dans la VM Traefik, avec le service gateway du fichier existant, son certificat et un DNS réel pour le domaine choisi. Le nom `backoffice.arkya.online` est une valeur proposée, pas un enregistrement DNS créé par le code.

La gateway conserve le Host et normalise les en-têtes de transfert. Admin n'accepte l'indication HTTPS que depuis les sources configurées. Les chemins du backoffice restent sous son propre domaine ; `api.arkya.online/admin/realms` n'expose pas le service de déploiement. Le renderer Admin désactive le module quêtes pour un démarrage autonome ; pour le conserver, activer `ContentApi:Enabled` et fournir sa destination HTTPS et sa clé administrateur existantes.

## Déployer un realm

1. Laisser `deployment.enabled=false` pendant la préparation. Ouvrir `https://backoffice.arkya.online/Realms`, se connecter et créer un brouillon depuis la cible et le profil souhaités.
2. Vérifier l'image dedicated, les Secrets de jeu, la capacité des nœuds et le contrôleur LoadBalancer. Activer `deployment.enabled=true`, régénérer/appliquer, puis redémarrer Realms pour relire sa configuration.
3. Dans la fiche du realm, demander **Déployer ce realm**. Actualiser pour observer les processus et adresses attribuées.
4. En cas d'échec, corriger la cause puis utiliser la nouvelle tentative. Ne pas supprimer une ressource étrangère pour contourner une erreur de propriété.
5. Vérifier les logs des pods, l'écoute et l'accès UDP depuis l'extérieur. La connexion d'un personnage réel nécessitera encore le protocole d'admission et l'intégration Unity.

Les Services générés utilisent `LoadBalancer` et un port par processus. [ServiceLB de k3s](https://docs.k3s.io/networking/networking-services) utilise des ports des nœuds ; le même port demandé par plusieurs realms peut manquer de nœuds disponibles. Allouer des ports distincts dans les profils ou utiliser un contrôleur/adressage adapté. L'adresse observée peut être privée : la vérifier avant toute publication aux joueurs. Un relais UDP Traefik externe reste une configuration distincte, avec ports explicitement ouverts et destinations stables.

## Vérifications locales

Pour démarrer localement avec MongoDB disponible, depuis `backend/` :

```powershell
dotnet dev-certs https --trust
$realmsKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set 'Mongo:ConnectionString' 'mongodb://<user>:<password>@localhost:27017/?authSource=admin' --project src/Arkya.Realms.Api
dotnet user-secrets set 'RealmsAdmin:Key' $realmsKey --project src/Arkya.Realms.Api
dotnet user-secrets set 'Mongo:ConnectionString' 'mongodb://<admin-session-user>:<password>@localhost:27017/?authSource=admin' --project src/Arkya.Admin
dotnet user-secrets set 'Mongo:DatabaseName' 'arkya_admin' --project src/Arkya.Admin
dotnet user-secrets set 'Admin:Password' '<mot-de-passe-operateur-16-caracteres-minimum>' --project src/Arkya.Admin
dotnet user-secrets set 'ContentApi:Enabled' 'false' --project src/Arkya.Admin
dotnet user-secrets set 'RealmsApi:Enabled' 'true' --project src/Arkya.Admin
dotnet user-secrets set 'RealmsApi:AdminKey' $realmsKey --project src/Arkya.Admin
dotnet run --project src/Arkya.Realms.Api --launch-profile https
```

Dans un autre terminal, lancer `dotnet run --project src/Arkya.Admin`, puis ouvrir `http://localhost:5183/Realms`. Realms écoute sur `https://localhost:7144`. Les valeurs restent hors du dépôt. Ajouter les cibles et profils de l'exemple dans la section `Deployment` des user-secrets de Realms pour créer un brouillon ; aucune cible n'est inventée par défaut. Laisser `Deployment:Enabled=false` pour travailler sans accès Kubernetes. Le stockage local utilise lui aussi MongoDB, sans remplacement silencieux par de la mémoire.

L'utilisateur MongoDB d'Admin doit disposer de droits de lecture, d'écriture et
de création d'index sur `arkya_admin`. Il ne doit pas recevoir les credentials
Realms pour accéder à ses données métier. Les deux bases peuvent partager le
même serveur MongoDB de développement.

Depuis la racine du dépôt :

```powershell
dotnet test backend/tests/Arkya.Realms.Tests/Arkya.Realms.Tests.csproj
dotnet test backend/tests/Arkya.Admin.Tests/Arkya.Admin.Tests.csproj
dotnet test backend/tests/Arkya.Gateway.Tests/Arkya.Gateway.Tests.csproj
./backend/deploy/realms/Test-Manifests.ps1
./backend/deploy/gateway/Test-AdminRendering.ps1
```

Les dépendances des tests HTTP sont simulées ; les tests de transport TLS utilisent de véritables connexions locales et certificats de test. Pour activer les tests MongoDB, définir `ARKYA_TEST_MONGO` avec une URI de test : ils créent des bases temporaires `arkya_realms_test_*` et `arkya_admin_test_*`, puis les suppriment. Le workflow Services images appelle Backend CI, qui fournit un MongoDB éphémère et active ces tests.

Validation du 16 octobre 2026 : 29 tests Realms, 74 tests Admin et 81 tests Gateway réussis, un test MongoDB réel ignoré localement faute de serveur. Les manifests et les rejets de configurations invalides ont été contrôlés hors ligne. Le parcours navigateur création → fiche → demande de déploiement a été vérifié avec une API simulée, sur ordinateur et mobile ; les publications .NET Release ont été vérifiées.

Les tests de génération ne remplacent pas le `dry-run=server`. Lors de cette implémentation, le moteur Docker Linux était arrêté et l'API du contexte k3s configuré ne répondait pas : aucun build de conteneur ou déploiement réel n'a été validé localement.
