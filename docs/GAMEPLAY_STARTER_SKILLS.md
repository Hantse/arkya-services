# Premières capacités — catalogue initial pour les niveaux de classe 1 à 10

Catalogue accepté le 16 octobre 2026 : noms, effets de principe, niveaux et possibilités d'héritage servent de base au premier essai. L'exemple **Heal R1 au niveau 1, Heal R2 au niveau 5, puis Heal R1 héritable** est conservé. L'équilibrage reste à éprouver en jeu. Aucune capacité de ce catalogue n'est implémentée par ce document.

La [proposition de coûts, casts et cooldowns](GAMEPLAY_SKILL_COSTS.md) chiffre les rangs natifs et hérités, les réserves et leur récupération. Ces nouveaux chiffres restent des valeurs de départ à tester ; ils ne sont pas encore des règles approuvées.

Les règles approuvées de classe active unique et de rating sont dans [Combat, classes et progression](GAMEPLAY_CLASSES.md). Tous les niveaux de cette proposition sont ceux de la **classe d'origine**, jamais le rating global.

## Périmètre et rythme proposés

Hypothèse : « premiers skills » désigne ici les capacités actives de combat. Les maîtrises d'armes, passifs et métiers auront leur propre proposition ; ce choix ne décide pas de leur progression.

**Valeurs de départ pour essai :** une tranche de niveaux 1 à 10, avec des capacités initiales au niveau 1, de nouvelles capacités aux niveaux 3, 6 et 10, et des améliorations de rang aux niveaux 5 et 10. Chaque classe commence avec une attaque et une capacité de défense ou de soin. Le premier héritage au niveau 5 permet de découvrir le système pendant le parcours de l'[île de départ, niveaux 1 à 6](world/STARTER_ISLAND.md).

Le choix de deux capacités au départ vise un apprentissage simple ; cinq capacités distinctes par classe dans cette tranche suffisent à comparer les identités sans fixer la taille du grimoire final. Un rang supérieur améliore une capacité existante et remplace par défaut son rang inférieur dans la barre ; il ne crée pas un bouton obligatoire supplémentaire.

Le test des seuils et la direction des ajustements sont décrits plus bas. Les dégâts et portées restent à chiffrer ; les coûts, temps de lancement et délais ont désormais une proposition séparée, liée en tête de ce document.

## Lecture de l'héritage

- Pour une capacité marquée transmissible, apprendre R2 débloque l'utilisation de R1 dans les autres classes, même celles débloquées plus tard.
- « R1 au niveau 5 » signifie : atteindre le niveau 5 dans la classe d'origine **et apprendre son R2**. Le niveau de la classe destinataire ne change pas ce déblocage dans cette proposition.
- R2 reste utilisable uniquement dans sa classe d'origine sur cette tranche. Un futur R3 pourrait libérer R2 ; aucun seuil de R3 n'est proposé ici.
- « Réservé » signifie que cette capacité demande la classe d'origine active, même si un rang supérieur est ajouté plus tard. Ces exclusions font partie du catalogue initial accepté et préservent les rôles.
- Hériter d'une capacité ne donne ni maîtrise d'arme, ni droit d'équipement, ni exemption de coût. Le nombre d'emplacements d'héritage équipables reste ouvert.

## Warrior — tenir le front et combattre au contact

| Capacité | R1 | R2 | Effet proposé | Disponible dans les autres classes |
| --- | --- | --- | --- | --- |
| Frappe puissante | 1 | 5 | Coup renforcé sur la cible devant soi, avec une arme de mêlée | R1 dès Warrior 5 et apprentissage de R2 |
| Garde | 1 | 5 | Réduit les dégâts physiques venant de face ; attaquer ou lancer un sort met fin à la garde | R1 dès Warrior 5 et apprentissage de R2 |
| Provocation | 3 | — | Force temporairement un ennemi non joueur à se concentrer sur le Warrior | Réservé au Warrior |
| Coup entravant | 6 | 10 | Frappe de mêlée qui ralentit la cible | R1 dès Warrior 10 et apprentissage de R2 |
| Charge | 10 | — | Rejoint rapidement une cible ennemie par un trajet praticable ; sans étourdissement ajouté | Réservé au Warrior |

