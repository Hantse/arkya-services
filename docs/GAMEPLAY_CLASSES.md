# Combat, classes et progression

Document de conception, commencé le 16 octobre 2026. Les règles confirmées ci-dessous expriment la direction du jeu ; elles ne sont pas encore implémentées. Les propositions et questions ouvertes ne constituent pas des décisions d'équilibrage.

## Direction confirmée

- Combat inspiré du ressenti de WoW décrit pour Arkya : sélectionner une cible, déclencher une capacité, tenir compte du placement et de l'orientation pour les actions qui l'exigent.
- Cinq classes : **Warrior, Scout, Mage, Cleric, Druid**.
- Un même personnage peut progresser dans plusieurs classes, avec **une seule classe active à la fois**.
- Certains sorts acquis dans une classe deviennent utilisables dans les autres classes.
- Le **rating représente le niveau global du personnage** : somme des niveaux de classe divisée par le nombre de classes débloquées. Il reste distinct du niveau de chaque classe.
- Le jeu comporte aussi des « skills » ; leur nature et leur progression restent à préciser.

Le but joueur est de construire son personnage en explorant plusieurs classes, puis de choisir une classe active et les capacités héritées qui complètent sa manière de jouer.

Le [catalogue initial de capacités pour les niveaux de classe 1 à 10](GAMEPLAY_STARTER_SKILLS.md), accepté le 16 octobre 2026, détaille les déblocages, les rangs héritables et les capacités réservées à chaque classe. Une [proposition de coûts, casts et cooldowns](GAMEPLAY_SKILL_COSTS.md) complète ce catalogue ; ses chiffres restent à tester et à valider.

## Identité des classes — direction du catalogue initial

Le catalogue initial accepté donne une première traduction de ces identités en capacités. Les spécialisations complètes, les droits d'équipement et les règles finales de ressources restent à concevoir.

| Classe | Identité proposée | Apport possible au groupe |
| --- | --- | --- |
| Warrior | Combat de proximité, résistance, maîtrise des armes | Tenir le front, protéger, interrompre |
| Scout | Mobilité, précision, repérage | Choisir les positions, atteindre une cible prioritaire |
| Mage | Magie offensive et contrôle | Infliger des dégâts, maîtriser l'espace |
| Cleric | Soins directs et protection | Restaurer la vie, sécuriser les alliés |
| Druid | Nature, soins dans la durée et entraves | Soutenir dans la durée, contrôler les déplacements |

La distinction entre Cleric et Druid doit se sentir dans leurs décisions de combat. Le catalogue inclut désormais Camouflage pour le Scout ; ses règles détaillées de détection, les transformations éventuelles du Druid et les spécialisations précises de tank, soigneur ou DPS restent ouvertes.

## Niveaux, skills et rating

| Notion | Sens retenu ou question ouverte |
| --- | --- |
| Niveau de classe | Progression propre à Warrior, Scout, Mage, Cleric ou Druid ; permet les déblocages de cette classe |
| Classe active | Classe actuellement jouée ; les conditions de changement restent à définir |
| Rang de sort | Version apprise d'un même sort, comme Heal R1 puis Heal R2 |
| Sort hérité | Rang rendu disponible hors de sa classe d'origine ; disponibilité, équipement et conditions de lancement sont des étapes distinctes |
| Skill | À préciser : capacités actives, passifs, maîtrises d'armes, compétences de métier, ou plusieurs de ces catégories |
| Rating global | Moyenne des niveaux des classes débloquées |

La distribution de l'expérience entre classes, les plafonds de niveau et l'éventuelle progression indépendante des skills ne sont pas encore fixés. Le rating ne remplace pas le niveau de classe pour apprendre un rang de sort. Aucun effet du rating sur les statistiques, l'accès au contenu ou les récompenses n'est décidé.

Formule confirmée :

```text
rating = somme des niveaux des classes débloquées / nombre de classes débloquées
```

