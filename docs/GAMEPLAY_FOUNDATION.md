# Première base de gameplay : inventaire et récolte

Cette tranche ajoute des données ECS de personnage, un inventaire limité en places et en poids, des ressources récoltables et les effets des outils et pierres. Le code se trouve dans `Unity3D/Arkya/Assets/Arkya/Gameplay/`, dans les assemblies `Arkya.Gameplay` et `Arkya.Gameplay.Authoring`.

La simulation de récolte s'exécute uniquement dans les mondes `ServerSimulation`. Cette base est indépendante de l'île actuelle et du prototype d'areas : aucune scène ni aucun prefab existant n'est configuré par cette tranche. Lancer la scène actuelle ne fournit donc pas encore une récolte jouable à la souris.

## Personnage et inventaire

`CharacterStats` conserve la vie, la stamina et la force. La vie et la stamina commencent à leur maximum. La récolte est refusée si la vie vaut zéro ; cette tranche ne dépense pas encore de stamina et n'ajoute ni dégâts, ni régénération, ni progression de statistiques.

La capacité de portage est calculée en grammes :

```text
poids maximum = poids de base + force × poids supplémentaire par point de force
défaut        = 30 000 g + 10 × 1 000 g = 40 000 g, soit 40 kg
```

`CharacterInventory.SlotCapacity` accepte de 1 à 150 emplacements. Le buffer `InventorySlot` contient seulement les piles occupées : un nouveau personnage possède zéro pile. Une récolte complète d'abord les piles du même identifiant, puis ouvre des piles jusqu'à la limite d'emplacements. La taille maximale d'une pile vient du catalogue ; elle vaut 100 par défaut pour chaque ressource. Les variantes normale et enchantée possèdent des identifiants différents et ne s'empilent pas ensemble.

Les limites de places et de poids sont vérifiées sur le résultat complet avant de modifier l'inventaire. Atteindre exactement la capacité de portage est autorisé.

## Catalogue des ressources

Un seul `ResourceCatalog` doit exister dans chaque monde de simulation qui récolte. Ses définitions sont identifiées par `ResourceItem`. Le catalogue par défaut contient ces douze identifiants ; les poids et tailles de pile sont des valeurs initiales d'équilibrage configurables.

| ID | Identifiant | Famille | Variante | Poids unitaire | Pile maximale |
| --- | --- | --- | --- | --- | --- |
| 1 | `Wood` | `Wood` | `Normal` | 500 g | 100 |
| 2 | `EnchantedWood` | `Wood` | `Enchanted` | 500 g | 100 |
| 3 | `Stone` | `Stone` | `Normal` | 1 000 g | 100 |
| 4 | `EnchantedStone` | `Stone` | `Enchanted` | 1 000 g | 100 |
| 5 | `Ore` | `Ore` | `Normal` | 750 g | 100 |
| 6 | `EnchantedOre` | `Ore` | `Enchanted` | 750 g | 100 |
| 7 | `Crystal` | `Crystal` | `Normal` | 250 g | 100 |
| 8 | `EnchantedCrystal` | `Crystal` | `Enchanted` | 250 g | 100 |
| 9 | `Berries` | `Berries` | `Normal` | 50 g | 100 |
| 10 | `EnchantedBerries` | `Berries` | `Enchanted` | 50 g | 100 |
| 11 | `Fiber` | `Fiber` | `Normal` | 25 g | 100 |
| 12 | `EnchantedFiber` | `Fiber` | `Enchanted` | 25 g | 100 |

Un catalogue peut contenir un sous-ensemble de ces ressources, mais toute ressource présente dans un inventaire ou une liste de récolte doit y être définie. Un identifiant inconnu, un doublon, un poids ou une pile maximale non positifs, ou une association famille/variante incorrecte sont refusés. Les IDs impairs désignent la variante normale ; les IDs pairs, sa variante enchantée.

## Outils, pierres et poids effectif

Le niveau 0 de `EquippedHarvestTool` représente les mains nues. Toutes les ressources peuvent être récoltées à la main, avec leur quantité de base. Un outil peut avoir un niveau de 1 à 100 et jusqu'à six emplacements de pierres ; les mains nues n'ont aucun emplacement.

