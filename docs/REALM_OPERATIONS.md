# Exploiter un realm depuis l’admin

La fiche `/Realms/{realmId}` regroupe le redémarrage, la mise à jour vers une
image approuvée, les annonces aux joueurs et les commandes `kick` et `save`.
Les formulaires héritent de la session administrateur et de l’antiforgery.
L’opérateur vient de la session, jamais d’un champ modifiable du navigateur.
Chaque action demande un motif et conserve son résultat dans un historique.

## Choisir le realm et sa cible

La cible **Serveur principal** n’impose plus l’identifiant `beta-web`.
L’opérateur choisit le nom et l’identifiant du nouveau realm, puis une cible
et un profil disponibles. La cible unique est présélectionnée sans remplir
l’identité du realm. Avec un profil sans prise en charge de topologie variable,
le formulaire propose sa configuration fixe pour que le realm soit déployable.
Les destinations restent celles approuvées côté serveur :
aucun namespace ni serveur Kubernetes arbitraire n’est fourni par le navigateur.

Chaque cible et namespace accepte un seul realm actif. Un autre realm peut
réutiliser la destination après suppression complète de son prédécesseur ;
l’ancien identifiant et les personnages restent conservés. L’authentification
dedicated et le routage WSS suivent le nouveau realm sur cette destination.
Le chemin historique `/game/beta-web/` désigne la destination de transport,
pas une restriction de nom du nouveau monde. Plusieurs realms simultanés
exigent plusieurs cibles et namespaces configurés avec leur routage.

## Navigation de l’admin

`/Realms` affiche uniquement la liste des royaumes. **Créer un royaume** ouvre
`/CreateRealm`, où se trouvent le nom, la cible, le profil et le plan complet
des Areas, instances et raids. L’édition du plan d’un brouillon utilise cette
même page ; les anciens liens `/Realms?editRealmId=...` y redirigent.

La fiche `/Realms/{realmId}` affiche une seule section à la fois :

| Section | Contenu |
| --- | --- |
| Aperçu | Liste d'Areas, carte de l'Area sélectionnée et partitions, pools de donjons/raids séparés ; serveurs et coordonnées repliés. |
| Réglages | Multiplicateurs XP/récolte et confirmation de leur application. |
| Maintenance | Déploiement, redémarrage, mise à jour et suppression ; ouvrir l’action voulue pour afficher son formulaire. |
| En jeu | Annonce, déconnexion et sauvegarde ; ouvrir le formulaire souhaité. |
| Historique | Commandes, opérations serveur et changements de taux dans trois volets repliables. |

La section est portée par `?section=overview|settings|maintenance|game|history`.
Actualiser et les retours de formulaire la conservent. Une erreur de validation
rouvre l’action concernée et conserve ses champs. Les historiques ne sont plus
répétés sous les formulaires ; leurs tableaux défilent dans leur propre zone.
L’aperçu ne dépend que de la lecture du royaume : les autres sources sont
chargées à l’ouverture de leur section. Une panne du suivi serveur est
explicitement distinguée d’un serveur qui n’a pas encore reçu les taux.

La refonte de navigation a été vérifiée le 23 octobre 2026 : suite Admin avec
467 réussites et un test MongoDB ignoré, puis 27 contrôles ciblés après les
derniers ajustements. Ils couvrent les sections, les retours de formulaire,
l’authentification, l’antiforgery et la panne du suivi serveur. Le rendu Razor
a été inspecté dans Chromium sur ordinateur et mobile ; les cinq sections et
la création ne débordent pas horizontalement à 320, 390, 768 et 1440 pixels.

## Areas et partitions

Un realm contient plusieurs Areas ; chacune est une carte entière, comme
l'île de départ, et peut utiliser plusieurs dedicateds. Dans `/CreateRealm`,
chaque Area propose **Colonnes X** et **Lignes Y**. Le nombre de dédiés est
leur produit (64 maximum par Area). L’aperçu se met à jour
sans enregistrer ; seule la validation du formulaire conserve le plan. Le
formulaire fonctionne aussi sans JavaScript, avec l’aperçu du dernier rendu
serveur. Les anciens plans restent en répartition automatique jusqu’au choix
explicite du mode grille.

Dans l’aperçu du royaume, le paramètre historique `?island=identifiant`
sélectionne une seule Area ; une valeur inconnue revient à la première.
Les lettres de partition correspondent aux serveurs affichés dans le volet
**Partitions et serveurs dédiés**. Le détail des serveurs des pools est replié
sous leur capacité. La première partition
est en bas à gauche, X vers la droite et Y vers le haut (Z Unity). Les limites
proviennent du snapshot enregistré. Les pools d’instances et de raids n’ont
pas de découpe spatiale sur l'Area.

Le catalogue d’images est local et contrôlé dans `RealmIslandPresentation` :
`starter-island` possède une carte du relief calculée depuis la texture R16
exportée du terrain X/Z `[0, 1536]`, sans bâtiments ni décors. La provenance et
la commande de génération figurent dans
[admin-map/README.md](world/starter-island/admin-map/README.md).
Si les dimensions ou l’origine du plan diffèrent, l’image est signalée comme
illustration ; les coordonnées du plan restent celles enregistrées. Une Area
inconnue affiche sa grille et indique que son image manque, sans charger
une URL fournie par l’opérateur. Le profil fixe `beta-web` avec son unique
serveur `island` affiche l'Area de l'île de départ en une partition, sans inventer de
coordonnées de partition dans son snapshot historique.

