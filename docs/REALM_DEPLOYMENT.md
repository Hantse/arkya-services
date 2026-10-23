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
    K8s -->|Déploiement| AreaA["Realm x · Area de départ · partition A"]
    K8s -->|Déploiement| AreaB["Realm x · Area de départ · partition B"]
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
| Area | Une carte entière du realm, par exemple l'île de départ ; son découpage est configurable |
| Dedicated | Un processus de simulation par partition d'Area ; une réplique par processus |
| YARP interne | Cible de routage HTTP des services de jeu ; instance et routes restant à implémenter |
| Identity / Characters | Services propriétaires des comptes et personnages ; aucune copie implicite par realm |

YARP est un proxy HTTP de couche 7 ; il ne transporte pas les datagrammes Netcode. Un relais UDP Traefik exige ses propres entryPoints et destinations. Un nom `realm-x.arkya.online` ne suffit pas à choisir une destination dans un flux UDP : les endpoints annoncés devront préciser l'adresse et le port. Voir les documentations [YARP](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/https-tls?view=aspnetcore-10.0), [Traefik UDP](https://doc.traefik.io/traefik/reference/routing-configuration/udp/routing/router/) et [Services Kubernetes](https://kubernetes.io/docs/concepts/services-networking/service/).

## Première tranche disponible

La liste **Realms** ouvre la création sur une page dédiée `/CreateRealm`. Cette page réutilise la session opérateur, les formulaires antiforgery et le style du backoffice existant. L'opérateur choisit un identifiant stable, un nom affiché, une cible et un profil fournis par le serveur. Il peut définir plusieurs Areas dans le realm, leur découpage entre dedicateds et les capacités prévues pour les instances et les raids. Le profil fixe l'image par digest, les ports et arguments autorisés, ainsi que les références éventuelles aux Secrets déjà présents dans le namespace cible. Le formulaire ne reçoit ni kubeconfig, ni YAML, ni commande libre.

La création enregistre un brouillon dans `arkya_realms`. Le déploiement enregistre d'abord une demande durable ; un worker crée les `Deployment` et `Service` manquants, puis observe leur état. Un redémarrage de Realms reprend les demandes persistées. Chaque processus possède son propre Service UDP `LoadBalancer` ; il n'y a pas de répartition aléatoire entre partitions dans un Service commun.

Les états affichés sont `Draft` (brouillon), `Pending` (en attente), `Deploying` (déploiement en cours), `Ready` (déploiement prêt), `Failed` (échec). `Ready` exige la génération observée, une réplique mise à jour et disponible par serveur, et une adresse de LoadBalancer. Cet état ne vérifie ni l'écoute UDP effective, ni l'admission de joueurs, ni les sauvegardes de jeu. Les dedicated générés n'ont pas encore de sonde applicative Unity.

### Areas, partitions, instances et raids

Un realm contient plusieurs **Areas**. Une Area est une carte entière :
`starter-island` représente l'Area de l'île de départ. Chaque Area est
découpée en partitions, avec un dedicated par partition. Le realm regroupe
ces processus et les capacités prévues pour ses instances et raids.
À la création, chaque Area possède un identifiant historique `islandId`
stable, un `dedicatedCount`, ses dimensions `width`/`depth` et son origine
`originX`/`originZ` dans le plan horizontal X/Z Unity. Une grille explicite
`gridColumns` × `gridRows` peut fixer les colonnes X et les lignes Z ; le Y
de la carte affichée représente cet axe Z du monde, pas l'altitude Unity.
La liste accepte les Areas futures : aucune règle ne la limite à StarterIsland.
Le build Unity doit disposer du contenu de chaque Area ; cette configuration
ne crée pas de terrain. Deux Areas peuvent utiliser les mêmes coordonnées
locales et des nombres de partitions différents ; leur identifiant les isole.

Le vocabulaire métier est `Realm → Areas → partitions → dedicateds`.
Pour conserver les plans, empreintes et clients existants, le JSON garde
`topology.islands` pour la liste d'Areas, `islandId` pour leur identifiant,
`kind = Island` et les IDs de serveur `island-*`. Les variables
`ARKYA_ISLAND_ID`, `ARKYA_AREA_BOUNDS` et `ARKYA_AREA_NEIGHBORS` restent
également historiques : les deux dernières décrivent les partitions et leurs
voisins. Cette clarification ne migre aucun identifiant ni snapshot.

L'Area de départ de 1 000 × 1 000 à l'origine (0, 0), répartie sur quatre
dedicateds, forme quatre partitions d'autorité de 500 × 500 :

