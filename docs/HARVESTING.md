# Récolte des ressources du monde

La récolte relie les arbres et rochers des Areas chargées, dont StarterIsland,
aux personnages du parcours local et du serveur dédié. Le serveur possède le travail restant sur
chaque ressource, valide chaque coup et attribue le butin dans les sacs du
personnage. Le client affiche les ressources répliquées et demande des coups.

## Utilisation

S'approcher d'un arbre ou d'un rocher, puis maintenir **G** pour donner des
coups de hache ou de pioche à la ressource proche sélectionnée. La portée est de
3 mètres depuis son rayon de surface ; l'écart vertical autorisé est de
3 mètres. Deux coups acceptés sont espacés d'au moins **0,85 seconde**.
L'interface affiche la ressource, le travail restant et les refus éventuels.
L'ouverture d'un panneau qui bloque les commandes suspend cette entrée.

Tous les arbres actifs actuellement placés sur l'île sont détectés, y compris
les arbres des Terrains Unity. Les visuels Arkya et les groupes LOD connus sont
regroupés en une seule ressource par arbre. Les rochers reconnus fournissent de
la pierre, du grès, du fer ou du cuivre : un hash stable de leur position détermine leur
famille, avec une pondération de **30 % pierre, 30 % grès, 20 % fer et 20 % cuivre**.
Ce calcul est déterministe ; les proportions exactes dépendent des positions
des rochers de la scène.

Le grès reçoit une teinte sable, le fer une teinte froide et le cuivre une teinte cuivrée. Des inclusions
minérales sont ajoutées aux meshes dont les données CPU sont accessibles ;
la teinte reste disponible sur les autres. Ces adaptations sont créées à
l'exécution et préservent les assets sources.

## Coups, outils et cristaux

Une hache et une pioche de départ permettent de commencer immédiatement :
leur puissance de base est de **20 unités de travail par coup**. Un outil
compatible sauvegardé avec le personnage utilise son niveau, ses bonus et ses
cristaux sertis. La famille `Wood` améliore le bois, `Stone` la pierre et le grès, et `Ore`
le fer et le cuivre. Les niveaux de combat et d'artisanat ne modifient pas la
récolte dans cette tranche.

Les valeurs initiales d'équilibrage sont les suivantes :

| Ressource | Travail total | Coups avec l'outil de départ | Quantité de base à épuisement | Retour après épuisement | ID persistant | Poids unitaire |
| --- | --- | --- | --- | --- | --- | --- |
| Bois | 80 | 4 | 8 | 120 s | `resource:wood` | 500 g |
| Pierre | 100 | 5 | 6 | 180 s | `resource:stone` | 1 000 g |
| Grès | 100 | 5 | 6 | 180 s | `resource:sandstone` | 1 000 g |
| Fer | 150 | 8 | 3 | 180 s | `iron-ore` | 750 g |
| Cuivre | 120 | 6 | 4 | 180 s | `copper-ore` | 750 g |

Pour un outil compatible, le bonus en points de base est calculé par :

```text
bonus = bonus de base + (niveau - 1) × bonus par niveau + bonus des cristaux
bonus appliqué = minimum(bonus, 100 000)
puissance = plancher(20 × (10 000 + bonus appliqué) / 10 000)
quantité outil = plancher(quantité de base × (10 000 + bonus appliqué) / 10 000)
quantité finale = plancher(quantité outil × taux du royaume en millièmes / 1 000)
```

Le plafond de bonus est de +1 000 %. Le taux du royaume modifie le butin,
sans modifier le nombre de coups. Les cristaux peuvent également réduire le
poids total des ressources et items portés dans les sacs, avec le plafond
existant de **90 %**. Cette réduction dépend de l'outil équipé, même si sa
famille ne correspond pas à la ressource en cours.

## Autorité, inventaire et sauvegarde

Le RPC de coup contient uniquement l'identifiant de ressource et une séquence.
Le serveur résout le personnage de la connexion, vérifie sa propriété, sa vie,
sa disponibilité, la séquence, le délai, la partition propriétaire, la portée
et la ligne de vue. Les sauvegardes ou transferts d'autorité qui verrouillent
l'inventaire empêchent la récolte pendant cette période.

