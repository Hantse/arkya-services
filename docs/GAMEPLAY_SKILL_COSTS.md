# Coûts et rythme des premières capacités

Proposition du 16 octobre 2026. Le [catalogue des premières capacités](GAMEPLAY_STARTER_SKILLS.md), ses niveaux et ses possibilités d'héritage ont été acceptés. **Tous les nouveaux chiffres de ce document sont des valeurs de départ pour essai**, pas un équilibrage validé. Les essais, leurs critères et les ajustements sont décrits en fin de document. Aucun code Unity ni backend n'est modifié.

## Réserves et récupération de référence

Pour isoler les coûts dans la tranche de niveaux de classe 1 à 10, chaque personnage possède **100 mana et 100 stamina**, quelle que soit sa classe active. Les coûts sont des points fixes, pas des pourcentages du maximum. Les réserves ne changent ni avec le rating, ni avec le nombre de classes débloquées. Les augmentations par équipement ou statistiques ne sont pas incluses dans cet essai.

| Paramètre | Mana | Stamina |
| --- | --- | --- |
| Réserve maximale de départ | 100 | 100 |
| Récupération normale, y compris en combat | 1 point/s | 5 points/s |
| Récupération renforcée | 5 points/s | 10 points/s |

La récupération renforcée commence après **5 secondes continues hors combat**. Le serveur doit avoir confirmé la sortie de combat : cesser d'appuyer sur les touches ne suffit pas tant qu'un ennemi reste engagé. Soigner ou protéger un allié engagé fait participer au combat, y compris via un effet progressif. Pendant l'attente de 5 secondes, la récupération normale continue. Les taux renforcés remplacent les taux normaux ; ils ne s'y ajoutent pas.

La récupération fonctionne pendant une incantation, une garde ou une canalisation, sans dépasser le maximum. Le déplacement ordinaire ne dépense pas de stamina dans cet essai ; le sprint n'est pas chiffré ici. Les valeurs courantes et les délais persistent au changement de classe, sans remplissage gratuit.

Ce modèle met les attaques répétables à un coût réduit : **6 mana** pour les attaques magiques de départ et **12 stamina** pour Frappe puissante et Tir précis. Les autres actions demandent d'arbitrer entre attaque, défense et soutien. Il faut vérifier ces budgets avec les futurs dégâts et rencontres ; aucune attaque gratuite supplémentaire n'est introduite par cette grille.

## Héritage et lecture des tableaux

**M = mana ; S = stamina.** La colonne native donne les coûts R1 puis R2. Une capacité réservée n'a qu'un rang dans ce catalogue.

```text
coût d'un R1 hérité = plafond(coût de son R1 natif × 1,25)
```

Le surcoût proposé est **+25 %, arrondi au point supérieur à chaque paiement défini**. Ainsi, 6 M deviennent 8 M, et 16 M deviennent 20 M. L'arrondi peut rendre le pourcentage effectif supérieur à 25 % sur un petit coût ; les tableaux affichent les points réellement payés.

Le R1 hérité conserve **la ressource, l'effet de son R1, le cast, le cooldown et les prérequis** du sort d'origine. Il ne reçoit pas un deuxième malus de puissance ou de vitesse. Pour cette grille, l'effet de base du rang ne varie pas avec la classe qui l'emploie ; les coefficients de statistiques restent à concevoir. R2 améliore l'effet au même coût, sauf Dissipation dont R2 réduit le coût. Il reste réservé à la classe d'origine sur les niveaux 1 à 10.

Les seuils d'héritage restent ceux du catalogue : les capacités initiales se transmettent en R1 après apprentissage de leur R2 au niveau 5 de la classe d'origine ; les capacités apprises au niveau 6 se transmettent après leur R2 au niveau 10. Le rating ne débloque aucun rang.

## Délai global et paiement

Toutes les capacités partagent un **délai global de 1,5 seconde**, même si elles dépensent des ressources différentes. Il démarre à l'acceptation de l'action après les contrôles initiaux, et court pendant le cast ou la canalisation. Un cast de 2 secondes avec un cooldown propre nul permet donc de commencer le suivant dès sa fin ; il n'ajoute pas 1,5 seconde d'attente après le soin. Un cooldown propre de **0 s** ne permet pas de contourner le délai global.