Le plan reste modifiable uniquement à l’état **Brouillon**. Il ne constitue
pas une redistribution à chaud des joueurs ni une validation des transferts
entre dédiés. Voir le [contrat de topologie](REALM_DEPLOYMENT.md).

Vérification du plan d’îles, le 23 octobre 2026 : **513 tests Admin** et
**294 tests Realms** réussis (quatre tests MongoDB optionnels ignorés), puis
**75 contrôles Admin ciblés** après le regroupement des détails repliables.
Dans Chromium, sélection d’une autre île, aperçu 3 × 2, origine en bas à
gauche, refus visuel d’une grille de 72 dédiés, image absente et emprise
modifiée ont été vérifiés. Aperçu et création restent sans débordement
horizontal à 320, 390, 768 et 1 440 pixels. La carte de relief a été inspectée
et ses points de contrôle comparés au layout ; la capture 3D Unity n’a pas
abouti (voir la provenance de la carte), aucun test en jeu n’est revendiqué
pour cette modification d’interface et de plan.

La clarification Area/carte et partition/dedicated du 23 octobre 2026 est
vérifiée par **634 tests Admin réussis** (un test MongoDB optionnel ignoré)
et **72 tests Realms ciblés réussis**, sans échec. Les deux Areas d'un même
catalogue peuvent partager leurs coordonnées et conserver des découpages
indépendants. Cette correction de vocabulaire conserve les clés de contrat,
les identifiants et les snapshots existants ; elle n'ajoute pas de nouveau
contenu Unity ni de passage entre cartes.

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
L’autoscaling ne fait pas partie de ces actions.

## Supprimer un royaume

Dans **Maintenance → Supprimer le royaume**, recopier son identifiant exact,
indiquer un motif et confirmer. La demande vérifie la révision affichée et
conserve l’opérateur et le motif. Elle est disponible pour un brouillon, un
royaume prêt ou en échec, ainsi que pour reprendre une suppression échouée.
Un déploiement, redémarrage ou changement d’image doit d’abord se terminer.

Le service Realms conserve un seul réplica avec une stratégie `Recreate`.
Son worker sérialise les accès Kubernetes et revérifie la version persistée
avant de toucher aux ressources : un ancien instantané ne peut pas relancer
un déploiement après la suppression. Plusieurs workers simultanés exigeraient
une coordination distribuée avant d’augmenter ce nombre de réplicas.

Un brouillon est retiré immédiatement. Pour un royaume déjà déployé, Realms
enregistre `Deleting` et le retire immédiatement des catalogues public et
d’admission. Le worker reprend la demande après un redémarrage et supprime
uniquement les ressources Kubernetes appartenant à ce royaume et à son
identité de déploiement. Il attend la disparition des workloads avant de
terminer : la demande HTTP acceptée ne signifie pas que les serveurs ont
déjà fini de s’arrêter. Le namespace, les ressources étrangères et les
secrets externes au déployeur sont conservés. Après l’arrêt complet des
dedicateds, les secrets générés `arkya-dedicated` et `arkya-ghcr` sont retirés
uniquement si leurs étiquettes désignent ce realm et cette identité de
déploiement. Les connexions sont interrompues ;
prévoir l’annonce et la sauvegarde avant de supprimer un royaume actif.

Une panne ou un conflit de propriété reste visible dans l’admin. La cible
et le namespace restent réservés jusqu’au nettoyage confirmé ; corriger
l’erreur puis reprendre la suppression si elle est à l’état `DeleteFailed`.
Les opérations de déploiement, redémarrage, mise à jour, réglages et commandes
ne peuvent pas réactiver un royaume en cours de suppression.

À la fin, l’état `Deleted` et sa date restent enregistrés dans Realms, mais
le royaume disparaît de la liste et ses pages publiques ne le trouvent plus.
Les personnages, leur liaison au realm et leur état persistant dans Characters
sont conservés : cette action ne supprime ni compte ni personnage et ne permet
pas de les transférer automatiquement. L’identifiant du royaume reste réservé
définitivement pour qu’un nouveau royaume n’hérite pas de ces personnages.
La cible et le namespace peuvent être réutilisés après nettoyage avec un
autre identifiant, si leur configuration l’autorise. Une cible explicitement
liée à l’ancien `RealmId` doit d’abord être reconfigurée. La cible actuelle
**Serveur principal** n’a pas cette restriction.
Les entrées standalone et les intentions de mise à jour ne ressuscitent pas
un royaume supprimé.

Au démarrage, la migration MongoDB crée `ux_realm_active_target` et
`ux_realm_active_namespace` avant de retirer les anciens index globaux
`ux_realm_target` et `ux_realm_namespace`. Les réservations actives portent
sur `DeletedAt: null`, y compris les documents historiques sans ce champ.
L’index `_id` et `ux_realm_deployment` conservent l’unicité des identités.
Le compte MongoDB de Realms doit donc pouvoir créer et supprimer ses index.

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
| `POST /admin/realms/{id}/delete` | `{expectedRevision, reason, confirmRealmId}` ; `202` après enregistrement durable, suppression Kubernetes suivie séparément. |
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
comme serveur `island` géré par Realms dans le namespace `arkya-beta-realm`
à contacter Realms sur 8080.
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