Garde nécessite une arme de mêlée, sans imposer un bouclier. Provocation concerne l'aggro des ennemis non joueurs et ne force pas les commandes d'un autre joueur. Charge ne traverse pas les obstacles et ne libère pas d'une immobilisation.

## Scout — distance et repositionnement

| Capacité | R1 | R2 | Effet proposé | Disponible dans les autres classes |
| --- | --- | --- | --- | --- |
| Tir précis | 1 | 5 | Tir préparé sur une cible devant soi ; demande de rester immobile et une arme à distance compatible | R1 dès Scout 5 et apprentissage de R2 |
| Pas léger | 1 | 5 | Accélération personnelle temporaire, utilisable en combat ; sans invulnérabilité ni suppression d'entrave | R1 dès Scout 5 et apprentissage de R2 |
| Marque de traque | 3 | — | Désigne une proie ; le prochain Tir précis du Scout contre elle reçoit un bonus et consomme la marque | Réservé au Scout |
| Tir entravant | 6 | 10 | Tir qui ralentit une cible devant soi ; demande une arme à distance compatible | R1 dès Scout 10 et apprentissage de R2 |
| Camouflage | 10 | — | Permet de se dissimuler hors combat ; une action offensive, un soin ou des dégâts reçus le rompent | Réservé au Scout |

La marque profite seulement au Scout qui l'a posée ; elle n'ajoute pas un bonus universel de dégâts pour le groupe. Le camouflage ne permet pas d'effacer un combat en cours. Son fonctionnement exact de détection reste à concevoir. Les droits d'utilisation des armes à distance sont un prérequis à décider avant de rendre les tirs hérités équipables hors Scout.

## Mage — dégâts magiques et contrôle des distances

| Capacité | R1 | R2 | Effet proposé | Disponible dans les autres classes |
| --- | --- | --- | --- | --- |
| Projectile arcanique | 1 | 5 | Incantation infligeant des dégâts magiques directs à la cible devant soi | R1 dès Mage 5 et apprentissage de R2 |
| Barrière arcanique | 1 | 5 | Bouclier personnel absorbant une quantité limitée de dégâts magiques | R1 dès Mage 5 et apprentissage de R2 |
| Vague de flammes | 3 | — | Incantation touchant les ennemis dans un cône devant le Mage | Réservé au Mage |
| Éclat de givre | 6 | 10 | Impact magique sur une cible devant soi, accompagné d'un ralentissement | R1 dès Mage 10 et apprentissage de R2 |
| Téléportation courte | 10 | — | Repositionnement dans la direction du personnage vers une destination valide, sans traverser un obstacle | Réservé au Mage |

La barrière ne protège pas des dégâts physiques. La téléportation n'ajoute ni invulnérabilité ni dissipation de contrôle ; l'immobilisation empêche son utilisation dans cette proposition. Son identité de mobilité reste propre au Mage même si un rang inférieur était créé.

## Cleric — soins directs et protection des alliés

| Capacité | R1 | R2 | Effet proposé | Disponible dans les autres classes |
| --- | --- | --- | --- | --- |
| Soin / Heal | 1 | 5 | Incantation restaurant directement la vie d'un allié ou de soi-même | R1 dès Cleric 5 et apprentissage de R2 — règle confirmée |
| Trait sacré | 1 | 5 | Incantation offensive sur une cible ennemie devant soi ; permet de progresser seul | R1 dès Cleric 5 et apprentissage de R2 |
| Égide sacrée | 3 | — | Bouclier absorbant des dégâts physiques ou magiques sur un allié ou soi-même | Réservé au Cleric |
| Dissipation | 6 | 10 | Retire une malédiction dissipable sur un allié ou soi-même ; aucun effet sur les poisons ou les blessures physiques | R1 dès Cleric 10 et apprentissage de R2 |
| Prière de groupe | 10 | — | Canalisation soignant progressivement les membres du groupe proches du Cleric ; empêche de lancer un autre sort simultanément | Réservé au Cleric |

Heal reste un soin incanté même lorsqu'il est hérité. Garde ne peut donc pas être maintenue pendant Heal. Dissipation conserve la même catégorie d'effets admissibles aux deux rangs ; R2 améliore son coût, sans lui donner une nouvelle famille de contrôle à supprimer. La résurrection est laissée pour une proposition ultérieure.

