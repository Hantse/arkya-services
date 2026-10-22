# Exploiter un realm depuis l’admin

La fiche `/Realms/{realmId}` regroupe le redémarrage, la mise à jour vers une
image approuvée, les annonces aux joueurs et les commandes `kick` et `save`.
Les formulaires héritent de la session administrateur et de l’antiforgery.
L’opérateur vient de la session, jamais d’un champ modifiable du navigateur.
Chaque action demande un motif et conserve son résultat dans un historique.

## Redémarrage et mise à jour

Un redémarrage relance les dedicateds du realm avec leur image actuelle.
Une mise à jour utilise exclusivement le digest approuvé dans la configuration
Realms. Les deux actions demandent de recopier l’identifiant du realm et
vérifient sa révision. Elles interrompent les connexions : annoncer
l’intervention et vérifier la sauvegarde avant de les lancer.

Le déploiement conserve une seule réplique, la stratégie `Recreate` et le délai
de grâce existant pour la sauvegarde/libération des leases à l’arrêt. Il ne
s’agit pas d’une migration sans interruption ni d’une garantie de sauvegarde
en cas de panne. L’état `Ready` attend la génération Kubernetes et, pour un
redémarrage, la révision de redémarrage attendue. Les patches sont limités à
l’image approuvée et à l’annotation de redémarrage, avec préconditions de
propriété, configuration et `resourceVersion`.

