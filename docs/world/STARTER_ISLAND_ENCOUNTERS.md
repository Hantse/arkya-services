# Rencontres de l’Île de l’Aube

Les archives fournies le 23 octobre 2026 (`Assets/free_wild_wolf.zip` et
`Assets/szombie.zip`) servent à trois zones de combat sur StarterIsland.
Le premier modèle représente un **loup-garou bipède**, et non un loup quadrupède.
Les originaux sont conservés ; les adaptations Unity sont séparées sous
`Assets/Arkya/Creatures/` et le zombie fournisseur sous
`Assets/ThirdParty/QuestCreatures/`.

Le loup-garou original compte 1 499 612 triangles et n’a pas de squelette. Sa
surface a été consolidée et réduite à 17 950 triangles, avec un rig de 20 os,
des poids lissés limités à quatre influences et des textures 2K rebakées. La
source Blender éditable, la provenance et les scripts sont conservés dans
[QuestCreaturesSources](../../Assets/QuestCreaturesSources/README.md). Le zombie
réutilise son squelette fournisseur et son mesh de 8 284 triangles.

## Jouer

Depuis l’exploration de l’île, suivre les directions et distances du suivi de
quêtes. `Tab` sélectionne un ennemi proche ; `F` ou `1` demande une attaque au
contact. Le cercle au sol et les barres de vie identifient la cible. Le serveur
refuse les attaques hors de portée, à travers un obstacle, trop rapprochées,
répétées avec la même séquence ou émises par un joueur mort.

| Zone | Centre X / Z | Population | Objectif |
| --- | --- | --- | --- |
| Lisière des Crocs | 446 / 438 | 4 loups-garous | Sécuriser la lisière : 3 éliminations |
| Guet oublié | 390 / 590 | 4 revenants | Faire taire les revenants : 3 éliminations |
| Creux des Murmures | 636 / 790 | 2 loups-garous, 2 revenants | Repousser la meute corrompue : 3 éliminations |

Les points d’apparition suivent la hauteur du terrain et recherchent un
emplacement libre à proximité. Les centres se trouvent sur sol sec, avec des
pentes mesurées de 12,16°, 1,00° et 12,38° dans le Terrain de l’île.

## Simulation et présentation

`CharacterEncounterServerSystem` possède les ennemis, les cibles, les points de
vie, les temporisations et les crédits de quête. Les ennemis patrouillent autour
de leur point d’origine, détectent un joueur vivant, le poursuivent, préparent
un coup puis infligent des dégâts au contact. Une cible trop éloignée, déconnectée
ou en transfert d’autorité entraîne le retour au camp. Le retour restaure la vie
du monstre. Les déplacements vérifient le relief, l’emprise de la partition et
les collisions statiques ; il s’agit d’une navigation locale, sans recherche de
chemin globale autour des bâtiments.

Le loup-garou poursuit plus rapidement ; le zombie est plus lent et frappe plus
fort. La portée joueur est de 3,2 m, avec un délai de 0,7 seconde et 25 dégâts.
Les ennemis réapparaissent après 24 secondes. Chaque transition vivant → mort
donne 20 points d’expérience au joueur qui porte le dernier coup. Les objectifs
sont plafonnés à trois morts, sans bonus de complétion rejouable. Le joueur
récupère progressivement hors combat et revient en vie après six secondes,
à un point sûr de son autorité courante lorsque ce point est disponible.

Les ghosts interpolés transmettent position, orientation, espèce, vie, état
d’animation et séquence d’action. Les compteurs de quête sont répliqués seulement
au propriétaire du personnage. Aucun RPC client ne transmet de dégâts, de
récompense ou de compteur de morts.

`CharacterEncounterPresentation` observe ces ghosts, instancie les prefabs et
met à jour le HUD sans remplacer les statistiques, les annonces ni le chat.
`CharacterEncounterPose` anime les vrais os : respiration, marche, poursuite,
préparation, frappe et chute. Ces poses sont créées pour Arkya : aucune animation
de locomotion ou de combat utilisable n’était fournie dans les archives.

## Portée de cette première intégration

Les rencontres fonctionnent dans le parcours local client/serveur et sont
enregistrées dans les mondes dédiés de StarterIsland. Chaque point d’apparition
est créé uniquement par sa partition propriétaire. Les monstres ne traversent
pas les frontières d’autorité ; leur visibilité et leurs impacts interpartition
restent à intégrer.

Ces trois objectifs sont intégrés au contenu Unity et acceptés automatiquement.
Ils ne sont pas publiés dans le catalogue Content existant. Leurs trois compteurs
bornés font partie de `CharacterGameplayState` : les snapshots de sauvegarde et
de transfert existants les conservent avec l’expérience et les points de vie.
Les snapshots antérieurs, sans ces champs, démarrent à zéro. L’essai local sans
compte repart à zéro à chaque session. Il n’y a pas de distribution de loot ni de
PNJ donneur de quête dans cette tranche.

Les deux archives ne contiennent pas de fichier de licence : aucune licence
supplémentaire n’est attribuée aux modèles dans ce dépôt.

## Validation

Validation dans Unity 6000.6.6f1 le 23 octobre 2026 : **27 tests ciblés réussis**.

- 14 cas couvrent la portée, les RPC rejoués, les obstacles, les apparitions par
  autorité, les attaques, le retour au camp et la récupération après la mort.
- 8 cas couvrent la sauvegarde des compteurs, les anciens snapshots et leurs bornes.
- 4 cas couvrent les poses, le transfert de scène et la préservation du HUD.
- 1 parcours Netcode ouvre un vrai serveur et un client locaux : apparition du
  ghost, dégâts, rejet d’une séquence rejouée, mort, compteur propriétaire et XP.

Les 26 premiers cas sont passés dans le lot
`859c9b7179b74706ae03b98b85faad7d`. Le parcours réseau a ensuite été rejoué avec
succès dans le lot `f65a5c2aee5541c98ce775ccceed291c`, après une compilation
complète `CleanBuildCache`. La sauvegarde MongoDB
et le combat entre plusieurs processus dédiés ne sont pas validés par ce lot.

La revue des assets confirme leurs `.meta`, références de matériaux, textures
URP et squelettes. Le budget des douze ennemis est d’environ 157 404 triangles
s’ils sont tous visibles, avec un renderer et un matériau par créature.

Le parcours réel de StarterIsland a ensuite été lancé depuis CharacterCreation.
Douze ennemis ont été observés dans les deux mondes Netcode, avec patrouille,
poursuite et attaques animées. Les déplacements entre camps ont été accélérés
par l’instrumentation Editor. Deux attaques du joueur ont tué le loup-garou
203 : le client a reçu `EncounterHollowKills = 1` et `WarriorExperience = 20`.
Les ennemis ont aussi effectivement diminué les points de vie du joueur.

L’adaptation Unity du loup-garou a été ajustée à 2,2 m en mesurant le maillage
skinné, avec sa base au niveau du sol. Les captures finales montrent cet ajustement :

- [Lisière des Crocs](encounters-2026-10-23/wolves-combat.png)
- [Guet oublié](encounters-2026-10-23/revenants-combat.png)
- [Creux des Murmures](encounters-2026-10-23/mixed-camp.png)
- [Mort du monstre et crédit de quête](encounters-2026-10-23/combat-reward.png)

Les captures ont produit des avertissements Netcode de regroupement de ticks
dans l’Editor ; ce contrôle ne constitue pas une mesure de performance d’un
serveur dédié. L’Editor a été laissé hors Play, sur CharacterCreation propre.