Le bonus de quantité s'applique séparément à chaque sortie dont la famille correspond à celle de l'outil. Un outil `Wood` augmente le bois normal et enchanté ; il conserve la quantité de base sur la pierre. Sur un arbuste qui produit des baies et de la fibre, un outil `Berries` améliore seulement les baies.

Les pourcentages utilisent des points de base entiers, suffixés `Bps` : `10000 = 100 %`, `2500 = 25 %`, `100 = 1 %`. Pour un outil compatible :

```text
bonus = bonus de base + (niveau - 1) × bonus par niveau + somme des bonus des pierres
quantité = plancher(quantité de base × (10 000 + bonus plafonné) / 10 000)
```

Les valeurs initiales sont `BaseYieldBonusBps = 10000` et `YieldBonusPerLevelBps = 2500` : un outil de niveau 1 donne deux fois la quantité de base ; le niveau 2 donne 2,25 fois cette quantité, arrondie à l'entier inférieur. Une sortie de base de 1 donne donc encore 2 au niveau 2. Les bonus des pierres sont additionnés au bonus de l'outil. Le bonus total est plafonné à `100000`, soit **+1 000 %** et un multiplicateur maximal de 11.

Les pierres peuvent aussi réduire le poids des ressources via `ResourceWeightReductionBps`. Leurs réductions s'additionnent, puis sont plafonnées à `9000`, soit **90 %**. Cet effet s'applique à l'ensemble des ressources portées tant qu'un outil de niveau supérieur à zéro est équipé ; il ne dépend pas de la famille de chaque pile. Les mains nues n'appliquent aucune réduction.

```text
poids brut = somme(quantité de chaque pile × poids unitaire du catalogue)
poids porté = plafond(poids brut × (10 000 - réduction plafonnée) / 10 000)
```

L'arrondi supérieur est effectué une seule fois sur le total, en grammes. Le poids effectif est recalculé à chaque mise à jour serveur, même sans nouvelle demande. Retirer une pierre ou changer d'outil peut donc rendre un inventaire existant trop lourd : les ressources restent présentes, mais les gains qui dépasseraient la capacité sont refusés. Le flux de changement d'équipement reste à implémenter.

## Validation d'une récolte

`HarvestSystem` traite une demande activée sur le personnage, puis la désactive. Il valide le catalogue, les statistiques, l'outil, les piles, la cible et les paramètres. Une demande doit avoir une séquence strictement positive et supérieure à `HarvestState.LastSequence`. Une nouvelle séquence est consommée même si la récolte est ensuite refusée : une nouvelle tentative exige une nouvelle séquence. Le compteur ne doit pas revenir à zéro pendant la vie de cette entité.

Le personnage doit être vivant, la cible à une distance inférieure ou égale à `HarvestSettings.Reach`, et le délai de la dernière récolte réussie écoulé. Le délai utilise le temps de simulation du serveur. Les positions doivent être finies et exprimées directement par des `LocalTransform` sans `Parent`.

Chaque nœud expose une ou plusieurs sorties `HarvestDrop`, de quantité strictement positive, sans identifiant répété. Toutes ses sorties doivent appartenir à sa variante. Les gains d'une action sont atomiques : si les baies passent mais que la fibre ne tient pas, l'action entière échoue. Un échec ne consomme ni charge du nœud ni délai de récolte.

Une réussite ajoute les gains, consomme une charge et démarre le délai du personnage. Lorsque la dernière charge est consommée, une réapparition est programmée après `RespawnSeconds` si cette valeur est positive. Elle restaure toutes les charges du nœud. Une valeur zéro laisse le nœud épuisé définitivement. Cette tranche ne masque pas encore son visuel.

Le résultat reste dans `HarvestResult`, avec la séquence, le statut et le nombre total d'unités accordées, toutes sorties confondues. Les statuts sont `Success`, `InvalidRequest`, `DuplicateRequest`, `Dead`, `Cooldown`, `OutOfRange`, `Depleted`, `InventoryFull`, `Overweight` ou `InvalidConfiguration` ; `None` est l'état initial. Zéro catalogue ou plusieurs catalogues entraînent `InvalidConfiguration` lors d'une demande.

## Configuration dans l'Inspector