Pour rendre une nouvelle release déclenchable par le bouton **Installer la
version approuvée**, préparer le digest du profil et l’intention décrite dans
[le guide de déploiement](REALM_DEPLOYMENT.md#mettre-à-jour-une-image-par-gitops),
puis ajouter :

```yaml
Deployment__ImageReleases__0__Automatic: 'false'
```

`Automatic` vaut `true` par défaut pour conserver le fonctionnement GitOps
existant. Le bouton ne construit pas une image et ne choisit pas un tag mobile.
Le digest, le realm, le profil, la version croissante et l’image précédente
doivent être approuvés ensemble. Le formulaire transmet aussi le digest affiché
pour refuser une validation portant sur une ancienne release.

Une demande persistée est reprise après redémarrage de Realms. Le rejeu du
même formulaire ne relance pas une seconde fois l’opération. Les 100 dernières
opérations explicites conservent acteur, motif, révision, images et résultat.
L’arrêt permanent, la suppression et l’autoscaling ne font pas partie de ces
actions.

## Configuration en jeu par royaume

La fiche expose les multiplicateurs **XP** et **quantité récoltée**. Ils valent
`1` par défaut, acceptent de `0` à `100` avec trois décimales au maximum ; `2`
signifie deux fois la valeur normale et `0` aucun gain. Le taux de récolte
multiplie la quantité finale après les bonus d’outils et de pierres, avec un
arrondi à l’entier inférieur ; il ne modifie pas la vitesse de récolte.

L’enregistrement exige un motif et la révision de configuration affichée.
Realms conserve les taux et les 100 dernières modifications avec l’opérateur,
les anciennes et nouvelles valeurs et leur date. La configuration possède sa
propre révision : la changer ne redéploie pas le serveur, ne change pas la
révision de déploiement et n’invalide pas les commandes déjà en attente.
Un formulaire obsolète est refusé ; le rejeu identique est sans double écriture
dans la fenêtre d’historique conservée.

Tous les dedicateds du royaume reçoivent le même instantané dans leurs lectures
du canal d’administration. Chaque processus vérifie toutes les valeurs, applique
les deux taux ensemble côté serveur et annonce la révision appliquée à sa
lecture suivante. Il refuse les révisions anciennes et les instantanés
invalides. Les lectures continuent pendant l’exécution d’une commande, y
compris durant une sauvegarde ou une reprise de son accusé de résultat.

L’admin distingue **enregistré**, **en attente**, **appliqué**, **hors ligne**
et **serveur incompatible** selon les accusés reçus. Une sauvegarde dans
Realms ne prouve pas que tous les processus ont déjà adopté la valeur ; un
processus arrêté récupère la dernière configuration à son prochain démarrage.
Un serveur ancien peut continuer ses commandes compatibles sans prétendre
prendre en charge les taux en direct.

Un dedicated qui attend son premier instantané valide ne distribue pas de
récompenses de récolte avec des valeurs par défaut provisoires. Les mondes
locaux sans canal de configuration conservent leur fonctionnement à `×1`.

La portée actuelle est explicite : **le gain d’XP n’est pas encore implémenté**.
Son taux est transmis et disponible dans la configuration autoritaire pour le
futur système de progression. La quantité récoltée est appliquée dans le
système de récolte existant, qui reste indépendant de l’île bêta et ne dispose
pas encore d’un parcours de récolte jouable sur cette île. L’accusé « appliqué »
confirme la configuration du processus ; il n’ajoute pas ces boucles de jeu.

## Commandes en jeu

| Action | Cible et résultat |
| --- | --- |
| Annonce | Texte affiché dans le chat du HUD des joueurs admis sur le dedicated sélectionné ; 400 octets UTF-8 maximum, une ligne. Le résultat compte les destinataires, sans accusé de lecture du joueur. |
| Déconnecter (`kick`) | UUID du personnage connecté à ce dedicated ; déclenche la déconnexion et le parcours existant de sauvegarde finale/libération du lease. Le résultat confirme la demande de déconnexion, pas la fin de la sauvegarde. |
| Sauvegarder (`save`) | Sauvegarde les positions/orientations des joueurs actifs via les leases et révisions possédés par le serveur. La réussite attend les réponses de persistance. Aucune économie, progression ou donnée client ne devient autoritaire. |

Le dedicated contacte Realms et annonce ses capacités. La fiche active les
commandes seulement pour les serveurs ayant répondu depuis moins de 30 secondes
et dont le realm est prêt. Un ancien dedicated sans cette intégration laisse
les boutons désactivés. Le choix du serveur est explicite, y compris pour les
realms qui possèdent plusieurs processus.

Les commandes expirent après deux minutes. Une commande encore en attente est
également invalidée si la révision du realm change (redémarrage ou mise à jour).
Au plus 20 commandes sont actives
par dedicated. Une livraison est revendiquée atomiquement une seule fois ;
une perte de réponse HTTP ne provoque pas une nouvelle exécution. Le dedicated
rejette les livraisons expirées et déduplique les identifiants déjà traités.
Les résultats peuvent être renvoyés sans répéter la commande tant que son entrée est conservée.

| État | Signification |
| --- | --- |
| `Pending` | Enregistrée, en attente de livraison. |
| `Dispatched` | Livrée au canal serveur, résultat attendu. |
| `Succeeded` / `Failed` | Résultat confirmé par le dedicated. |
| `Expired` | Délai dépassé avant livraison ; ne sera pas exécutée. |
| `Unknown` | Livraison revendiquée mais résultat non confirmé ; vérifier en jeu avant une nouvelle demande. Un résultat tardif du même processus peut encore clôturer l’entrée. |

MongoDB conserve au plus 100 entrées par serveur dans `realm_commands` ;
l’interface affiche les 50 dernières du realm. Il s’agit d’un historique
d’exploitation borné, pas d’un journal d’audit permanent. L’envoi admin possède
un UUID d’idempotence dans la fenêtre d’historique conservée ; le réutiliser avec un contenu différent est refusé.
Les commandes autorisées sont structurées : aucune console système, expression
C#, commande Kubernetes ou shell n’est exécutée.

## Transport et configuration

Les routes restent privées, absentes de la gateway publique :

| Route | Contrat |
| --- | --- |
| `GET /admin/realms/{id}/operations-options` | Actions serveur disponibles et image approuvée. |
| `POST /admin/realms/{id}/restart` | `{expectedRevision, reason}` |
| `POST /admin/realms/{id}/update` | `{expectedRevision, expectedImage, reason}` |
| `GET /admin/realms/{id}/configuration` | Taux, révision de configuration et historique des changements. |
| `PUT /admin/realms/{id}/configuration` | `{expectedRevision, xpMultiplier, harvestMultiplier, reason}` ; nombres JSON, révision de configuration indépendante. |
| `GET /admin/realms/{id}/commands` | Capacités, dernier contact et historique. |
| `POST /admin/realms/{id}/commands` | `{requestId, expectedRevision, serverId, type, message, characterId, reason}` ; champs inutilisés à chaîne vide. |
| `POST /internal/realms/{id}/servers/{serverId}/commands/poll` | `{instanceId, protocolVersion:1, supportedCommands:[...], configurationVersion:1, appliedConfigurationRevision:-1, acceptCommands:true}` ; au plus une commande et `runtimeConfiguration:{revision,xpMultiplier,harvestMultiplier}`. |
| `POST /internal/realms/{id}/servers/{serverId}/commands/{commandId}/complete` | `{instanceId, success, resultCode, affectedPlayers}` |

Les mutations admin exigent `X-Arkya-Admin-Key` et `X-Arkya-Admin-Actor`.
Le dedicated utilise `X-Arkya-Dedicated-Key`, dérivé par
`RealmRuntime:DedicatedKeySeed` pour son couple realm/serveur. Cette clé
ne permet pas d’accéder aux routes admin ni aux commandes d’un autre serveur.
Le résultat doit appartenir au processus auquel la commande a été livrée.

Les champs de configuration du polling sont facultatifs pour les anciens
clients. `configurationVersion:0` ou son absence signifie que ce serveur ne
sait pas appliquer les taux en direct. Avec la version `1`, la révision
appliquée vaut `-1` tant qu’aucun instantané valide n’a été reçu ; la révision
initiale `0` représente les taux par défaut. Le backend refuse un accusé portant
sur une révision future. `acceptCommands:false` permet de poursuivre les mises
à jour de configuration sans revendiquer une nouvelle commande lorsque le
serveur en traite déjà une.

`ARKYA_REALMS_API_URL` configure l’origine Realms du dedicated. Par défaut,
elle est déduite uniquement pour l’installation bêta connue :
`ARKYA_ALLOW_CLUSTER_HTTP=1` avec Characters sur
`http://arkya-characters.arkya-beta.svc.cluster.local:8080` sélectionne
`http://arkya-realms.arkya-beta.svc.cluster.local:8080`.
Dans les autres installations, l’absence de cette variable désactive ce
canal ; configurer une origine HTTPS. Les redirections HTTP sont refusées.
Le mode local de test accepte explicitement le loopback HTTP.

Le composant GitOps `realm-control` autorise uniquement les pods identifiés
`beta-web/island` du namespace `arkya-beta-realm` à contacter Realms sur 8080.
Realms contrôle également le pair réseau privé et la clé dédiée. Cette règle
n’ouvre aucun listener supplémentaire dans le dedicated et ne change pas son
manifeste Kubernetes figé.

## Livraison et vérification

Déployer les nouvelles images Admin/Realms et la règle réseau, puis construire
et publier une paire **client Unity + dedicated** compatible. L’annonce ajoute
une RPC Netcode : un ancien client et un nouveau serveur ne constituent pas
une paire compatible. Publier les deux builds ensemble, avec leurs digests
validés. Les actions restent indisponibles sur l’ancien serveur jusqu’à son
remplacement. Aucun appel d’exploitation sur le realm public n’est nécessaire
pour lancer les tests locaux.

Les tests Realms couvrent HTTP/authentification, idempotence, livraison unique,
expiration, capacités, bornes, persistance BSON et conflits MongoDB lorsqu’un
`ARKYA_TEST_MONGO` est fourni. Les tests Admin couvrent les formulaires et
l’antiforgery. Les tests Unity ciblent les règles et le parcours admission
WebSocket, annonce, sauvegarde et déconnexion. Valider ensuite sur le realm de
test un client admis, une annonce visible, une sauvegarde, une déconnexion puis
un redémarrage et une mise à jour avec reprise des admissions.

Vérification locale du 23 octobre 2026, après l’ajout des taux en direct :
263 tests Realms et 433 tests Admin réussis, zéro échec ; quatre tests
d’intégration MongoDB ignorés car aucune instance de test n’était disponible.
Unity a compilé dans l’éditeur et passé 98 cas déterministes et trois parcours
runtime locaux. Ceux-ci couvrent notamment deux dedicateds recevant les mêmes
taux, leur accusé d’application et une récolte effective passant de 6 à 15
unités puis à `NoYield`, sans redémarrer les mondes. Les instantanés anciens ou
invalides sont refusés et les taux continuent de changer pendant les retries
de résultat d’une commande. Les parcours d’admission et d’annonce/sauvegarde/
déconnexion avec client WebSocket réussissent également.

Le rendu Razor de la configuration a été inspecté dans Chromium à 1440 et
390 pixels, sans débordement horizontal de la page. Les validateurs des
manifests `realm-control` et `admin`, inchangés par la configuration en direct,
avaient réussi lors de l’ajout des actions d’exploitation. Cette validation
ne constitue pas un déploiement ni un essai sur le royaume public.

La préparation intégrée du 23 octobre, basée sur la release services `65ce6d3`,
a passé les **1 541 tests .NET** avec MongoDB 8.0.30 jetable : aucun échec ni
cas ignoré, dont **266 tests Realms** et **457 tests Admin**. Les validateurs
stricts Admin, Reports et Inference passent sur les 52 objets composés ;
les contrôles Realm, WebGL et du renderer passent aussi, ainsi que les
28 tests de promotion d'images. Cette préparation conserve les digests
publiés et les clés opérateur existantes ; elle n'a lancé aucun Editor,
build Unity ni commande d'exploitation publique.