Les premiers coups réduisent le travail restant. **Seul le dernier coup
attribue le butin à son auteur.** Le travail du nœud est partagé entre les
joueurs ; le joueur qui termine reçoit la quantité complète. Une attribution
réussie donne également **5 XP à l'école active** si elle enseigne le skill
de récolte : `Logging` pour le bois (Logger/Carpenter), `Quarrying` pour la
pierre et le grès (Miner/Mason), `Mining` pour les minerais (Miner/Blacksmith).
Les coups intermédiaires et les
refus ne donnent pas d'XP. Cette progression appartient aux
[écoles d'artisanat](CRAFTING.md), indépendamment des classes de combat.

Les gains complètent les piles existantes puis les premiers emplacements
libres, en préservant les positions des autres items. Les ressources se
rangent par piles de 100. La capacité existante des sacs s'applique, dans la
limite de 150 emplacements, avec une capacité de portage initiale de **40 kg**.
Le poids des items publiés vient du catalogue d'items ; une identité inconnue
empêche l'attribution plutôt que de lui attribuer un poids nul.

L'attribution est atomique : si les places ou le poids ne permettent pas de
recevoir l'ensemble du butin, le dernier coup est refusé, l'inventaire reste
intact, aucune XP n'est attribuée et la ressource conserve son travail précédent. Un taux de royaume qui
donne une quantité finale nulle conserve également la ressource.

Les gains utilisent les vrais buffers de possessions du personnage. Ils sont
donc repris par la sauvegarde, la restauration et le transfert déjà en place
dans le parcours d'admission au dédié. Les minerais de fer et de cuivre ont
des IDs distincts et restent distincts dans les sacs et en persistance. Le
parcours local sans compte conserve seulement ses données de session.

La pierre et le grès servent aux premières recettes du [stone cutter](CRAFTING.md) :
**4 à 2 unités brutes → 1 brique**, selon le meilleur score de `Stonecrafting`,
avec **10 XP à Miner ou Mason actif** par fabrication réussie.
Les deux ressources et leurs briques possèdent des IDs persistants distincts.

Une ressource épuisée disparaît et libère ses collisions ; une souche ou des
fragments de pierre restent visibles jusqu'à sa réapparition. Le retour
restaure son travail complet et ses collisions.

## Ajouter de nouvelles ressources à la scène

Le catalogue d'exécution se trouve dans
`Unity3D/Arkya/Assets/Arkya/Characters/Runtime/Harvesting/`.
`CharacterHarvestNodeAuthoring` permet de déclarer explicitement une racine
de ressource et sa famille `Wood`, `Stone`, `Sandstone`, `Iron` ou `Copper`.

Les arbres actuels et les noms de familles déjà reconnus sont détectés
automatiquement. Pour un nouvel asset dont le nom ou la structure diffère,
ajouter **Arkya > Harvesting > Resource Node** à sa racine ; cette déclaration
explicite garantit son intégration sans dépendre d'une convention de nommage.
Les modèles fournisseurs et leurs matériaux d'origine restent préservés.

## Vérification et limites

L'extension artisanale du 24 octobre ajoute le grès, les gains d'XP de métier,
les briques et la paire réseau **6 / `arkya-crafting-v1`**. La suite backend
Characters donne **343 réussites, 0 échec et 36 tests MongoDB ignorés**.
Les quatre assemblies concernées et la variante à symboles serveur compilent
hors Editor ; **33 cas de règles pures réussissent** dans ce contrôle.
La compilation Editor, l'exécution des nouveaux tests Unity et la validation
serveur/client restent à effectuer après l'échec d'initialisation MCP sur
`127.0.0.1:8080` ; voir le [bilan artisanal](CRAFTING.md#vérification-et-limites).
Les contrôles ci-dessous décrivent la tranche de récolte précédente.

Les sources de récolte ont été compilées hors Editor avec Roslyn et les
références de Unity 6000.6.6f1 installé : runtime Editor, assembly de tests et
variante à symboles serveur, sans erreur. Les générateurs ECS/Netcode produisent
le ghost de ressource et les deux serializers RPC. La variante serveur utilise
les dépendances Editor mises en cache ; elle ne remplace pas un build dédié.
Les tests ajoutés couvrent les règles
d'outils et de cristaux, les quatre ressources et leurs IDs persistants, les
emplacements clairsemés, les refus atomiques, les limites de poids, le taux du
royaume et la découverte des ressources de scène.

Les tests backend existants ciblés sur les possessions donnent **49 réussites,
0 échec et 1 cas MongoDB ignoré** faute d'environnement d'intégration configuré.
Les 24 cas Unity ajoutés (16 règles, 7 scènes et 1 parcours réseau avec deux
clients loopback) sont compilés mais n'ont pas été exécutés.

Le MCP Unity a échoué à son initialisation HTTP pendant cette intervention.
Les tests Unity Edit Mode/Play Mode, un vrai parcours serveur/client et une
capture dans l'Editor restent à exécuter. La compilation hors Editor
inclut les générateurs ECS/Netcode, mais ne valide pas le rechargement de domaine,
le traitement IL de Unity, le rendu, les collisions en jeu ou le
fonctionnement réseau complet.

Le travail restant et l'épuisement des nœuds résident **en mémoire dans
chaque dédié**. Un redémarrage réinitialise les ressources ; les objets déjà
reçus par les personnages suivent leur persistance habituelle. La persistance
des nœuds et les interactions à cheval sur une frontière de partition restent
des évolutions distinctes. Le client doit rejoindre la partition propriétaire
d'une ressource pour y récolter.