| Partition | Dedicated | X min → max | Z min → max |
| --- | --- | --- | --- |
| A | `island-starter-island-01` | 0 → 500 | 0 → 500 |
| B | `island-starter-island-02` | 500 → 1 000 | 0 → 500 |
| C | `island-starter-island-03` | 0 → 500 | 500 → 1 000 |
| D | `island-starter-island-04` | 500 → 1 000 | 500 → 1 000 |

Chaque partition possède son processus et son Service, avec une seule réplique
Kubernetes. Quatre replicas derrière un Service partagé ne fournissent pas
ce découpage. Les bornes intérieures sont semi-ouvertes : X = 500 appartient
à la colonne de droite, Z = 500 à la ligne suivante (C/D), et (500, 500) à D.
Les bords extérieurs maximaux de l'Area restent inclus. Une position hors de
l'Area n'a pas de propriétaire dans ce plan.

Lorsqu'une grille explicite est fournie, le découpage utilise exactement
ses colonnes et ses lignes, même si une autre orientation produirait des
cellules plus proches du carré. Les deux champs sont obligatoires ensemble,
de 1 à 64 chacun, et leur produit doit être égal à `dedicatedCount`, au plus 64.
Les cellules ont toutes la même largeur et profondeur.

Sans ces deux champs (absents ou `null`), le découpage historique choisit
une grille rectangulaire dont colonnes × lignes égale
exactement `dedicatedCount`, en recherchant les cellules les plus proches du
carré compte tenu des dimensions de l'Area. À égalité, il privilégie davantage
de colonnes X. Une Area carrée donne donc 2 × 2 avec quatre dedicateds,
3 × 2 avec six, et 5 × 1 avec cinq. Les indices progressent d'abord en X,
puis en Z. Chaque dedicated possède une cellule ; il n'y a ni trou ni
superposition d'autorités aux frontières.

Le profil autorisé sert de modèle. La création développe et fige les
processus, leur type (`kind = Island`, `Instance` ou `Raid`), l'`islandId`,
l'indice `partitionIndex` à partir de zéro, `partitionCount` et les `bounds`
(`minX`, `minZ`, `maxX`, `maxZ`) des partitions. Les autres Areas peuvent avoir
des dimensions et une origine différentes. Les documents MongoDB portant
ce plan utilisent le schéma 2 ; les définitions historiques de schéma 1
restent lisibles. Les anciens plans de schéma 2 sans grille explicite
conservent leur partitionnement automatique. Les champs de grille `null`
sont omis du JSON transmis au dedicated afin de conserver exactement les
manifests existants. Les nouvelles valeurs sont figées dans le snapshot,
persistées dans MongoDB et conservées lors d'une mise à jour d'image.

Pour les seuls plans explicites, les manifests injectent
`ARKYA_SERVER_KIND` et `ARKYA_REALM_TOPOLOGY` (JSON camelCase). Les partitions
reçoivent aussi `ARKYA_ISLAND_ID`, `ARKYA_PARTITION_INDEX`,
`ARKYA_PARTITION_COUNT`, `ARKYA_AREA_BOUNDS` (JSON camelCase) et
`ARKYA_AREA_NEIGHBORS` (liste JSON des identifiants voisins partageant un
côté de la même Area). Le runtime de transfert lit le catalogue authentifié de Characters, qui
reprend le plan Realms figé ; ces variables décrivent aussi le plan injecté
au processus. Le profil historique version 1 conserve son comportement.
Le [guide de handoff StarterIsland](STARTER_ISLAND_HANDOFF.md) précise le
protocole et les contrôles du nouveau runtime.

Les compteurs `instanceDedicatedCount` et `raidDedicatedCount` prévoient des
emplacements distincts, identifiés par `instance-01`, `raid-01`, etc. Ils
représentent une capacité configurée. L'allocation d'une partie privée à un
groupe, ses droits d'entrée, son état, sa durée de vie et le retour au monde
ouvert restent à implémenter. Ces nombres ne comptent pas des parties
actives et ne garantissent pas des instances jouables.

Une topologie omise conserve exactement les serveurs du profil historique.
Une topologie explicite peut être enregistrée comme brouillon avec ce même
profil, pour préparer le realm. Son déploiement est refusé avec
`topology_runtime_unsupported` tant que le profil ne déclare pas explicitement
un runtime compatible. Cette garde existe à la demande de déploiement et
dans le déployeur, indépendamment du formulaire.

