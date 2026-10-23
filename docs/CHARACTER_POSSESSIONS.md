# Sauvegarde des possessions du personnage

Les sacs, la banque et l'équipement du personnage admis sont détenus par le
serveur Unity. Ils sont sauvegardés dans `game_leases.State.Possessions` par
Characters et consultables en lecture seule depuis la fiche d'administration.
La banque appartient au personnage ; cette tranche n'ajoute pas de banque
partagée entre comptes ou personnages.

## Source de vérité et cycle de sauvegarde

`InventorySlot` reste l'unique buffer des objets portés, avec la capacité de
`CharacterInventory`. Il accepte les ressources historiques et les identifiants
d'items du catalogue, ainsi que des emplacements explicites pour conserver les
trous dans un sac. `CharacterBank`/`CharacterBankItem` détiennent la banque et
`CharacterEquipmentItem` les objets équipés. `EquippedHarvestTool` et
`SocketedStone` conservent la configuration réelle de l'outil de récolte.
Ces données ne sont pas répliquées aux clients par ce changement ; aucun
endpoint joueur ou RPC ne permet d'imposer leur contenu.

Lors de l'admission, le serveur restaure l'instantané avant d'utiliser l'acteur.
Un personnage historique sans possessions reçoit des conteneurs réellement
vides en mémoire serveur, de 150 emplacements chacun, puis les enregistre à sa
première sauvegarde. Aucun objet initial n'est ajouté. La capacité sauvegardée
est ensuite restaurée, sans être remplacée par le défaut.

Les sauvegardes périodiques, les sauvegardes forcées de l'administration et la
sortie utilisent un instantané des buffers serveur. Les possessions sont
écrites avec la position et la simulation dans le même document MongoDB, avec
le même bail, la même époque et la même révision attendue. Il n'existe pas de
seconde écriture indépendante susceptible de sauvegarder seulement la moitié
du personnage. Un état ECS invalide n'écrase pas les objets persistés.

Chaque requête conserve son instantané immuable pendant les retries. Les
mutations d'inventaire sont suspendues lors du gel d'autorité et dès la fermeture
du personnage, avant sa sauvegarde finale. Un transfert abandonné libère ce
verrou ; une récolte en attente peut alors reprendre une seule fois.

Le préchargement d'un transfert contient les possessions de préparation ; le
seal contient l'état final, figé par le serveur source. Le commit transmet cet
état avec l'autorité au serveur cible. Les portails entre Areas changent la
position et la simulation de déplacement, tout en conservant les mêmes
possessions. Un abort conserve l'instantané source. Les règles de bail et de
révision empêchent l'ancien serveur de réécrire après le commit.

## Contrat versionné

Le champ facultatif `possessions` est séparé du JSON opaque `simulation`. Son
schéma courant est `1`. Il figure dans `SavedWorldState`, les requêtes de
sauvegarde et les requêtes prepare/seal. Le document MongoDB utilise le schéma
`6` lorsqu'un instantané de possessions existe ; les schémas précédents restent
lisibles.
La limite HTTP est de 256 Kio pour laisser coexister les conteneurs complets
et l'enveloppe de simulation, qui conserve sa propre borne de 32 768 caractères.

```json
{
  "schemaVersion": 1,
  "bags": {
    "slotCapacity": 150,
    "slots": [{ "slot": 0, "itemId": "resource:wood", "quantity": 7 }]
  },
  "bank": {
    "slotCapacity": 80,
    "slots": [{ "slot": 79, "itemId": "old-copper-ring", "quantity": 1 }]
  },
  "equipment": [{ "slot": "main-hand", "itemId": "valley-sword", "quantity": 1 }],
  "harvestTool": null
}
```

Ces valeurs illustrent le contrat et ne sont jamais injectées dans les
personnages. Les limites de structure sont les suivantes :

- Capacité de chaque conteneur de 1 à 150 ; seules les piles occupées sont
  envoyées. Un emplacement est un indice unique de `0` à `slotCapacity - 1`.
- Quantité entière de 1 à 1 000 000 par pile. Il s'agit d'une borne de transport ;
  les règles de gain serveur doivent encore respecter la taille de pile et le
  poids du catalogue applicable.
