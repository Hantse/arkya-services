# Réseau, backoffice et déploiement des realms

La cible reprend le schéma fourni le 16 octobre 2026 : une VM Traefik reçoit les domaines Arkya, une gateway YARP dessert les applications web, et les serveurs dédiés d'un realm appellent les services de jeu par une gateway interne. Le premier parcours implémenté est l'administration des déploiements k3s.

Pour l'installation progressive actuelle, le [profil Argo `admin`](../backend/deploy/gitops/admin/README.md)
ajoute les modules Quêtes et Realms sur `admin.arkya.online`, avec les six
applications dans `arkya-beta` et un seul LoadBalancer YARP. Il autorise
explicitement HTTP privé entre Admin et ses API ; la topologie HTTPS interne
décrite ci-dessous reste disponible dans le profil bêta complet. Le déployeur
de dedicateds n'est activé qu'à une étape ultérieure.

## Topologie et responsabilités

```mermaid
flowchart TB
    Browser["Navigateur · *.arkya.online"] -->|HTTPS| Edge["Traefik · VM"]
    Edge -->|HTTP réseau privé| Public["YARP public · k3s"]
    Public -->|backoffice.arkya.online| Admin["Backoffice ASP.NET"]
    Public -.->|Routage frontend à intégrer| Frontend["Frontend"]
    Public -->|api.arkya.online| Backend["API publiques Identity / Characters"]
    Admin -->|HTTPS + clé administrateur| Realms["Realms API · opérations persistantes"]
    Realms --> DB[("MongoDB · arkya_realms")]
    Realms -->|Identité de service, namespaces autorisés| K8s["API Kubernetes · k3s"]
    K8s -->|Déploiement| AreaA["Realm x · dedicated area A"]
    K8s -->|Déploiement| AreaB["Realm x · dedicated area B"]
    Client["Client Unity"] -->|Netcode · UDP vers endpoint attribué| UDP["Service UDP par dedicated"]
    UDP --> AreaA
    UDP --> AreaB
    Edge -.->|Relais UDP facultatif, ports explicites| UDP
    AreaA -.->|HTTPS services · à intégrer| Internal["YARP interne des services de jeu"]
    AreaB -.->|HTTPS services · à intégrer| Internal
    Internal -.-> Identity["Game Service Identity"]
    Internal -.-> Characters["Game Service Characters"]
    Internal -.-> Others["Autres services de jeu"]
```

Les pointillés signalent les intégrations restantes ou facultatives. L'UDP représenté est le transport cible ; cette tranche ne branche pas le client Unity sur ces endpoints. Le frontend existant reste publié séparément tant que son hébergement et son routage n'ont pas été raccordés.

| Élément | Rôle |
| --- | --- |
| Traefik VM | TLS des sites et API ; relais UDP éventuel configuré séparément |
| YARP public | Routage HTTP par domaine ; séparation de `api.arkya.online` et du backoffice |
| Backoffice `Arkya.Admin` | Connexion opérateur, édition des quêtes existante, création et suivi des realms |
| `Arkya.Realms.Api` | Définitions persistantes, demande de déploiement, observation des ressources Kubernetes |
| Realm | Univers logique stable, associé pour cette V1 à un namespace préautorisé |
| Dedicated | Un processus de simulation par area explicitement nommée ; une réplique par processus |
| YARP interne | Cible de routage HTTP des services de jeu ; instance et routes restant à implémenter |
| Identity / Characters | Services propriétaires des comptes et personnages ; aucune copie implicite par realm |

YARP est un proxy HTTP de couche 7 ; il ne transporte pas les datagrammes Netcode. Un relais UDP Traefik exige ses propres entryPoints et destinations. Un nom `realm-x.arkya.online` ne suffit pas à choisir une destination dans un flux UDP : les endpoints annoncés devront préciser l'adresse et le port. Voir les documentations [YARP](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/https-tls?view=aspnetcore-10.0), [Traefik UDP](https://doc.traefik.io/traefik/reference/routing-configuration/udp/routing/router/) et [Services Kubernetes](https://kubernetes.io/docs/concepts/services-networking/service/).

## Première tranche disponible

L'écran **Realms** réutilise la session opérateur, les formulaires antiforgery et le style du backoffice existant. L'opérateur choisit un identifiant stable, un nom affiché, une cible et un profil fournis par le serveur. Le profil fixe l'image par digest, les processus, leurs ports et arguments, ainsi que les références éventuelles aux Secrets déjà présents dans le namespace cible. Le formulaire ne reçoit ni kubeconfig, ni YAML, ni commande libre.

La création enregistre un brouillon dans `arkya_realms`. Le déploiement enregistre d'abord une demande durable ; un worker crée les `Deployment` et `Service` manquants, puis observe leur état. Un redémarrage de Realms reprend les demandes persistées. Chaque processus possède son propre Service UDP `LoadBalancer` ; il n'y a pas de répartition aléatoire entre areas dans un Service commun.