Pour déclarer cette capacité, l'exploitation configure
`Deployment:Profiles[*]:TopologyTemplateServerId` avec l'identifiant d'un
serveur modèle existant. Pour WebSocket, elle fournit aussi
`TopologyPublicEndpointTemplate`, contenant `{serverId}` et éventuellement
`{realmId}`, pour annoncer une destination distincte à chaque processus.
Les routes publiques et leurs destinations doivent être provisionnées
séparément. Chaque endpoint développé doit respecter les limites du protocole,
dont 125 octets pour le chemin WebSocket, et rester distinct après
normalisation URI. Le runtime actuel exige aussi `protocolVersion = 2` et
`buildId = arkya-partition-handoff-v1` sur le serveur modèle. Cette paire ne
remplace pas les pins historiques version 1. Le runtime v2 prend en charge
les autorités d'île ; tout compteur d'instances ou de raids non nul laisse
le plan enregistrable mais refuse son déploiement. L'allocateur et les modes
privés restent à implémenter.

Le plan conserve `topologyRuntimeSupported` avec son profil figé. Une
modification de la configuration du profil ne transforme pas automatiquement
les définitions existantes. Tant que le realm est `Draft`, l'opérateur peut
explicitement réenregistrer son plan et choisir un autre profil, avec la
révision attendue. Cela permet de préparer un plan avec le profil bêta, puis
de le rattacher à un runtime compatible. Les états `Pending`, `Deploying`,
`Ready` et `Failed` refusent cette édition : une topologie déjà engagée ne peut
pas être redécoupée sans futur workflow de drain et de migration.

Le build historique `1 / arkya-web-beta-v1` charge une île dédiée sans
handoff ; son profil ne peut pas activer la topologie distribuée. Le runtime
`2 / arkya-partition-handoff-v1` ajoute le passage du joueur entre domaines
StarterIsland. Le catalogue expose les partitions d'Area des plans compatibles,
avec une version de topologie ; Characters choisit la partition de la position
sauvegardée et refuse une admission dont la paire client ne correspond pas.
Les pools d'instances et de raids ne sont pas publiés comme destinations
jouables. Le contenu des autres Areas reste à fournir dans un runtime adapté ;
la liste du backoffice conserve leur plan sans créer ces contenus. Le handoff
reste à l'intérieur d'une même Area ; aucune proximité de coordonnées ne
déclenche un passage vers une autre carte.

### Passage automatique du joueur

La préparation et le claim préparent la connexion cible sans créer de seconde
autorité. La source scelle son état final avant le commit ; Characters remplace
atomiquement la lease et son époque dans MongoDB. Les écritures de l'ancien
propriétaire sont alors refusées. Le client conserve l'identité du personnage
pendant le changement de monde Netcode. Expiration et annulation ont un état
durable et une annulation ne permet de reprendre que si elle a effectivement
gagné la comparaison de version.

Le [guide du handoff](STARTER_ISLAND_HANDOFF.md) décrit les API, l'enveloppe de
simulation active, la configuration des clés et le test à quatre dedicateds.
Les tests backend avec MongoDB, le préflight HTTP et un run réel avec quatre
exécutables dedicated et un client Unity Editor rendu passent. Ce run compte
six handoffs, avec un blocage maximal mesuré de 0,43 seconde près d'une
frontière ; le client natif exporté et le déploiement public restent à valider.
La réplication
frontalière des PNJ, le combat entre domaines et les parties privées ne font
pas partie de cette tranche. Le [prototype de partitions](AREA_PROTOTYPE.md)
conserve ses tests locaux de combat et ne constitue pas cette preuve.

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
- Le backoffice permet maintenant le redémarrage, la mise à jour vers une release approuvée et des commandes en jeu ; voir [Exploitation des realms](REALM_OPERATIONS.md). La suppression est décrite dans ce même guide ; l’autoscaling reste séparé.

Les comptes restent globaux. Le rattachement `realmId`, les tickets et les
leases sont conservés lors du handoff. Le parcours historique reste disponible
pour sa paire version 1 ; le nouveau catalogue version 2 route les partitions
d'Area compatibles. Les autres contenus d'Area, le placement des groupes et
l'allocation d'instances/raids gardent leurs travaux propres.

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
digest vient exclusivement du profil autorisé. Le `RealmId` de la release
doit correspondre au realm ; sa cible et son namespace restent ceux du snapshot
enregistré. Une cible sans réservation de `RealmId` est admise, tandis qu’une
cible explicitement épinglée à un autre realm est refusée. La topologie, les arguments, le protocole,
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
| `GET /admin/realms/options` | Cibles et profils sélectionnables, `topologySupported` par profil, `deploymentEnabled` |
| `GET /admin/realms` | Liste des définitions et états observés |
| `GET /admin/realms/{realmId}` | Détail et endpoints observés par serveur |
| `POST /admin/realms` | `{realmId, displayName, targetId, profileId, topology?}` ; `201` si créé |
| `PUT /admin/realms/{realmId}/topology` | `{expectedRevision, profileId, topology}` ; `200` après édition d'un brouillon |
| `POST /admin/realms/{realmId}/deploy` | `{expectedRevision}` ; `202` si demande acceptée ou retrouvée |

