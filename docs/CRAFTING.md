# Premières écoles d'artisanat

La tranche du 24 octobre 2026 ajoute quatre écoles d'artisanat au personnage
d'exploration et deux recettes de briques au **stone cutter**. La récolte et la
fabrication attribuent de l'XP à l'école concernée, indépendamment des
[classes de combat](CHARACTER_STATS_SKILLS.md).

## Écoles et compétences

| École | Accès initial | Compétences |
| --- | --- | --- |
| Mineur | Niveau 1, sans prérequis | `Mining`, `Quarrying`, `Stonecrafting`, `Smelting` |
| Forgeron | Niveau 1, sans prérequis | `Armorcraft`, `Weaponsmith` |
| Bûcheron | Niveau 1, sans prérequis | `Logging`, `Woodworking` |
| Charpentier | Bûcheron niveau 10 | À définir |

Chaque école conserve son niveau et son XP. Les compétences d'une école ont
automatiquement la valeur de son niveau : Mineur niveau 4 donne par exemple
`Mining 4`, `Quarrying 4`, `Stonecrafting 4` et `Smelting 4`. Cette première
version n'ajoute pas de compteurs d'XP distincts par compétence ni d'achat de
rangs. Les compétences montent ensemble ; elles ne modifient pas encore le
rendement ou la puissance des outils.

La courbe provisoire demande **100 × niveau courant** XP pour monter d'un
niveau, avec un plafond à **100**. Les excédents peuvent traverser plusieurs
niveaux ; au plafond, l'XP reste à zéro. Atteindre Bûcheron niveau 10 ouvre
automatiquement Charpentier au niveau 1. Les recettes et compétences propres
au charpentier, ainsi que les recettes du forgeron, restent à définir.

Les actions connues attribuent leur XP à leur école propriétaire. La récolte
de bois progresse en Bûcheron ; la pierre, le grès et les minerais progressent
en Mineur. Le niveau de Warrior, Scout, Mage, Cleric ou Druid, leurs XP, leurs
réserves et le rating de combat restent indépendants.

Le panneau **Métiers · K** affiche les niveaux et XP des quatre écoles, puis
leurs huit compétences. Le panneau **V** du stone cutter reste réservé aux
recettes de cette machine. La jauge principale d'XP continue de suivre la
classe de combat.

## Récolte et premières recettes

