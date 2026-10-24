# Artisanat : écoles, skills et progression

La conception retenue le 24 octobre 2026 définit neuf écoles, seize skills
et des gains par niveau propres à chaque couple école/skill. Les écoles
spécialisées se débloquent à partir des skills acquis, avec des seuils de
150 ou 200. Cette conception reste à implémenter ; l'état de la première
implémentation et ses vérifications sont conservés plus bas.

## Conception retenue

### Écoles et rôles

Un personnage peut progresser dans plusieurs écoles et conserve le niveau
et l'XP de chacune. Une seule école est active à la fois. La progression
artisanale reste indépendante des classes et du rating de combat.

| École | Métier | Accès | Rôle |
| --- | --- | --- | --- |
| Miner | Mineur | École de base, sans prérequis | Extraction des minerais et de la pierre, fonte et préparation des matériaux. |
| Logger | Bûcheron | École de base, sans prérequis | Abattage et préparation du bois. |
| Blacksmith | Forgeron | École de base, sans prérequis | Travail du métal, fabrication généraliste d'armes, d'armures et d'outils. |
| Spellcrafter | Créateur de sorts | École de base, sans prérequis | Fabrication de sorts que les joueurs peuvent apprendre, personnalisables avec des techniques. |
| Armorer | Armurier | Spécialisation | Maîtrise de la fabrication d'armures. |
| Tinker | Bricoleur | Spécialisation | Fabrication d'outils, de mécanismes et d'objets utilitaires. |
| Enchanter | Enchanteur | Spécialisation | Manipulation du mana et amélioration magique des armes, armures et outils. |
| Carpenter | Charpentier | Spécialisation de housing | Poutres, charpentes, planchers, portes, escaliers et mobilier en bois. |
| Mason | Maçon | Spécialisation de housing | Fondations, murs en pierre, dallages, colonnes et cheminées. |

Spellcrafter crée la magie que le personnage apprend ; Enchanter fixe la
magie dans les objets. Les skills de transformation préparent les matériaux
intermédiaires : Travail du bois produit notamment les planches, tandis que
Charpenterie sert aux éléments assemblés et au mobilier. Travail de la pierre
et Maçonnerie suivent la même distinction.

### Skills et gains par niveau

Chaque ligne définit la contribution d'une école à un skill. Les gains
s'appliquent aux niveaux gagnés dans cette école, sans compteur d'XP distinct
par skill. Une école débute au niveau 1 avec une contribution de 0 à chacun
de ses skills. Les libellés ci-dessous décrivent la conception ; ils ne
renomment pas les identifiants techniques de l'implémentation actuelle.

| Skill | École | Gain par niveau |
| --- | --- | ---: |
| Minage | Miner | +10 |
| Minage | Blacksmith | +8 |
| Extraction de pierre | Miner | +10 |
| Extraction de pierre | Mason | +8 |
| Fonte | Miner | +10 |
| Fonte | Blacksmith | +8 |
| Fonte | Armorer | +8 |
| Travail de la pierre | Mason | +10 |
| Travail de la pierre | Miner | +8 |
| Travail du métal | Blacksmith | +10 |
| Travail du métal | Armorer | +8 |
| Travail du métal | Tinker | +8 |
| Fabrication d'armes | Blacksmith | +10 |
| Fabrication d'armures | Armorer | +10 |
| Fabrication d'armures | Blacksmith | +8 |
| Fabrication d'outils | Tinker | +10 |
| Fabrication d'outils | Blacksmith | +8 |
| Mécanique | Tinker | +10 |
| Création de sorts | Spellcrafter | +10 |
| Manipulation du mana | Enchanter | +10 |
| Manipulation du mana | Spellcrafter | +8 |
| Enchantement | Enchanter | +10 |
| Abattage | Logger | +10 |
| Abattage | Carpenter | +8 |
| Travail du bois | Carpenter | +10 |
| Travail du bois | Logger | +8 |
| Travail du bois | Tinker | +8 |
| Charpenterie | Carpenter | +10 |
| Maçonnerie | Mason | +10 |

### Calcul des skills partagés

**La valeur intrinsèque d'un skill est la meilleure contribution parmi les
écoles débloquées qui l'enseignent. Les contributions ne s'additionnent pas.**

```text
Contribution d'une école = (niveau de l'école − 1) × gain par niveau
Valeur du skill = maximum des contributions des écoles qui l'enseignent
```