## Druid — soins dans la durée et entraves naturelles

| Capacité | R1 | R2 | Effet proposé | Disponible dans les autres classes |
| --- | --- | --- | --- | --- |
| Ronces | 1 | 5 | Incantation produisant un impact naturel puis des dégâts progressifs sur une cible ennemie devant soi | R1 dès Druid 5 et apprentissage de R2 |
| Renouveau | 1 | 5 | Soin progressif sur un allié ou soi-même, sans soin immédiat au lancement | R1 dès Druid 5 et apprentissage de R2 |
| Enracinement | 3 | — | Immobilise une cible ennemie ; de nouveaux dégâts subis rompent l'effet | Réservé au Druid |
| Peau d'écorce | 6 | 10 | Réduit temporairement les dégâts physiques subis par le lanceur ; permet de continuer à agir | R1 dès Druid 10 et apprentissage de R2 |
| Floraison | 10 | — | Termine les Renouveaux du Druid sur les membres du groupe proches et convertit leurs soins restants en soins immédiats | Réservé au Druid |

Ronces et Enracinement créent un arbitrage : les dégâts progressifs de Ronces rompent aussi les racines. Floraison consomme les effets du lanceur seulement, sans reprendre ceux d'un autre Druid ; elle ne duplique pas les soins encore à venir. Renouveau hérité n'accorde pas Floraison à une autre classe. Les formes animales restent un axe possible ultérieur, sans kit imposé dans cette première proposition.

## Comportements communs proposés

Ces règles rendent le catalogue comparable ; elles restent des hypothèses de prototype, pas des contrats déjà implémentés.

- **Ressources :** toutes les classes auraient accès à une réserve de stamina et une réserve de mana. Warrior et Scout utilisent la stamina ; Mage, Cleric et Druid utilisent le mana. Une capacité héritée garde sa ressource d'origine. La proposition chiffrée détaille les réserves, la récupération et le surcoût d'héritage ; les coefficients de puissance restent à définir.
- **Rangs :** R2 améliore l'efficacité chiffrée de R1 — puissance, absorption ou coût selon la capacité. Le ralentissement ou la mobilité d'un petit rang peuvent rester très utiles : un rang inférieur n'est pas, à lui seul, une garantie d'équilibrage. Les capacités réservées n'ont pas de rang supérieur défini dans cette tranche.
- **Entrée :** le lanceur doit être vivant, posséder et avoir équipé le rang, disposer de la ressource et ne pas être bloqué par un contrôle ou un délai. Une cible doit être valide, à portée et visible ; les attaques frontales contrôlent aussi l'orientation. Les soins et protections sur alliés ne demandent pas de leur faire face.
- **Incantation :** déplacement, annulation volontaire, mort et contrôle interrompant empêchent la résolution. Subir des dégâts ordinaires n'interrompt pas automatiquement. La cible choisie au départ reste celle de l'action même si la sélection UI change ; sa validité et les conditions spatiales sont contrôlées de nouveau à la résolution. Tir précis est également une action incantée. La grille chiffrée liée en tête de document précise les casts proposés ; Garde est maintenue et Prière est canalisée.
- **Coûts et sorties :** pour une action instantanée ou incantée, coût et délai sont engagés à la résolution valide. Un échec avant résolution ne produit pas d'effet. Garde engage son coût et son délai à l'entrée ; Prière les engage au premier soin puis dépense sa ressource au fil de la canalisation. Annuler après un effet ne rend pas le coût déjà engagé. Une ressource insuffisante met fin à la canalisation avant l'effet suivant.
- **Enchaînement :** une demande répétée ne crée pas une nouvelle résolution. Une nouvelle capacité demande de terminer ou d'annuler l'incantation ou la canalisation précédente ; lancer une capacité termine Garde. La proposition chiffrée précise une file d'entrée et un délai global commun à tester.
- **Effets persistants :** un soin ou dégât progressif déjà appliqué ne demande pas de rester face à la cible. Une même famille d'effets du même lanceur est renouvelée ou remplacée, sans empiler ses rangs. Les ralentissements ne s'additionnent pas : seul le plus fort agit. Les cumuls entre plusieurs lanceurs demandent encore une règle.
- **Changement de classe :** proposition de changement hors combat seulement, en préservant les délais et sans régénération gratuite. Les rangs d'une même capacité partagent leur délai. L'héritage d'une capacité ne recopie pas le mécanisme exclusif qui l'accompagne dans sa classe.