Les états affichés sont `Draft` (brouillon), `Pending` (en attente), `Deploying` (déploiement en cours), `Ready` (déploiement prêt), `Failed` (échec). `Ready` exige la génération observée, une réplique mise à jour et disponible par serveur, et une adresse de LoadBalancer. Cet état ne vérifie ni l'écoute UDP effective, ni l'admission de joueurs, ni les sauvegardes de jeu. Les dedicated générés n'ont pas encore de sonde applicative Unity.

### Garanties et limites

La gateway normalise le schéma effectif `wss` en `https` après vérification
du proxy approuvé : Traefik utilise ce schéma lors de l'upgrade WebSocket.
Ce comportement est explicite dans le
[middleware Traefik](https://github.com/traefik/traefik/blob/master/pkg/middlewares/forwardedheaders/forwarded_header.go),
alors qu'[ASP.NET `IsHttps`](https://github.com/dotnet/aspnetcore/blob/v10.0.10/src/Http/Http/src/Internal/DefaultHttpRequest.cs)
compare uniquement le schéma `https`.
La garde HTTPS reste active ; `ws`, HTTP direct et les en-têtes d'un proxy
non approuvé sont refusés. Le test utilise le vrai pipeline de production,
Kestrel et un échange WebSocket binaire ; l'admission du dedicated reste
une vérification distincte.

- Une cible et son namespace ne peuvent appartenir qu'à un realm ; MongoDB impose l'unicité. Le profil est copié dans la définition pour éviter qu'un changement de configuration modifie un realm existant.
- La demande porte `expectedRevision`. Un rejeu avec la même révision retrouve la demande déjà enregistrée ; une autre modification concurrente est refusée. Le worker utilise une version interne distincte pour ses observations.
- Les noms de ressources sont déterministes et portent une identité de déploiement persistante. Une ressource étrangère ou une configuration modifiée manuellement provoque une erreur ; le service ne l'écrase pas.
- Le worker peut créer une ressource absente, y compris après sa suppression externe. Cela relance un processus ; aucune reprise d'état de simulation n'est promise. Désactiver `Deployment:Enabled` avant une intervention de maintenance externe qui doit conserver un serveur arrêté.
- Le worker observe aussi les realms prêts. Une perte de disponibilité quitte l'état prêt. Une erreur définitive ou un délai dépassé demande une nouvelle tentative depuis le backoffice après correction de sa cause.
- Les namespaces et droits sont provisionnés par l'exploitation. Le compte de service a `get/create/patch` sur Deployments et `get/create` sur Services dans ces namespaces, `get` sur les seuls Secrets `arkya-ghcr` et `arkya-dedicated`, et `create` sur les Secrets. `patch` est utilisé uniquement pour l'image d'un Deployment possédé, sur intention GitOps explicite décrite ci-dessous. Kubernetes ne limite pas `create` par nom ; Realms impose les noms fixes, la cible autorisée et l'identité du realm avant tout appel. Aucun droit de liste, modification ou suppression de Secret ni accès aux autres namespaces n'est accordé. Le Secret de registre source `arkya-beta/arkya-ghcr` doit être provisionné avant le rollout Realms ; voir le [contrat YAML du Secret](REALM_RUNTIME.md#provisionner-le-registre-source).
- Il n'y a pas encore de suppression, d'arrêt, de mise à jour d'image ni d'autoscaling depuis le backoffice. Ces actions devront intégrer le drain, les sauvegardes et l'autorité des joueurs connectés.

Les comptes restent globaux. Le rattachement `realmId` des personnages, les tickets d'admission et le catalogue public de sélection restent à implémenter suivant la [décision 0003](decisions/0003-realms-and-area-authority.md). Le nouveau catalogue est réservé aux opérateurs.

### Mettre à jour une image par GitOps

Changer seulement `Deployment:Profiles[*]:Image` conserve volontairement l'image
figée des realms existants. Une release d'image doit ajouter une intention
`Deployment:ImageReleases`, vide par défaut. Pour `beta-web`, le ConfigMap
Realms du profil Argo `admin` porte les clés suivantes ; remplacer les deux
digests par ceux vérifiés de la release précédente et de la nouvelle CI :

```yaml
Deployment__Profiles__0__Image: ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:<nouveau-digest>
Deployment__ImageReleases__0__RealmId: beta-web
Deployment__ImageReleases__0__ProfileId: beta-web
Deployment__ImageReleases__0__Version: '1'
Deployment__ImageReleases__0__PreviousImage: ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:<digest-actuel>
```

La version est un entier positif strictement croissant par realm. Le nouveau
digest vient exclusivement du profil autorisé, et la cible doit être liée
explicitement au même `RealmId`. La topologie, les arguments, le protocole,
les identifiants, les références de secrets et le namespace restent identiques.
Une intention qui ne correspond pas au profil figé, à l'image précédente ou
à une version déjà enregistrée est ignorée sans écriture ni déploiement.

Publier ensemble les images client/dedicated issues du même commit CI, puis
committer les pins et cette intention dans le YAML suivi par Argo CD. Le
changement de ConfigMap généré provoque le redémarrage de Realms. Aucun
`kubectl` ni changement direct de MongoDB n'est nécessaire. Mettre également
à jour Characters lorsque le catalogue d'apparence est étendu : l'ancien
dedicated valide ce catalogue et refuse les nouvelles options inconnues.

Le worker attend un realm `Ready` (ou un ancien realm `Failed` sans release
précédente), puis persiste par comparaison
de version l'image et son intention avant de toucher Kubernetes. Cette
écriture retire le realm du catalogue prêt pour interrompre les nouvelles
admissions pendant la transition. Un rejeu conserve la même révision ; un
processus obsolète ne peut remplacer une définition plus récente.

Le patch Kubernetes vérifie le nom déterministe, le namespace, les labels de
propriété et la configuration attendue complète. Seuls l'ancien digest
annoncé et l'annotation `arkya.online/image-release` peuvent changer. Le
`resourceVersion` est testé atomiquement avec l'ancienne image ; l'annotation
de version doit encore correspondre à la transition précédente. Ce verrou
empêche un ancien worker de rejouer une release après un retour volontaire à
un digest historique. Toute autre dérive est refusée, sans modification des
Services ni des Secrets existants.

La stratégie demeure `Recreate`, une seule réplique et 30 secondes de grâce :
le dedicated reçoit `SIGTERM` pour sauvegarder et libérer ses leases avant
le lancement de son remplaçant. Une interruption des connexions est donc
attendue. Après le patch, la readiness précédente est écartée ; la nouvelle
génération doit être observée avec une réplique disponible et l'image
attendue. Le digest prêt est conservé dans `ServerObservation.ReadyImage`.
L'intention persistée reprend après redémarrage ; une release en échec est
revérifiée avec la même intention, sans changer de révision à chaque essai.

Conserver l'intention courante dans Git après réussite. Une configuration
retirée, contradictoire ou de version inférieure ne gère plus un realm déjà
promu et ne le rétrograde pas. Pour revenir à un digest historique, publier
une nouvelle version supérieure avec l'image actuelle comme `PreviousImage`.
Attendre la fin de la release courante avant d'en publier une nouvelle.
Une transition précédente encore en échec ne peut être remplacée par une
version supérieure : restaurer d'abord son intention et terminer sa reprise.
Les validations unitaires couvrent les contrats et le faux cluster ; une
admission dans le vrai client et un arrêt avec lease active restent les
vérifications de déploiement à effectuer sur chaque nouvelle paire.

## Contrats d'administration

Toutes les routes `/admin/realms` sont internes et exigent `X-Arkya-Admin-Key`, fourni par le serveur Admin. Les services refusent HTTP en production ; le backend Realms utilise TLS interne. La clé ne rejoint jamais le navigateur. Les secrets Kubernetes sont des références de configuration, pas des données retournées par l'API.

| Méthode et route | Entrée / résultat |
| --- | --- |
| `GET /admin/realms/options` | Cibles et profils sélectionnables, `deploymentEnabled` |
| `GET /admin/realms` | Liste des définitions et états observés |
| `GET /admin/realms/{realmId}` | Détail et endpoints observés par serveur |
| `POST /admin/realms` | `{realmId, displayName, targetId, profileId}` ; `201` si créé |
| `POST /admin/realms/{realmId}/deploy` | `{expectedRevision}` ; `202` si demande acceptée ou retrouvée |

Les identifiants de realm, profil, cible et serveur ont 3 à 31 caractères : lettre minuscule initiale, chiffres/minuscules/tirets, terminaison alphanumérique. Les noms affichés ont 1 à 80 caractères. Une cible ou un realm déjà réservé renvoie `409`, un choix invalide `400`. Les définitions exposent une révision, l'image figée, l'état, une erreur bornée et la date du dernier changement observé.

Le mot de passe opérateur unique est conservé pour cette tranche, comme pour les quêtes. Les comptes d'administration individuels, rôles fins et journal d'audit nominatif restent à ajouter avant d'élargir les accès.

## Exploitation et vérification

Le [guide k3s](../backend/deploy/realms/README.md) décrit les images, secrets, certificats, profils, permissions et la mise en service du backoffice. Le déploiement est désactivé par défaut. Les tests .NET exercent les contrats, autorisations, conflits, reprise, observation Kubernetes et l'interface via des dépendances simulées ; le script PowerShell contrôle les manifests hors ligne.

La validation finale d'un realm réel devra démarrer une image Linux dedicated compatible, confirmer ses endpoints UDP depuis l'extérieur et faire entrer un client avec le futur protocole d'admission. Les builds Windows du prototype local ne constituent pas cette image. Le déploiement du backoffice ne prouve pas cette intégration de jeu.
