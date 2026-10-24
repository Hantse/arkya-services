# Carte des positions dans l'administration

La fiche d'un realm propose **Live map** sous
`/Realms/{realmId}?section=map`. Le sélecteur d'Area conserve les cartes
indépendantes, même lorsqu'elles partagent les mêmes coordonnées. La carte
affiche les joueurs admis, les PNJ et les créatures vivantes de la simulation
serveur. Les filtres permettent de masquer chaque catégorie et la sélection
d'un marqueur donne son identité, ses coordonnées et son dedicated.

Les positions sont relevées et envoyées toutes les deux secondes environ,
puis l'administration les interroge à la même cadence. Il s'agit d'un suivi
opérateur périodique ; la latence réseau et le temps des requêtes s'ajoutent
à cette cadence. La carte ne lit pas les sauvegardes de position Characters,
qui ne constituent pas un flux de positions actuelles.

## Origine et fraîcheur

Un système Unity réservé au monde serveur collecte les entités autoritaires.
Les joueurs en cours d'admission, déconnectés ou dont l'autorité est gelée
pendant un transfert sont exclus. L'époque d'autorité permet de dédupliquer
un personnage visible dans deux relevés voisins pendant un handoff. Les PNJ
utilisent leur identifiant de catalogue ; les créatures ont un identifiant
local à leur Area. Aucun nouveau champ de ghost n'est nécessaire.

Chaque envoi remplace intégralement le relevé précédent du dedicated, y
compris lorsqu'il est vide. Realms conserve seulement le dernier relevé dans
`realm_world_maps`, avec l'heure de réception et la révision du realm. Ce
stockage d'observation n'est ni une sauvegarde de personnage ni un historique
de déplacements.

Après dix secondes sans relevé, les positions du serveur sont masquées et
son état devient **Stale**. Un realm qui n'est pas prêt, un relevé d'une
ancienne révision et un dedicated sans télémétrie ne sont pas présentés comme
une carte vide et actuelle. Une erreur de lecture est indiquée dans la vue.
Le nombre d'entités d'un relevé est limité à 4 096 par dedicated ; toute
troncature est explicitement signalée avec le nombre total.

Les images de relief et le plan de partitions restent ceux de
`RealmIslandPresentation`. Le plan horizontal correspond à X/Z Unity ; Z
croît vers le haut de la carte. Une image dont l'emprise ne correspond pas au
plan reste une illustration, comme dans l'aperçu existant.

## Contrat et accès

| Route privée | Accès | Usage |
| --- | --- | --- |
| `POST /internal/realms/{realmId}/servers/{serverId}/world-map` | `X-Arkya-Dedicated-Key`, dérivée du couple realm/serveur | Remplacer le relevé autoritaire, réponse `204` |
| `GET /admin/realms/{realmId}/world-map` | `X-Arkya-Admin-Key` | Lire les relevés et leurs états |

Le navigateur passe par un handler Razor authentifié de la fiche du realm.
Les clés privées restent dans Admin et le dedicated. Le host d'administration
d'un realm limite la lecture à ce realm. Ces routes Realms ne sont pas
ajoutées à l'API publique de Gateway.

Le corps d'envoi utilise le protocole `version: 1`, une `instanceId` UUID,
`entities`, `totalEntities` et `truncated`. Une entité fournit `id`, `kind`
(`player`, `npc` ou `creature`), `name`, `x`, `y`, `z` et `authorityEpoch`.
Les joueurs portent un UUID de personnage et une époque positive ; les
autres catégories ont une époque nulle. Identifiants, noms, doublons,
coordonnées finies, compteurs et emprise d'Area sont validés côté Realms.
Une marge autour de l'Area permet les transitions d'autorité aux frontières.
Le corps est limité à 2 Mio pour cette route uniquement.

La remontée réutilise la configuration privée Realms du dedicated et sa clé
serveur existante. Elle possède sa propre requête en cours et ses reprises ;
une indisponibilité de la carte n'empêche pas les commandes administrateur
ou les sauvegardes de fonctionner. Les requêtes et réponses de consultation
ne sont pas mises en cache.

## Mise en service

Déployer les services Realms et Admin, puis une image dedicated contenant le
nouveau collecteur. Un ancien dedicated apparaît sans télémétrie jusqu'à sa
mise à jour ; l'interface n'invente pas de positions à partir des personnages
persistés. Aucun changement du protocole Netcode client/serveur n'est requis.
La livraison de ces sources ne prouve pas que les dedicateds déjà déployés
ont été remplacés.

La vérification du parcours complet nécessite un dedicated, un client admis,
une lecture Admin et l'observation d'un déplacement, d'une déconnexion et de
l'expiration d'un relevé. Les tests isolés des contrats et du rendu ne
remplacent pas ce contrôle en jeu.

## Vérification du 24 octobre 2026

- Realms : **548 tests réussis**, 7 tests MongoDB optionnels ignorés.
- Admin : **979 tests réussis**, 1 test MongoDB optionnel ignoré ; les
  **6 tests JavaScript** de la carte passent et sont exécutés par Backend CI.
- Unity `6000.6.6f1` : compilation après rafraîchissement via Coplay MCP,
  **8 tests Edit Mode du collecteur réussis**. Un **test d'intégration
  serveur/client WebSocket réel** passe aussi : admission, mouvement par les
  entrées client, remontée HTTP, erreur `404`, commandes et sauvegarde
  indépendantes, reprise et relevé vide après déconnexion.
- Navigateur Edge/Chromium sur une fixture isolée du vrai rendu Razor :
  mouvement des marqueurs, sélection, filtres, recherche, séparation des
  Areas, rapports périmés/absents/incomplets, panne et noms traités comme
  texte vérifiés. Aucun débordement horizontal à 320, 390, 768 et 1 440 px ;
  captures ordinateur et mobile inspectées.

Les tests MongoDB ignorés demandent `ARKYA_TEST_MONGO`. Le test réseau Unity
émet des diagnostics Netcode de regroupement de ticks sous charge dans la
console ; son résultat fonctionnel est réussi. Les avertissements existants
sur les API ECS managées dépréciées restent présents. Ces contrôles ne
constituent pas une validation d'un build dédié exporté, de sa charge en
production ou d'une mise à jour des serveurs déjà déployés.