Les distances, angles, durées des effets ordinaires, mouvements sur terrain, immunités de certaines cibles, interactions entre contrôles, persistance des effets au changement de classe et résolution interarea restent nécessaires avant développement. Les coûts et délais proposés doivent être éprouvés. Le serveur conserve l'autorité prévue par l'[architecture](ARCHITECTURE.md).

## Exemples de choix obtenus

- **Warrior avec Cleric 5 :** Heal R1 donne un soin de secours, mais demande mana et incantation ; Garde se termine au lancement.
- **Mage avec Scout 5 :** Pas léger R1 aide à se replacer sans donner Camouflage ni les bonus de Marque de traque.
- **Scout avec Druid 5 :** Renouveau R1 permet d'anticiper les dégâts, sans recevoir Floraison.
- **Cleric avec Mage 5 :** Barrière arcanique R1 ajoute une défense personnelle contre la magie ; sa concurrence avec Égide doit être testée.
- **Druid avec Warrior 5 :** Garde R1 donne une option défensive frontale avec une arme compatible ; l'utiliser empêche de continuer à lancer des soins.

Ces exemples montrent des possibilités, sans approuver le nombre de capacités héritées simultanées ni affirmer qu'elles sont équilibrées.

## Essais des valeurs de départ et de l'expérience

Ces essais sont préparés, pas exécutés. Les seuils de Heal déjà confirmés restent la référence ; les autres niveaux et la densité de capacités sont des valeurs de départ ajustables.

| Essai | Critère de réussite ou d'échec | Ajustement si échec |
| --- | --- | --- |
| Découverte des niveaux 1 à 6 avec chaque classe, sans héritage | Le joueur progresse seul et sait expliquer l'usage de ses capacités avant le prochain déblocage ; blocage de progression ou bouton incompris = échec | Ajuster la puissance et les retours ; déplacer un ajout proposé plus tôt s'il manque un choix, plus tard si les précédents restent incompris |
| Passage au niveau 5 et changement de classe | Le joueur identifie R2 dans la classe d'origine, R1 hors classe et l'absence des capacités réservées ; confusion sur le rang ou le rating = échec | Revoir grimoire, message de déblocage et présentation des rangs avant les seuils |
| Progression proposée jusqu'au niveau 10 | Les nouveaux boutons ont une utilité identifiable ; un ajout jamais utilisé ou une longue portion sans décision nouvelle = échec | Revoir la fonction ou déplacer les seuils proposés 3, 6 et 10 ; ne pas augmenter automatiquement le nombre de boutons |
| Comparaison Heal, Renouveau et défenses héritées | Les variantes ont des avantages selon la rencontre ; une combinaison qui domine toutes les situations ou rend le soin hérité indispensable = échec | Ajuster coûts, rendement, contraintes de lancement ou sélection d'héritages avant de généraliser |
| Spam, annulation, changement de cible et ressource épuisée | Chaque effet correspond à une résolution valide et à son coût ; duplication ou soins gratuits = échec | Corriger les transitions et coûts avant toute retouche de puissance |
| Placement et maîtrise | Faire face, anticiper un soin ou choisir entre Ronces et Enracinement change l'issue de façon compréhensible | Revoir conditions et retours si la différence n'est pas perceptible |
| Observation d'un combat | Un observateur distingue lancement, interruption, absorption et soin réellement appliqué | Associer une animation ou un effet lisible et un son distinct à la réussite ; alléger les effets si les actions se confondent |

La réponse aux entrées et la clarté passent avant le réglage des valeurs. La motivation est le nouveau choix obtenu par l'héritage ; la satisfaction vient de son effet lisible en combat. Les animations, sons et effets doivent garder l'identité de la classe d'origine et suivre la direction artistique peinte d'Arkya.