- **Instantané :** coût et cooldown propre engagés à la résolution immédiate valide. « Instantané » décrit le déclenchement ; une charge ou une téléportation garde ses contraintes de déplacement.
- **Incanté :** ressource vérifiée au départ puis à la résolution ; paiement et cooldown propre seulement à la résolution valide. Déplacement, annulation ou contrôle interrompant empêchent l'effet. L'annulation conserve le temps restant du délai global.
- **Maintien ou canalisation :** voir les règles particulières de Garde et de Prière ci-dessous. Désactiver un maintien ne déclenche pas un nouveau délai global.
- **Délais partagés :** R1 natif, R1 hérité et R2 d'une même capacité partagent leur cooldown. Changer de classe, de barre ou de rang ne le réinitialise pas. Il reste commun même si son coût diffère.
- **Action suivante :** aucun chevauchement d'incantations ou de canalisation avec une autre capacité. Une demande en avance peut être mise en attente pendant une fenêtre proposée de **0,2 seconde** avant la disponibilité, puis entièrement revalidée au démarrage. Une seule demande est mémorisée ; une nouvelle la remplace. L'annulation explicite vide cette demande. Aucun coût ni délai n'est engagé tant que l'action n'a pas commencé.

Le cooldown propre part de la **résolution**, sauf les maintiens qui l'engagent à leur premier effet. Ainsi, Vague de flammes met 2 secondes à incanter, puis attend son cooldown de 8 secondes avant le lancement suivant : deux résolutions consécutives sont espacées d'au moins 10 secondes. Tous les instants et durées de gameplay sont décidés par le serveur.

## Warrior

| Capacité | Coût natif R1 / R2 | Coût hérité R1 | Cast | Cooldown propre |
| --- | --- | --- | --- | --- |
| Frappe puissante | 12 / 12 S | 15 S | Instantané | 0 s |
| Garde | 12 / 12 S | 15 S | Instantané ; maintien ≤ 3 s | 8 s |
| Provocation | 8 S ; rang unique | Réservé | Instantané | 8 s |
| Coup entravant | 16 / 16 S | 20 S | Instantané | 10 s |
| Charge | 20 S ; rang unique | Réservé | Instantané | 15 s |

## Scout

| Capacité | Coût natif R1 / R2 | Coût hérité R1 | Cast | Cooldown propre |
| --- | --- | --- | --- | --- |
| Tir précis | 12 / 12 S | 15 S | 1,5 s | 0 s |
| Pas léger | 16 / 16 S | 20 S | Instantané | 20 s |
| Marque de traque | 8 S ; rang unique | Réservé | Instantané | 12 s |
| Tir entravant | 16 / 16 S | 20 S | Instantané | 10 s |
| Camouflage | 20 S ; rang unique | Réservé | Instantané | 30 s |

## Mage

| Capacité | Coût natif R1 / R2 | Coût hérité R1 | Cast | Cooldown propre |
| --- | --- | --- | --- | --- |
| Projectile arcanique | 6 / 6 M | 8 M | 1,5 s | 0 s |
| Barrière arcanique | 12 / 12 M | 15 M | Instantané | 20 s |
| Vague de flammes | 18 M ; rang unique | Réservé | 2 s | 8 s |
| Éclat de givre | 8 / 8 M | 10 M | Instantané | 10 s |
| Téléportation courte | 20 M ; rang unique | Réservé | Instantané | 20 s |

## Cleric

| Capacité | Coût natif R1 / R2 | Coût hérité R1 | Cast | Cooldown propre |
| --- | --- | --- | --- | --- |
| Soin / Heal | 16 / 16 M | 20 M | 2 s | 0 s |
| Trait sacré | 6 / 6 M | 8 M | 1,5 s | 0 s |
| Égide sacrée | 18 M ; rang unique | Réservé | Instantané | 15 s |
| Dissipation | 12 / 8 M | 15 M | Instantané | 8 s |
| Prière de groupe | 8 M par pulsation ; 32 M maximum | Réservé | Canalisation de 4 s | 15 s |

## Druid

| Capacité | Coût natif R1 / R2 | Coût hérité R1 | Cast | Cooldown propre |
| --- | --- | --- | --- | --- |
| Ronces | 6 / 6 M | 8 M | 1,5 s | 0 s |
| Renouveau | 12 / 12 M | 15 M | Instantané | 3 s |
| Enracinement | 14 M ; rang unique | Réservé | Instantané | 12 s |
| Peau d'écorce | 16 / 16 M | 20 M | Instantané | 25 s |
| Floraison | 18 M ; rang unique | Réservé | Instantané | 20 s |