- ID de catalogue de 1 à 64 caractères minuscules, chiffres et mots séparés par
  un tiret. Les douze ressources historiques utilisent l'espace réservé
  `resource:wood`, `resource:enchanted-wood`, etc., sans collision avec un ID du
  catalogue.
- Douze emplacements d'équipement uniques : `main-hand`, `off-hand`, `head`,
  `chest`, `hands`, `legs`, `feet`, `neck`, `shoulders`, `back`, `waist`, `finger`.
  Chaque emplacement contient au plus un objet, avec une quantité de 1.
- L'outil facultatif conserve famille, niveau de 0 à 100, bonus de récolte et
  jusqu'à six pierres, avec les valeurs entières en points de base. Le niveau 0
  représente les mains nues et ne peut contenir aucune pierre.

Les clés invalides, doublons d'emplacements, quantités invalides, sections
manquantes et versions inconnues sont refusés avant l'écriture. Les retries
comparent les valeurs des instantanés, y compris après une sérialisation JSON ;
réordonner des entrées sans modifier leurs emplacements ne crée pas de conflit.

Une ancienne requête qui omet `possessions` conserve le dernier instantané. Pour
enregistrer un conteneur vide, le serveur transmet explicitement un instantané
valide avec `slots: []`. L'absence d'un champ ne peut donc pas effacer un sac.

Le transport Unity préserve explicitement `possessions: null` et
`harvestTool: null`, car `JsonUtility` matérialise sinon les classes absentes en
objets par défaut. Cette normalisation concerne seulement les champs connus du
contrat et laisse le contenu de `simulation` intact. Un objet explicitement
mal formé reste refusé ; il n'est jamais remplacé par un inventaire vide.

## Consultation dans l'admin

La fiche `/Character/{id}` affiche les emplacements réels, les quantités et les
identifiants sauvegardés des sacs, de la banque et de l'équipement, ainsi que
l'outil et ses pierres. L'affichage numérote les emplacements de sacs à partir
de 1. Un sac sauvegardé vide est distingué d'un état jamais sauvegardé, invalide
ou de version inconnue. Les noms des ressources sont connus ; les autres IDs
restent affichés tels quels pour ne pas substituer une définition brouillon
différente du contenu effectivement utilisé par le serveur.

Les valeurs sont celles de la dernière sauvegarde autoritaire. Les personnages
anciens restent « non sauvegardés » jusqu'à leur passage sur un dedicated mis à
jour et une sauvegarde réussie. Ce raccordement n'ajoute ni loot artificiel,
ni interface de dépôt/retrait bancaire, ni interface d'équipement en jeu. Les
futurs systèmes de gain et d'interaction doivent modifier ces mêmes buffers
serveur, puis laisser la sauvegarde existante les persister. La récolte de la
fondation reste indépendante des interactions jouables de l'île.

## Vérification

Les tests backend utilisent une instance MongoDB locale de test isolée et
vérifient les instantanés non vides, les sauvegardes/reconnexions, les transferts
entre partitions et Areas, les retries, les reprises du stockage, les anciennes
autorités, les écritures joueur refusées et la projection administrative.
L'UI est vérifiée avec les vrais rendus Razor de réponses de test explicites,
sur ordinateur et mobile.

Validation du 23 octobre 2026 : 328 tests Characters et 868 tests Admin réussis,
sans test ignoré, avec MongoDB réel isolé. Dans Unity 6000.6.6f1, la compilation
complète réussit et les 155 tests du périmètre possessions, JSON, récolte,
règles, handoff et admission sont validés. Un ancien générateur JSON de fixture
a été corrigé après le premier lot, puis son test a été relancé avec succès.
Les deux nouveaux parcours HTTP/WebSocket vérifient les contenus non vides,
l'outil présent ou absent, la copie cliente ignorée, la sauvegarde forcée,
la sauvegarde finale et la restauration après réadmission. Les tests JSON
couvrent aussi les champs absents, les données corrompues et la préservation de
la simulation opaque. La console ne contient aucune erreur à l'issue des tests.
