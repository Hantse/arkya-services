# Statistiques et capacités du personnage

Première tranche implémentée le 23 octobre 2026 dans le projet Unity actif.
Elle reprend les cinq classes et les seuils du [catalogue accepté](GAMEPLAY_STARTER_SKILLS.md),
avec les [règles de rating et d’héritage](GAMEPLAY_CLASSES.md).
Les [écoles d'artisanat](CRAFTING.md), ajoutées séparément, possèdent leurs
propres niveaux, compétences et XP.

Le catalogue de combat décrit ici contient des **abilities** (capacités individuelles, comme le sort **Thunder**), distinctes des **skills** (compétences comme **Nature**). Les noms techniques historiques `CharacterSkillId`, `CharacterSkillCatalog` et `HudSkillView` désignent encore les abilities de ce catalogue. Les skills de combat tels que Nature ne sont pas encore modélisés dans cette tranche ; Thunder n'est pas ajouté au catalogue par cette clarification.

## État et autorité

`CharacterGameplayState` est la source de vérité de l’acteur d’exploration :
vie, mana, endurance, classe active, niveaux/XP de chaque classe débloquée
et rangs appris. Ce composant ECS est envoyé uniquement à son propriétaire.
Les acteurs indépendants de récolte et le prototype d’areas conservent leurs
propres composants ; ils ne sont pas ajoutés au même acteur.

Le serveur initialise un profil absent avec Warrior, les deux capacités
initiales R1 et trois réserves pleines de 100 points. Le niveau historique est
repris entre 1 et 10. Ce choix initial, les réserves et la courbe d’XP sont des
réglages de prototype, pas un équilibrage définitif. Il n’y a pas encore de
choix de classe à la création.

`CharacterProgressionSystem` traite une boîte `CharacterProgressionRequest`
activable uniquement par un adaptateur serveur de confiance : attribuer de
l’XP à la classe active, débloquer une classe, activer une classe débloquée ou
apprendre un rang admissible. Aucune RPC cliente ne permet d’attribuer de l’XP
ou des statistiques. Les acteurs gelés pour un transfert ne traitent aucune
demande ; un personnage mort ne peut pas progresser par cette boîte.
L’adaptateur doit vérifier les conditions de gameplay avant de soumettre une
demande, notamment les restrictions futures de changement de classe.

L’XP appartient au niveau courant d’une classe. La courbe de prototype demande
100, 200, …, 900 XP pour atteindre le niveau suivant et s’arrête à 10. Les
excédents traversent plusieurs niveaux ; au plafond, l’XP est zéro. Une montée
de niveau ne remplit pas les réserves et n’apprend pas automatiquement de rang.
Le domaine accepte une autre `CharacterExperienceCurve` ; changer la courbe
du runtime ou son plafond exige aussi de migrer/valider les profils persistants.
Le rating est la moyenne des niveaux des seules classes débloquées.

La récolte et la fabrication font progresser `CharacterCraftState` : Mineur,
Forgeron, Bûcheron et Charpentier. Ces actions ne donnent aucune XP aux classes
de combat et ne modifient pas leur rating. Les compétences artisanales suivent
automatiquement le niveau de leur école ; l'apprentissage explicite et
l'héritage des rangs décrits ici restent propres au catalogue de combat.

Le catalogue contient 25 abilities et 40 rangs. Atteindre un seuil autorise
l’apprentissage ; le rang doit ensuite être appris explicitement. Apprendre
R2 d’une capacité transmissible rend son R1 disponible dans les autres
classes. Les capacités réservées restent natives. La disponibilité indique
un acquis, pas un équipement ni une autorisation complète de lancer un sort.

## HUD et fiche personnage

Les jauges étaient vides parce que le HUD recevait uniquement le nom et le
niveau. `CharacterLocalPlaytest` lit désormais le ghost du propriétaire,
vérifie son identité dans le parcours distant et actualise la présentation
lorsque ses statistiques changent, au plus cinq fois par seconde. Il ne lit
pas le monde serveur pour fabriquer un affichage client.

La vie et l’endurance alimentent les jauges existantes. L’XP affiche son
pourcentage et son compte en infobulle ; 0/100 est une progression connue à
zéro, alors qu’un tiret indique une donnée absente. `C` ou **Personnage · C**
ouvre la fiche en lecture seule : vie/mana/endurance, classe, rating, niveaux
et catalogue des abilities avec rang appris/hérité ou seuil requis.
Les mises à jour de statistiques préservent les annonces, quêtes et barres
d’actions déjà présentes.

Les écoles d'artisanat disposent de leur propre panneau **Métiers · K**,
avec leurs quatre niveaux/XP et leurs huit compétences. **V** ouvre les
recettes près du stone cutter ; voir les [commandes d'artisanat](CRAFTING.md).

## Persistance et compatibilité

Les sauvegardes périodiques, la sortie et les deux captures de handoff incluent
les réserves et la progression. L’enveloppe backend reste `schemaVersion: 1` ;
le JSON de simulation passe à `version: 2` et contient un bloc `gameplay`
versionné. Un ancien snapshot de mouvement `version: 1`, sans bloc de stats,
reçoit le profil initial lors de sa première lecture. Un bloc présent mais
invalide est refusé, sans remplissage silencieux des réserves.

La première tranche de statistiques utilisait la paire **2 / `arkya-character-stats-v1`**.
Le build actif avec écoles, maîtres et formules utilise désormais **7 / `arkya-craft-schools-v1`** ;
la simulation reste au format 3 introduit pour la plongée et possède un bloc
`craft` versionné, indépendant du bloc `gameplay`.
Les services reconnaissent aussi les anciennes paires ; ils exigent une
correspondance exacte entre le client et la destination. Les profils de
déploiement existants ne sont pas migrés automatiquement : client et dedicated
doivent être reconstruits ensemble et le profil du realm doit déclarer le
nouveau build avant son admission. Aucun déploiement n’est effectué ici.
Les champs historiques `Character.level` et `CharacterRemoteIdentity.Level`
ne deviennent pas propriétaires de la progression ; le HUD lit le niveau de
classe répliqué. Une future synthèse pour les listes de personnages reste à
brancher côté Characters.

## Limites de cette tranche

Il n’y a pas encore d’attribution d’XP par combat/quêtes, de dépense ou
régénération des réserves, de lancement de capacités, d’effets de combat,
de choix de rangs équipés ou de mutation depuis la fiche. Les chiffres de
[coûts et récupération](GAMEPLAY_SKILL_COSTS.md) restent des propositions.
Les caractéristiques offensives/défensives et leurs coefficients restent à
définir. Aucun bouton de sort ne simule un effet local.

## Vérification

Les tests couvrent les seuils d’XP, le plafond, les profils invalides,
l’apprentissage et l’héritage, les snapshots et la présentation des jauges.
Les tests backend vérifient les paires de protocole, la conservation du JSON
à la reconnexion et le refus d’une ancienne autorité.

Lors de cette intervention, deux découvertes MCP ont retourné
`instance_count: 0`. Les contrôles hors Editor sont consignés dans le compte
rendu ; ils ne remplacent pas les tests réseau serveur/client, les tests
Editor/Play Mode ni une capture réelle du HUD. Ces derniers restent à exécuter
lorsque l’Editor est reconnecté.