Les classes non débloquées sont exclues du calcul. Débloquer une classe dont le niveau est inférieur à la moyenne fait baisser le rating, sans retirer de niveau aux classes déjà apprises. Cette conséquence est acceptée avec le choix de la formule ; le rating n'est donc pas une progression nécessairement croissante.

La présentation des fractions et les règles d'arrondi restent ouvertes. Avant le déblocage de la première classe, afficher un rating non défini plutôt qu'effectuer une division par zéro est la règle de travail proposée.

## Héritage des sorts

L'exemple donné pour **Heal du Cleric** est la référence confirmée :

| Progression Cleric | Quand Cleric est actif | Quand une autre classe est active |
| --- | --- | --- |
| Niveau 1 : apprentissage de Heal R1 | Heal R1 disponible | Heal R1 pas encore hérité |
| Niveau 5 : apprentissage de Heal R2 | Heal R2 disponible | Heal R1 devient disponible par héritage |

Ainsi, un joueur peut monter Cleric jusqu'au niveau 5, puis jouer Warrior avec l'accès à Heal R1. Cela ne lui donne pas Heal R2 en Warrior. Les seuils 1 et 5 viennent de l'exemple de conception ; ils ne constituent pas une courbe universelle pour tous les sorts.

**Règle appliquée au catalogue initial accepté :** pour un sort transmissible, apprendre R2 dans sa classe d'origine libère R1 pour les autres classes. Le personnage conserve cet acquis lorsqu'il change de classe. Généraliser cette relation aux futurs rangs reste à valider lors de leur conception.

Le catalogue initial précise quels sorts sont transmissibles ou réservés. Restent ouverts les futurs sorts et passifs, la sélection de tous les anciens rangs et le nombre de sorts hérités équipables simultanément. Avec la généralisation aux rangs futurs, le dernier rang resterait réservé à sa classe puisqu'aucun rang suivant ne le libère ; une exception éventuelle devrait être explicite. Un futur sort sans rang suivant demande également une règle dédiée.

Le nombre de cases montré dans les [maquettes du HUD](art-direction/ui/hud/README.md) ne détermine pas une limite de sorts hérités.

## Ciblage et lancement — base à préciser

Interprétation de travail de « quand c'est devant nous » : la direction du personnage compte pour les attaques concernées. Cela ne décide pas encore si une frappe touche uniquement la cible sélectionnée ou tous les ennemis dans un secteur frontal.

Proposition d'interaction : cliquer sélectionne la cible ; un raccourci ou une case de barre déclenche la capacité. La caméra, l'orientation automatique éventuelle et l'auto-attaque restent à définir. Chaque capacité précise sa cible admissible, sa portée, son orientation requise et sa ligne de vue ; un soin sur soi ne reçoit pas automatiquement les contraintes d'une frappe frontale.

Avant une implémentation, compléter le cycle suivant pour chaque famille de capacités :

| Étape | Règle de travail ou décision nécessaire |
| --- | --- |
| Demande | Le serveur vérifie le personnage, le droit d'utiliser ce rang, la cible, les ressources et les délais |
| Entrée en incantation | Définir les conditions de portée, d'orientation, de visibilité et de mouvement au démarrage |
| Incantation | Définir ce que mouvement, dégâts, contrôle, changement de cible ou annulation volontaire interrompent |
| Résolution | Définir les contrôles répétés à la fin ou à l'impact ; appliquer l'effet autoritaire une seule fois |
| Coût et récupération | Définir quand le coût et les délais sont engagés, puis leur traitement en cas d'annulation ou d'échec |
| Action suivante | Définir la file d'entrée éventuelle et les transitions autorisées après réussite ou interruption |

Cas à couvrir : cible morte ou disparue, obstacle apparu, cible passée derrière ou hors de portée, différence de hauteur, ressource devenue insuffisante, requête répétée, mort du lanceur et transfert d'area pendant l'action. Le moment exact des contrôles est une décision de gameplay, pas un détail à choisir implicitement dans le code.

## Points d'équilibrage à éprouver