## Précisions sur les capacités particulières

**Garde :** 12 S natifs ou 15 S hérités, payés une seule fois à l'entrée. Aucun entretien. Maintien maximal de **3 secondes**, interrompu volontairement ou en lançant une attaque/un sort. Le cooldown de 8 secondes démarre à l'entrée et continue après une sortie anticipée ; il ne peut pas être effacé en relâchant puis reprenant la garde. La récupération de stamina continue. R2 améliore la protection, pas cette durée ni ce coût.

**Prière de groupe :** canalisation immobile de **4 secondes**, sans pulsation à l'instant initial. Les soins ont lieu à **1, 2, 3 et 4 secondes** après le début. Chaque pulsation paie **8 M juste avant son effet**, soit **32 M pour la canalisation complète**. Le cooldown de 15 secondes commence à la première pulsation. Au départ, il faut pouvoir payer cette première pulsation ; il n'est pas nécessaire de posséder immédiatement les 32 M. Une interruption, un déplacement ou un manque de mana arrête les futures pulsations, sans rembourser les précédentes. Le lanceur fait partie des cibles de groupe admissibles ; les soins excédant la vie manquante consomment quand même le mana.

**Camouflage :** 20 S une seule fois à l'entrée, sans entretien. Il est activable uniquement hors combat et suit le délai global. Son cooldown démarre à l'entrée et peut expirer pendant la dissimulation ; une réactivation après rupture est alors possible une fois hors combat. Cette conséquence est à éprouver lors des essais d'approche et de fuite, sans ajouter implicitement un second cooldown à la sortie.

**Dissipation :** le R1 natif coûte 12 M, le R2 natif 8 M, et le R1 hérité 15 M. Le coût hérité est calculé depuis R1, jamais depuis le R2 qui a permis son déblocage. Sans malédiction dissipable à retirer, l'action est refusée avant paiement et cooldown.

**Floraison :** le coût de 18 M achète l'application immédiate de soins déjà prévus, pas des soins supplémentaires. Seuls les Renouveaux du lanceur sur les membres du groupe proches sont consommés. Sans effet admissible à convertir, l'action est refusée sans coût ni cooldown. Les soins qui excèdent la vie manquante sont perdus, sans remboursement.

Les puissances, les portées et les durées des buffs, soins progressifs et contrôles restent à chiffrer. Seuls le maintien de Garde et la canalisation de Prière sont bornés ici, car leur durée définit directement l'action ou son paiement. Les temps proposés ne fixent donc pas à eux seuls l'efficacité des capacités.

## Exemples de budget

- **Cleric, Heal R1 ou R2 :** 16 M, cast de 2 s, aucun cooldown propre. Le meilleur rang appris soigne davantage au même coût ; sa quantité reste à définir.
- **Warrior avec Heal R1 hérité :** 20 M, même cast de 2 s, aucun cooldown propre. Avec 100 M et en ignorant volontairement la récupération, il peut payer **5 soins**, contre **6** pour un Cleric utilisant 16 M par soin. Le Warrior doit interrompre Garde et ses attaques pour incanter.
- **Mage avec Pas léger R1 :** 20 S, instantané, cooldown de 20 s. Cette action utilise sa stamina, mais partage le temps d'action global de ses sorts.
- **Mage avec Frappe puissante R1 :** 15 S et une arme de mêlée compatible. Alterner cette frappe et son projectile peut exploiter les deux réserves ; le surcoût ne garantit pas une moins bonne endurance qu'une rotation purement magique.
- **Druid avec Heal et Renouveau :** les deux soins dépensent du mana et se disputent le temps d'action. Leur combinaison doit être comparée au Druid sans Heal ; leur disponibilité ne prouve pas qu'une configuration est équilibrée.

L'objectif est un héritage utile qui conserve un coût de temps, de ressource et d'équipement. Un Warrior dispose d'une réserve de mana peu sollicitée par ses attaques : le surcoût de 25 % ne suffit donc pas à démontrer que monter Cleric restera facultatif. Le nombre de capacités héritées équipables demeure une décision ouverte.

## Essais et réglages

Les durées d'essai et seuils d'acceptation ci-dessous sont également des **valeurs de départ**, destinées à rendre la proposition mesurable. Aucun essai en jeu n'a encore été exécuté.