Maintenir **G** près d'une ressource permet de récolter les matériaux ; les
[règles de portée, d'outils et de sacs](HARVESTING.md) s'appliquent. Seul le
joueur qui termine une ressource et reçoit effectivement son butin gagne
**5 XP** dans l'école correspondante. Les coups intermédiaires et les
attributions refusées ne donnent pas d'XP.

S'approcher du stone cutter, puis appuyer sur **V** ou sur l'invite affichée
ouvre les deux recettes. Chaque clic réussi fabrique une brique. **Échap** ou
le bouton de fermeture quitte le panneau. Le personnage doit être vivant,
à moins de 3 mètres horizontalement et de 3 mètres verticalement de la machine,
avec une ligne de vue libre. Les demandes sont espacées d'au moins
**0,85 seconde** côté serveur.

| Recette | Entrée dans les sacs | Sortie dans les sacs | Compétence et niveau requis | XP |
| --- | --- | --- | --- | --- |
| Brique de pierre | 2 `resource:stone` | 1 `resource:stone-brick` | Mineur 1, `Stonecrafting` | 10 Mineur |
| Brique de grès | 2 `resource:sandstone` | 1 `resource:sandstone-brick` | Mineur 1, `Stonecrafting` | 10 Mineur |

Les quantités et les récompenses sont des réglages initiaux. Pierre et grès
pèsent chacun **1 000 g** ; les deux types de briques pèsent **2 000 g** par
unité. Le poids total des ingrédients consommés est donc conservé. Les piles
sont limitées à 100 unités et la capacité de portage existante s'applique,
y compris la réduction accordée par les cristaux équipés.

## Machine dans le monde

Sans station déclarée dans la scène, StarterIsland reçoit une station
d'exécution à **+5 m sur X, +3 m sur Z depuis son point d'arrivée**. Sa hauteur
est échantillonnée sur le Terrain ; elle est ajoutée seulement si cette
position est au-dessus de la limite de rivage. Les autres Areas n'obtiennent
pas automatiquement cette station. Seule la partition propriétaire de sa
position l'accepte côté serveur.

`CharacterCraftStationAuthoring` permet d'ajouter des stations avec des IDs
positifs uniques. Dès qu'une scène en déclare, ces stations remplacent le
placement automatique. Le catalogue commun détermine les mêmes positions
côté serveur et client, sans réécrire les scènes ouvertes ni les assets
fournisseurs.

Le premier visuel est un assemblage procédural : plateau en pierre, supports
en bois et roue de coupe métallique. Il sert de représentation fonctionnelle
provisoire. Le modèle final peint, son animation, son son et sa validation
visuelle dans l'Editor restent à réaliser.

## Autorité et persistance

`CharacterCraftState` est un composant ECS distinct de
`CharacterGameplayState`, répliqué uniquement au propriétaire. Le client
envoie l'ID de station, la recette et une séquence ; il ne soumet ni quantité
produite ni XP. Le serveur vérifie la connexion, l'acteur, les séquences, le
délai, la portée, la ligne de vue, la partition, la recette et l'état du
personnage. Un acteur gelé ou dont l'inventaire est verrouillé ne fabrique pas.

La fabrication prépare les sacs et l'XP avant de modifier l'état autoritaire.
Un manque de matériaux, de place ou de capacité de portage refuse toute
l'opération : aucun ingrédient consommé, aucune brique et aucune XP.
Les positions des autres piles sont préservées. Les récompenses utilisent les
vrais buffers de possessions, avec les mêmes garanties de sauvegarde et de
transfert que la récolte.

Les sauvegardes périodiques, déconnexions, handoffs entre partitions et passages
entre Areas conservent tous les niveaux et XP artisanaux. L'enveloppe backend
reste au schéma 1 et la simulation au format 3 ; un bloc facultatif `craft`
version 1 contient le profil complet. Les snapshots historiques aux formats
1, 2 ou 3 sans ce bloc reçoivent le profil artisanal initial. Un bloc présent
mais invalide, y compris `null`, est refusé au chargement au lieu de perdre la
progression. Le parcours local sans compte conserve seulement sa session.

Le composant ghost et les nouvelles RPC exigent la paire réseau
**6 / `arkya-crafting-v1`**. Unity, Characters et le service CI de topology
reconnaissent cette paire. L'admission exige une correspondance exacte entre
client et destination ; les anciens builds ne sont pas compatibles avec ce
ghost. Les profils de déploiement existants restent inchangés. Reconstruire
ensemble client et dedicated, puis mettre à jour explicitement le profil du
realm avant d'ouvrir les admissions à ce build.

## Vérification et limites

La suite backend Characters donne **343 tests réussis, 0 échec et 36 tests
MongoDB ignorés** faute d'environnement d'intégration configuré. Les nouveaux
cas vérifient la sauvegarde, les deux chemins de transfert, la reconnexion,
le refus de l'ancienne autorité et les IDs des matériaux. Les trois tests
Python du service CI de topology réussissent avec la nouvelle paire réseau.

Les tests Unity ajoutés couvrent le catalogue, les prérequis, les courbes d'XP,
la séparation des progressions, les recettes atomiques, les stations et la
compatibilité des snapshots. **33 cas de règles pures réussissent hors Editor**.
Les quatre assemblies `Arkya.Gameplay`, `Arkya.Characters.Runtime`,
`Arkya.Characters.Editor` et `Arkya.Characters.EditorTests` compilent hors
Editor avec Roslyn et les générateurs ECS/Netcode. Les assemblies de runtime
compilent également avec les symboles serveur ; cette variante utilise les
références Editor disponibles et ne remplace pas un vrai build dédié.

L'exécution des tests dans Unity reste à effectuer, notamment la fixture réseau.
Le MCP Unity a échoué à son initialisation sur `127.0.0.1:8080` pendant cette
intervention. La compilation Editor, les tests Edit Mode/Play Mode, un vrai
parcours serveur/client et une capture du stone cutter ne sont donc pas
validés. Les contrôles hors Editor ne remplacent pas ces vérifications.

Il n'y a pas encore de file de fabrication, de durée de recette, de coût en
combustible, de construction de station par le joueur, de déblocage par PNJ
ni de publication du catalogue artisanal dans l'Admin. Les futures recettes
de fonte, forge et charpenterie restent distinctes de cette première tranche.