Ouvrir le projet actif `Unity3D/Arkya/`. Les composants se trouvent dans **Add Component > Arkya > Gameplay**. Les trois types d'authoring doivent être placés dans une **SubScene** destinée au monde serveur pour que Unity crée les entités ; les ajouter à des GameObjects d'une scène classique ne suffit pas à exécuter la simulation ECS.

Créer les GameObjects d'authoring comme racines de la SubScene. Le personnage et chaque nœud doivent rester sans parent : leurs Bakers refusent explicitement une hiérarchie parente. Cette restriction garantit que les coordonnées locales utilisées pour la distance sont aussi les coordonnées du monde.

1. Créer une racine `ResourceCatalog` et ajouter **Resource Catalog Authoring** une seule fois pour ce monde. Garder les douze entrées par défaut, ou régler `Resources`. Pour chaque entrée, renseigner `Item`, la famille et la variante du tableau, `Unit Weight Grams` positif et `Max Stack` positif.
2. Créer une racine `Character` et ajouter **Character Gameplay Authoring**. Les valeurs par défaut sont les suivantes :

| Champ Inspector | Défaut | Contrainte |
| --- | --- | --- |
| `Inventory Slots` | 150 | 1 à 150 |
| `Max Health` | 100 | Positif |
| `Max Stamina` | 100 | Positif |
| `Strength` | 10 | Zéro ou positif |
| `Base Carry Weight Grams` | 30 000 | Positif |
| `Carry Weight Per Strength Grams` | 1 000 | Zéro ou positif |
| `Harvest Reach` | 3 | Fini, strictement positif, en unités Unity |
| `Harvest Cooldown Seconds` | 1 | Fini, strictement positif |
| `Tool Family` | `Wood` | Une famille connue |
| `Tool Level` | 0 | 0 à 100 ; 0 = mains nues |
| `Tool Base Yield Bonus Bps` | 10 000 | Zéro ou positif |
| `Tool Yield Bonus Per Level Bps` | 2 500 | Zéro ou positif |
| `Tool Socket Capacity` | 0 | 0 à 6 ; obligatoirement 0 à mains nues |
| `Socketed Stones` | Vide | Au plus la capacité de l'outil |

Chaque entrée de `Socketed Stones` expose `Yield Bonus Bps`, zéro ou positif, et `Resource Weight Reduction Bps`, de 0 à 10000. Le plafond de réduction total appliqué par la simulation reste de 9000. Pour essayer un outil de bûcheron, choisir `Tool Level = 1`, `Tool Family = Wood`, puis augmenter la capacité avant d'ajouter des pierres.

3. Créer une racine par ressource et ajouter **Harvestable Node Authoring**. Régler sa position dans la portée du personnage pour un essai, `Variant`, `Harvest Charges`, `Respawn Seconds` et `Drops`. Les valeurs par défaut sont `Normal`, 5 charges, 60 secondes et une sortie `Wood` de quantité 1. Chaque sortie possède `Item` et `Base Quantity`.

| Objet à configurer | `Variant` | Sorties d'exemple |
| --- | --- | --- |
| Arbre normal | `Normal` | `Wood`, quantité 1 |
| Arbre enchanté | `Enchanted` | `EnchantedWood`, quantité 1 |
| Rocher normal | `Normal` | `Stone`, quantité 1 |
| Rocher enchanté | `Enchanted` | `EnchantedStone`, quantité 1 |
| Arbuste normal | `Normal` | `Berries`, quantité 2, et `Fiber`, quantité 1 |
| Arbuste enchanté | `Enchanted` | `EnchantedBerries`, quantité 2, et `EnchantedFiber`, quantité 1 |

Les minerais et cristaux utilisent respectivement `Ore`/`EnchantedOre` et `Crystal`/`EnchantedCrystal`. Changer la variante ne remplace pas automatiquement les sorties : il faut modifier leurs identifiants. Le type visuel de l'objet ne détermine pas son butin ; la liste `Drops` le configure explicitement. Conserver les visuels Scinty dans leur espace fournisseur et ajouter l'authoring sur des objets Arkya.

## Entrée serveur et lecture du résultat