Le personnage conserve ces contributions lorsqu'il change d'école active.
L'école active détermine la progression développée, sans diminuer les skills
déjà acquis. Une action éligible à l'XP récompense cette seule école lorsqu'elle
correspond à un skill enseigné par celle-ci. Ainsi, miner en Blacksmith peut
faire progresser Blacksmith ; chaque niveau gagné apporte alors sa
contribution de +8 en Minage, sans XP simultanée dans Miner. La valeur visible
du skill n'augmente que si cette contribution devient la meilleure.

Exemple : Miner niveau 21 apporte 200 en Minage et Blacksmith niveau 31
apporte 240. Le personnage possède donc 240 en Minage. En atteignant Miner
niveau 26, cette école apporte 250 et devient la meilleure contribution.

### Prérequis de déblocage des spécialisations

Les prérequis utilisent la **valeur intrinsèque du skill, sans bonus
d'équipement ou d'enchantement**. Toute école enseignant le skill peut
contribuer à atteindre le seuil ; le parcours indiqué est un exemple depuis
une école de base, pas une obligation de monter cette école précise.
Le déblocage est définitif et la nouvelle école commence au niveau 1.

Les seuils sont répartis à 150 pour les spécialisations d'outillage et de
housing, et à 200 pour la spécialisation d'armures et l'enchantement.

| École spécialisée | Skill requis | Seuil minimum | Premier parcours depuis une école de base |
| --- | --- | ---: | --- |
| Armorer | Fabrication d'armures | 200 | Blacksmith niveau 26 : 200 points |
| Tinker | Fabrication d'outils | 150 | Blacksmith niveau 20 : 152 points |
| Enchanter | Manipulation du mana | 200 | Spellcrafter niveau 26 : 200 points |
| Carpenter | Travail du bois | 150 | Logger niveau 20 : 152 points |
| Mason | Travail de la pierre | 150 | Miner niveau 20 : 152 points |

À +8 par niveau, le niveau 19 donne 144 points et le niveau 20 donne 152 :
le seuil de 150 est donc atteint au niveau 20. Le seuil de 200 est atteint au
niveau 26.

Pour Carpenter, la cible remplace explicitement l'ancien prérequis
**Logger niveau 10** par **Travail du bois 150**. La migration des personnages
ayant déjà débloqué Carpenter dans le prototype reste à définir ; elle devra
préserver ce déblocage et leur progression.

La règle du maximum produit un palier au début d'une spécialisation. Par
exemple, Armorer débloqué grâce à Blacksmith niveau 26 hérite déjà de 200 en
Fabrication d'armures. Sa propre contribution rejoint 200 au niveau 21 et
la dépasse au niveau 22 avec 210, si Blacksmith reste au même niveau. Le
seuil de déblocage n'est pas ajouté comme bonus à sa contribution. Des
recettes ou techniques propres à la spécialisation pourraient donner un
intérêt immédiat à ces premiers niveaux ; leur catalogue reste à définir.

### Portée de cette décision

Cette conception fixe les écoles, leurs rôles, les gains de skills, leur
partage et les prérequis. Elle ne fournit pas encore les nouvelles recettes,
les techniques, les effets des enchantements ou la construction de housing.
La courbe d'XP et le plafond du prototype restent des réglages provisoires.
Le calcul uniforme à +5, les règles actuelles d'attribution d'XP et le
déblocage de Carpenter devront être adaptés lors de l'implémentation, avec
préservation des progressions sauvegardées.

## État de la première implémentation

La tranche du 24 octobre 2026 ajoute quatre écoles d'artisanat au personnage
d'exploration, trois maîtres en ville et un grimoire de formules apprises.
La récolte et la fabrication attribuent de l'XP uniquement lorsque l'école
concernée est active, indépendamment des
[classes de combat](CHARACTER_STATS_SKILLS.md).

### Écoles et compétences

| École | Accès initial | Compétences |
| --- | --- | --- |
| Mineur | Niveau 1, sans prérequis | `Mining`, `Quarrying`, `Stonecrafting`, `Smelting` |
| Forgeron | Niveau 1, sans prérequis | `Armorcraft`, `Weaponsmith` |
| Bûcheron | Niveau 1, sans prérequis | `Logging`, `Woodworking` |
| Charpentier | Bûcheron niveau 10 | À définir |