Les identifiants de realm, profil, cible et serveur ont 3 à 31 caractères : lettre minuscule initiale, chiffres/minuscules/tirets, terminaison alphanumérique. Les noms affichés ont 1 à 80 caractères. Une cible ou un realm déjà réservé renvoie `409`, un choix invalide `400`. Les définitions exposent une révision, l'image figée, l'état, une erreur bornée et la date du dernier changement observé.

Exemple de plan : quatre dedicateds pour l'Area de départ, deux pour une autre
Area, deux emplacements d'instances et un de raid. Les clés JSON historiques
`islands` et `islandId` représentent ces Areas :

```json
{
  "realmId": "eu-one",
  "displayName": "Europe one",
  "targetId": "realm-eu-one",
  "profileId": "island-v1",
  "topology": {
    "islands": [
      {
        "islandId": "starter-island",
        "dedicatedCount": 4,
        "gridColumns": 2,
        "gridRows": 2,
        "width": 1000,
        "depth": 1000,
        "originX": 0,
        "originZ": 0
      },
      {
        "islandId": "second-island",
        "dedicatedCount": 2,
        "gridColumns": 2,
        "gridRows": 1,
        "width": 2000,
        "depth": 1000,
        "originX": -1000,
        "originZ": 0
      }
    ],
    "instanceDedicatedCount": 2,
    "raidDedicatedCount": 1
  }
}
```

Une topologie accepte au plus 32 Areas, avec des identifiants uniques de 3 à
20 caractères au même format minuscule. Chaque Area demande 1 à 64 dedicateds ;
chaque capacité instance/raid accepte 0 à 64 dedicateds. La somme doit être
comprise entre 1 et 256 processus. `gridColumns` et `gridRows` sont optionnels
ensemble ; s'ils sont renseignés, chaque valeur est un entier de 1 à 64 et
leur produit doit correspondre à `dedicatedCount`. Une paire incomplète,
une dimension invalide ou un produit différent renvoie `400 invalid_topology`.
Les indices commencent à zéro et suivent `ligne * gridColumns + colonne`,
depuis l'origine, en X croissant puis en Z croissant.
`width` et `depth` sont finis, de 1 à 1 000 000 ; les origines sont finies,
avec une valeur absolue au plus égale à 10 000 000. Omettre les dimensions
conserve les valeurs par défaut de
1 000 × 1 000 à l'origine (0, 0). `topology = null` ou le champ absent conserve
le contrat historique. Le détail expose `topology`,
`topologyRuntimeSupported` et les métadonnées spatiales des serveurs prévus.
Une création réussie reste un brouillon : elle ne valide ni le contenu du
build, ni ses routes, ni le passage d'un joueur entre dedicateds.

L'édition d'un brouillon réutilise exactement l'objet `topology` ci-dessus,
avec `expectedRevision` et le `profileId` choisi. Elle recalcule le snapshot
complet et incrémente la révision par comparaison atomique. Une révision
périmée renvoie `409` ; un realm hors état `Draft` renvoie `409` avec
`topology_edit_unavailable`. Dans cette requête, `topology` doit être présent ;
`null` revient explicitement aux serveurs du profil historique. L'édition ne
change ni le realm, ni sa cible, ni le rattachement des personnages.

Le mot de passe opérateur unique est conservé pour cette tranche, comme pour les quêtes. Les comptes d'administration individuels, rôles fins et journal d'audit nominatif restent à ajouter avant d'élargir les accès.

## Exploitation et vérification

Le [guide k3s](../backend/deploy/realms/README.md) décrit les images, secrets, certificats, profils, permissions et la mise en service du backoffice. Le déploiement est désactivé par défaut. Les tests .NET exercent les contrats, autorisations, conflits, reprise, observation Kubernetes et l'interface via des dépendances simulées ; le script PowerShell contrôle les manifests hors ligne.

La validation finale d'un realm réel doit démarrer une image Linux dedicated
compatible, confirmer ses endpoints depuis l'extérieur et faire entrer un
client avec le protocole d'admission adapté. Les builds Windows du prototype
local ne constituent pas cette image. Le déploiement du backoffice ne prouve
pas cette intégration de jeu.

Pour une île répartie sur plusieurs dedicateds, la validation doit en plus
faire traverser un vrai client dans les deux sens, vérifier une seule autorité
par entité, la continuité des interactions frontalières, le refus des
écritures de l'ancien propriétaire et la persistance après transfert. Les
retries, timeouts, paquets dupliqués, arrêt d'un voisin et reprise après crash
doivent être exercés sur les processus distribués. Les instances et raids
nécessiteront leurs propres tests d'allocation de groupe, d'accès et de retour.