Le Baker crée `HarvestRequest` désactivé sur le personnage. Un adaptateur serveur de confiance doit résoudre le personnage de la connexion, vérifier qu'il appartient au serveur courant, résoudre la cible, puis renseigner et activer ce composant existant. Le client ne choisit jamais directement les gains, le poids ou les statistiques.

Exemple à intégrer dans cet adaptateur serveur ; `character` et `target` sont déjà des entités résolues dans ce monde, et `sequence` est strictement croissante pour ce personnage :

```csharp
using Arkya.Gameplay;
using Unity.Entities;

// Exécuté côté serveur, avant HarvestSystem.
bool QueueHarvest(EntityManager entityManager, Entity character, Entity target, uint sequence)
{
    if (sequence == 0 ||
        sequence <= entityManager.GetComponentData<HarvestState>(character).LastSequence ||
        entityManager.IsComponentEnabled<HarvestRequest>(character))
        return false;

    entityManager.SetComponentData(character, new HarvestRequest
    {
        Target = target,
        Sequence = sequence
    });
    entityManager.SetComponentEnabled<HarvestRequest>(character, true);
    return true;
}

// Après la mise à jour de HarvestSystem, pour la demande envoyée :
HarvestResult result = entityManager.GetComponentData<HarvestResult>(character);
if (result.Sequence == sequence && result.Status != HarvestStatus.None)
{
    // Publier le résultat au futur adaptateur de présentation/réseau.
}
```

Ce composant est une boîte de demande unique par personnage. Attendre son traitement avant d'en soumettre une autre évite de remplacer une demande en attente. `HarvestResult` conserve uniquement le dernier résultat ; il ne constitue ni une file d'événements ni un journal durable. La réplication de l'inventaire et du résultat devra être ajoutée explicitement.

## Périmètre et vérification

Les règles et le système disposent de tests Edit Mode dans `Assets/Arkya/Gameplay/Tests/Editor/`. La compilation et l'exécution des tests doivent être vérifiées dans le projet actif avant de considérer une modification validée. La présence de ces tests ne prouve pas une intégration de scène ou réseau.

Vérification effectuée sous Unity **6000.6.6f1** : compilation sans erreur ni avertissement, **63 cas Edit Mode réussis** et **1 test Play Mode réussi**. Ce dernier, dans `Assets/Arkya/Gameplay/Tests/PlayMode/`, vérifie une récolte unique sur plusieurs frames dans un groupe serveur isolé, sans gain dans le groupe client. Il n'ouvre pas de connexion Netcode. Le rapport est conservé dans `artifacts/gameplay-foundation/playmode-results.json`.

Le contrôle `AgentScripts/Gameplay/ValidateGameplayBaking.cs` convertit les trois types d'authoring dans un monde isolé, puis exerce les récoltes à la main et avec outil/pierre. Il a validé un personnage, les douze définitions, quatre nœuds et cinq récoltes, sans modifier les scènes ouvertes. Son rapport se trouve dans `artifacts/gameplay-foundation/baking-smoke.json`.

Pour relancer ce contrôle depuis la racine du dépôt :

```powershell
unity command run_script --file AgentScripts/Gameplay/ValidateGameplayBaking.cs --entry ValidateGameplayBaking.Run --project-path D:/Repositories/arkya/Unity3D/Arkya --caller plugin --skill unity-cli --json
```

Ce script de vérification utilise par réflexion une entrée interne de baking d'Entities 6.6 ; il peut demander une adaptation lors d'une mise à jour du package. Le code de gameplay n'en dépend pas.

Restent à implémenter : sélection/clic de récolte, animation et disparition des ressources, UI d'inventaire et de statistiques, RPC et association connexion/personnage, apparition autoritaire des personnages, changement d'outil et insertion/retrait des pierres, transfert d'area, sauvegarde backend et restauration. Les données ajoutées ne sont pas des ghosts et ne sont pas automatiquement transférées ni persistées.

Le prototype existant continue de posséder sa vie de combat dans `PrototypeActor.Health`. Cette tranche utilise `CharacterStats.Health` seulement pour ses entités indépendantes ; elle ne branche pas un second propriétaire de vie sur les acteurs du prototype. Une intégration ultérieure devra choisir une seule source de vérité et adapter les systèmes concernés.