Chaque école conserve son niveau et son XP lors des changements de métier.
Une seule est active ; le profil neuf commence en Mineur. Les compétences
d'une école valent **5 × (niveau − 1)** : niveau 1 donne 0, niveau 21 donne 100,
niveau 41 donne 200. Cette conversion est un réglage initial explicite ; il n'y
a pas de compteur d'XP distinct par compétence ni d'achat de rangs. Les
compétences d'une même école montent ensemble. Elles modifient maintenant le
coût des formules, sans ajouter de bonus à la puissance des outils.

La courbe provisoire demande **100 × niveau courant** XP pour monter d'un
niveau, avec un plafond à **100**. Les excédents peuvent traverser plusieurs
niveaux ; au plafond, l'XP reste à zéro. Atteindre Bûcheron niveau 10 ouvre
automatiquement Charpentier au niveau 1. Les recettes et compétences propres
au charpentier, ainsi que les recettes du forgeron, restent à définir.

La récolte de bois progresse uniquement en Bûcheron actif ; la pierre, le grès
et les minerais progressent uniquement en Mineur actif. Récolter du bois en
Mineur accorde le bois, sans XP dans aucune école. Une formule déjà apprise
reste utilisable dans une autre école, avec ses prérequis habituels, mais ne
donne alors aucune XP artisanale. Le niveau de Warrior, Scout, Mage, Cleric ou Druid, leurs XP, leurs
réserves et le rating de combat restent indépendants.

La fiche **C** contient trois onglets : **Fiche personnage**, **Compétences**
(combat et artisanat), **Écoles d'artisanat** (niveaux, XP et école active).
**K** ouvre directement les écoles. **L** ouvre la fenêtre séparée **Formules**,
le grimoire des recettes apprises. Le panneau **V** du stone cutter reste
réservé aux recettes de cette machine. La jauge principale d'XP suit la classe de combat.

### Maîtres de métier et apprentissage

Trois PNJ supplémentaires, fixes, utilisent le catalogue d'Aubefont et les
modèles Paladin/Valkyrie existants : un maître bûcheron, un maître mineur et
un maître forgeron (IDs stables 21, 22, 23). **E** ouvre leur dialogue à proximité.
Les boutons permettent de choisir leur école, demander la dotation d'outils
de base et apprendre leurs formules disponibles. Ces actions n'utilisent pas l'inférence.

La dotation est gratuite et accordée une seule fois par école : hache de
bûcheron, pioche de mineur et pic de carrier, ou marteau de forge. Un manque
de place ou de capacité de portage refuse toute la dotation, sans marquer
les outils comme reçus. Ces outils utilisent la puissance basique de récolte
existante et ne remplacent pas les outils améliorés ou cristaux équipés.

Le maître mineur enseigne séparément les deux formules de briques. Le personnage
neuf ne les connaît pas ; une formule apprise apparaît une seule fois dans le
grimoire. Un second apprentissage est refusé sans mutation. Les recettes de
forge et de bois restent à définir ; leurs maîtres fournissent déjà les outils
et le changement d'école. Le charpentier garde son prérequis Bûcheron 10, sans
nouveau maître ni formule dans cette tranche.

### Récolte et premières recettes