- **Préserver les choix de classe.** Tester une sélection limitée de sorts hérités équipés ; aucun nombre d'emplacements n'est fixé ici. Un personnage qui a tout appris ne doit pas nécessairement tout utiliser simultanément.
- **Conserver un coût compréhensible.** Définir la ressource et les statistiques utilisées par un soin hérité sur Warrior. Son rang inférieur ne suffit pas à garantir qu'il reste moins efficace qu'un soin de Cleric.
- **Éviter un passage obligé.** Si Heal R1 devient indispensable à toute configuration, l'héritage perd son intérêt de personnalisation. Comparer des personnages avec et sans ce soin dans les mêmes rencontres.
- **Encadrer les changements.** Proposition : changer de classe ou de sorts équipés hors combat, en conservant les délais et effets en cours. Les restrictions exactes restent à décider ; le changement ne devrait pas réinitialiser les ressources ni permettre de répéter immédiatement une capacité.
- **Examiner les combinaisons.** Les soins, contrôles, déplacements et défenses hérités peuvent ensemble effacer les faiblesses d'une classe. Les tester en groupe comme en solo.

## Lecture de l'expérience et essais proposés

| Critère | Objectif et observation à faire |
| --- | --- |
| Clarté | Le grimoire distingue « appris en Cleric », « transmissible » et « équipé » ; le joueur comprend pourquoi Heal R1 devient disponible ailleurs |
| Motivation | Monter une autre classe ouvre un choix que le joueur souhaite essayer ; vérifier que cette progression ne devient pas une obligation identique pour tous |
| Réponse | La sélection et la demande de cast ont un retour immédiat ; un refus indique sa cause précise et les possibilités d'annulation sont lisibles |
| Satisfaction | Un soin réussi combine un retour visuel et sonore distinct d'une simple demande de lancement ; le passage à l'héritage est signalé dans le grimoire |
| Cohérence | Les animations, sons et effets gardent l'identité du sort d'origine tout en restant lisibles sur la classe active et dans la direction artistique peinte |

Scénarios de validation à exécuter sur le futur prototype :

1. **Découverte :** suivre l'exemple Cleric niveau 1 puis niveau 5, changer de classe et vérifier que le joueur anticipe quel rang sera utilisable. En cas d'incompréhension, revoir d'abord le grimoire et les messages de déblocage.
2. **Stress :** répéter les entrées, perdre la cible et franchir la portée pendant une incantation. Aucune demande ne doit produire plusieurs effets ni un coût incohérent ; corriger les transitions avant de régler les dégâts.
3. **Maîtrise :** comparer une bonne orientation et un bon moment de soin à un mauvais placement. Les choix doivent changer l'issue de façon explicable ; revoir les conditions et retours si le joueur ne perçoit aucune différence.
4. **Abus :** changer de classe ou de configuration pour tenter de récupérer un délai, puis tester l'accumulation de contrôles et de soins hérités. Revoir les restrictions ou les synergies si une configuration supprime les arbitrages.
5. **Lisibilité :** faire observer le combat par un autre joueur ; il doit distinguer cible, incantation, interruption et réussite. Ajuster les signaux avant les valeurs de puissance.

Ces essais sont préparés, pas exécutés. La première tranche jouable proposée est le parcours de ciblage/cast avec Warrior et Cleric, puis l'héritage de Heal R1 ; ce périmètre reste à valider avant développement.

## Intégration future

Le serveur Unity décide de la progression, des déblocages, des changements de classe et des effets des capacités. Characters conserve la progression persistante via des mutations de confiance du serveur ; le client exprime des intentions. Les règles d'autorité et de transfert interarea suivent l'[architecture](ARCHITECTURE.md).

La [base d'inventaire et de récolte](GAMEPLAY_FOUNDATION.md) et le combat du prototype restent les implémentations existantes. Ce document n'ajoute ni composant ECS, ni contrat réseau, ni champ de persistance ; leur conception devra reprendre les décisions de gameplay une fois précisées.