| Essai | Critère de départ | Ajustement si échec |
| --- | --- | --- |
| Rotation d'attaque de base pendant 20 s, réserve pleine, récupération normale | Chaque classe peut utiliser son attaque de départ sans attente forcée et conserve au moins 20 points de sa ressource principale ; ce contrôle ne mesure pas les dégâts | Réduire le coût de l'attaque ou augmenter sa récupération si la réserve est trop vite vide ; retester ensuite les utilitaires |
| Rencontre solo au niveau 1, puis au niveau 5, sans héritage | La classe peut finir une rencontre de départ sans rester bloquée en attente de sa ressource ; l'usage d'un soin ou d'une défense demande un choix visible | Ajuster d'abord coût/efficacité de l'attaque et durée de rencontre ; ne pas rendre un héritage nécessaire pour corriger une classe incomplète |
| Rotation native contre alternance mana/stamina sur 60 s | Aucune alternance ne doit dominer à la fois dégâts, survie et mobilité dans toutes les rencontres de comparaison | Revoir coûts des capacités concernées, exigences d'équipement ou puissance de leur R1 avant d'augmenter globalement tous les coûts hérités |
| Warrior avec et sans Heal, puis avec une autre capacité héritée | Heal crée une occasion de secours ; absence de Heal ne rend pas systématiquement les mêmes rencontres impossibles. Mesurer aussi le temps passé à soigner et la récupération entre combats | Ajuster rendement/coût de Heal, pression pendant le cast ou sélection d'héritages si le soin devient un passage obligé |
| Heal + Renouveau + défense, puis contrôle + mobilité | La combinaison conserve des fenêtres de vulnérabilité ; impossibilité de maintenir toutes les défenses sans arbitrage | Revoir durées/effets, coûts et cumuls à partir des résultats ; les valeurs de ces effets doivent être fixées avant ce test |
| Garde pendant toute sa durée, puis arrêt anticipé et relance | Un seul débit de 12 S natifs ou 15 S hérités ; arrêt au plus tard à 3 s ; aucune relance avant le cooldown de 8 s | Corriger les transitions si le débit ou les délais sont faux ; ajuster ensuite durée/coût si la garde est trop dominante ou inutilisable |
| Prière complète, interrompue puis à mana insuffisant | Quatre pulsations coûtent exactement 32 M avant récupération ; aucune pulsation gratuite à zéro, aucun remboursement après effet | Corriger l'ordre paiement/effet ; ajuster coût par pulsation ou cooldown seulement après conformité |
| Réactivité avec délai global et file d'entrée | Une entrée dans les 0,2 s avant disponibilité produit exactement l'action attendue, sans action fantôme après annulation ; le joueur perçoit l'acceptation immédiatement | Corriger la revalidation ; réduire le délai global si le rythme est trop lent, ajuster la fenêtre si les demandes volontaires sont perdues ou trop anticipées |
| Récupération et changement de classe | Soutenir un allié en combat empêche la récupération renforcée ; après 5 s continues hors combat les bons taux s'appliquent ; aucune recharge de réserve ou de cooldown au changement de classe | Corriger le statut de combat et la conservation d'état avant d'ajuster les taux |

Présenter dans le grimoire le coût réel du rang équipé, son origine, le cast et les deux délais. Un retour visuel et sonore distingue début d'action, interruption et effet confirmé ; un refus précise la cause. Cela permet de corriger d'abord la compréhension et la réponse aux entrées avant de retoucher les valeurs.

## Vérification documentaire effectuée

Les 25 capacités correspondent au catalogue accepté : 15 R1 transmissibles et 10 capacités réservées. Les coûts hérités ont été recalculés avec l'arrondi supérieur ; les liens locaux des documents sont valides.

Un calcul de budget sur 20 secondes, réserve pleine et récupération normale, donne 32 stamina restantes après 14 Frappes puissantes, 36,5 stamina après 13 Tirs précis et 40,5 mana après 13 attaques magiques de départ. Les paiements ont lieu à la résolution, les incantations occupent 1,5 seconde et la récupération est plafonnée à 100. Aucun de ces cas n'attend sa ressource. Cela vérifie seulement le budget de dépense, sans dégâts, soins, équipements, réseau ou simulation Unity ; répéter Ronces dans ce calcul ne prouve pas que ce serait une bonne rotation de dégâts.