Maintenir **G** près d'une ressource permet de récolter les matériaux ; les
[règles de portée, d'outils et de sacs](HARVESTING.md) s'appliquent. Seul le
joueur qui termine une ressource et reçoit effectivement son butin gagne
**5 XP** si l'école correspondante est active. Les coups intermédiaires et les
attributions refusées ne donnent pas d'XP.

S'approcher du stone cutter, puis appuyer sur **V** ou sur l'invite affichée
ouvre les deux recettes. Chaque clic réussi fabrique une brique. **Échap** ou
le bouton de fermeture quitte le panneau. Le personnage doit être vivant,
à moins de 3 mètres horizontalement et de 3 mètres verticalement de la machine,
avec une ligne de vue libre. Les demandes sont espacées d'au moins
**0,85 seconde** côté serveur.

| Recette | Entrée dans les sacs | Sortie dans les sacs | Compétence et niveau requis | XP |
| --- | --- | --- | --- | --- |
| Brique de pierre | 4 → 2 `resource:stone` | 1 `resource:stone-brick` | Mineur 1, `Stonecrafting` 0 → 200 | 10 Mineur actif |
| Brique de grès | 4 → 2 `resource:sandstone` | 1 `resource:sandstone-brick` | Mineur 1, `Stonecrafting` 0 → 200 | 10 Mineur actif |

Le coût est interpolé entre le skill minimum **0** et optimal **200**, avec
arrondi supérieur et bornage : 4 à 0, 3 à 100, 2 à 200 et au-delà. Les prochains
tiers peuvent définir d'autres seuils et quantités dans leur définition de
recette ; aucune formule des tiers suivants n'est ajoutée ici. Pierre et grès
pèsent chacun **1 000 g** ; les deux types de briques pèsent **2 000 g** par
unité. La perte de matière diminue avec la compétence. Les piles
sont limitées à 100 unités et la capacité de portage existante s'applique,
y compris la réduction accordée par les cristaux équipés.

### Machine dans le monde

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

### Autorité et persistance

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
version 2 contient le profil complet, les formules apprises et les dotations
déjà reçues. Un ancien bloc `craft` version 1 conserve ses niveaux/XP et reçoit
les deux formules de briques déjà accessibles dans cette version ; ses dotations
restent disponibles. Les snapshots historiques aux formats
1, 2 ou 3 sans ce bloc reçoivent le profil artisanal initial. Un bloc présent
mais invalide, y compris `null`, est refusé au chargement au lieu de perdre la
progression. Le parcours local sans compte conserve seulement sa session.

Le composant ghost et les nouvelles RPC d'artisanat ont introduit la paire réseau
**7 / `arkya-craft-schools-v1`**. Le build actif utilise désormais
**8 / `arkya-npc-vitals-v1`** pour les réserves et niveaux répliqués des PNJ,
en conservant les contrats artisanaux de cette tranche. Unity, Characters et
le service CI de topology reconnaissent la nouvelle paire. L'admission exige une correspondance exacte entre
client et destination ; les anciens builds ne sont pas compatibles avec ce
ghost. Les profils de déploiement existants restent inchangés. Reconstruire
ensemble client et dedicated, puis mettre à jour explicitement le profil du
realm avant d'ouvrir les admissions à ce build.

### Vérification et limites

Les résultats suivants ont été consignés pour la première implémentation.
Ils ne valident pas la nouvelle conception à neuf écoles ; la mise à jour
présente est documentaire et ne modifie aucun code de jeu.

La vérification ciblée backend donne **60 tests réussis, 0 échec** :
compatibilité des paires réseau, admission exacte, sauvegarde, deux chemins
de transfert, reconnexion et refus de l'ancienne autorité. Les trois tests
Python du service CI de topology réussissent avec la nouvelle paire réseau.

Les tests Unity ajoutés couvrent le catalogue, les prérequis, les courbes d'XP,
la séparation des progressions, les recettes atomiques, les stations et la
compatibilité des snapshots. **43 cas de règles pures réussissent hors Editor**,
dont les coûts à 0/100/200, les prochains seuils configurables, l'apprentissage
unique, les dotations par école et le refus d'XP pour une école inactive.
Les quatre assemblies `Arkya.Gameplay`, `Arkya.Characters.Runtime`,
`Arkya.Characters.Editor` et `Arkya.Characters.EditorTests` compilent hors
Editor avec Roslyn et les générateurs ECS/Netcode. Les assemblies de runtime
compilent également avec les symboles serveur ; cette variante utilise les
références Editor disponibles et ne remplace pas un vrai build dédié.

Des tests Unity supplémentaires couvrent les services des maîtres, la dotation
atomique, les rejets d'autorité et les trois vues de l'interface. Les fixtures
réseau couvrent deux clients de crafting, la récolte sans XP hors école et le
parcours maître → école → formule → outils. Leur exécution dans Unity reste à effectuer.
Le MCP Unity a échoué à son initialisation sur `127.0.0.1:8080` pendant cette
intervention. La compilation Editor, les tests Edit Mode/Play Mode, un vrai
parcours serveur/client et une capture du stone cutter ne sont donc pas
validés. Les contrôles hors Editor ne remplacent pas ces vérifications.

Il n'y a pas encore de file de fabrication, de durée de recette, de coût en
combustible, de construction de station par le joueur ni de publication du
catalogue artisanal dans l'Admin. Les futures recettes
de fonte, forge et charpenterie restent distinctes de cette première tranche.
