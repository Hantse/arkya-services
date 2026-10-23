# Aubefont et son puits de mana

Aubefont habille le refuge de l’île de départ avec neuf maisons actives, un hôtel de ville en pierre, un puits de pierre gravée, une petite place pavée et des rues courbes. La seconde version rapproche les maisons, remplace l’enceinte elliptique par un tracé irrégulier plus compact et ajoute deux escaliers vers un chemin de ronde continu. La [passe de matières et d’intérieurs du 20 octobre](starter-island/town/warmth-2026-10-20/README.md) complète les planchers, vitres et étages, les tissus au vent et les tonneaux. Les fonctions annoncées par les enseignes restent décoratives : aucun PNJ, commerce, restauration de mana ou état de quête n’est ajouté.

Le projet actif est `Unity3D/Arkya/`, sous Unity **6000.6.6f1**. La scène est `Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity`. Le puits reste centré en **X = 550 m, Z = 520 m**.

La [passe magique et fleurie du 20 octobre](starter-island/town/enchantment-2026-10-20/README.md) ajoute dix lanternes de mana en lévitation, des ornements runiques, des jardinières et des lianes lilas et roses. Les crépis pastel différencient les maisons. Ces ajouts de présentation conservent le Terrain et les collisions, sans lumière ponctuelle supplémentaire.

La [reprise du sol du 20 octobre](starter-island/town/ground-2026-10-20/README.md) remplace le pavage régulier par des pierres plus sombres, usées et décalées, avec des joints moussus et des transitions de terre plus naturelles. Cinq petits groupes de paille et une corbeille accompagnent les cours et marchés ; les toits, pignons et pièces de charpente des maisons et de la mairie deviennent légèrement courbes. Les modules de façade conservent leurs meshes d’origine. Cette première reprise du coin de mairie relève une guirlande et replace un lampadaire. Sa peinture reste locale, sans modification des hauteurs du Terrain ; seul le collider du lampadaire suit alors son déplacement.

La [mairie en pierre et son parvis dégagé](starter-island/town/civic-stone-2026-10-20/README.md) remplacent ensuite la maison du boulanger devant l’édifice par un espace ouvert. Cette maison reste dans la scène, désactivée avec ses 122 colliders ; neuf maisons demeurent actives. Ses décors détachés, deux anciennes poutres de pignon et sa branche pavée sont retirés. La mairie reçoit une maçonnerie plus présente, quatre contreforts, un porche à arcades et un campanile en pierre. Les portes, fenêtres, planchers et ancrages des bâtiments restent en place. Quatre colliders du porche s’élargissent et quatre volumes de contrefort s’ajoutent ; les 2 373 autres colliders conservent leur état et leur géométrie. Cette passe conserve intégralement les hauteurs et la peinture du Terrain.

La [composition et les matières peintes finales](starter-island/town/art-finish-2026-10-20/README.md) déplacent ensuite la mairie en **X539/Z552**, l’agrandissent de **20 %** et lui donnent une approche courbe reliée à la place. Des joints continus ancrent le parvis, 315 pierres complètent les rues, 23 groupes plantés accompagnent les façades et trois arbres de cour s’élargissent. Les matières de pierre, chaume et bois sont adoucies, avec une reprise locale de la prairie sans changement de relief. Les 181 colliders de mairie suivent sa hiérarchie ; les 2 322 autres restent identiques. Le bloom demeure actif, mais son entrée HDR est bornée pour supprimer le grand halo blanc de la vue Scene. Les neuf maisons, les deux portes et les lumières restent en place.

La [passe des cours habitées et des références visuelles](starter-island/town/lived-in-2026-10-20/README.md) ajoute **24 groupes** de livraisons, réserves et plantations : quatre charrettes, 26 caisses, 12 balles de foin, 26 buissons, 50 groupes d’herbes et 37 groupes de fleurs. Une seconde couche ajoute **10 arbres, 24 buissons et 30 groupes de fleurs** ; ses herbes initiales sont remplacées par une prairie de **132 440 brins courbes**, fins et verts, avec un éclairage cohérent sur leurs deux faces. Neuf de ces dix arbres sont agrandis à **9,61–12,56 m**, avec des feuilles multipliées par 2,2 autour de leur centre ; le dernier garde sa forme à cause d’un contrefort voisin. Cette seconde couche représente **995 024 triangles proches et 129 renderers, tous niveaux compris**. Une troisième couche distincte ajoute **14 arbres, 499 240 triangles et 42 renderers** pour encadrer les maisons. Ces comptes ne mesurent pas les FPS. Les neuf maisons actives reçoivent des toitures incurvées et des charpentes épaissies ; cinq couvertures passent à de larges bardeaux d’ardoise. La reprise des contacts de pignon porte les meshes de toiture résultants à **296 638 triangles**.

Les **31 matériaux de palette**, puis la réponse lumineuse des **35 matériaux possédés** de bâtiments et accessoires, séparent davantage les matières et refroidissent leur lumière ambiante diurne. Les lanternes de mana retrouvent leur modelé de jour en conservant leur émission nocturne. Trois matériaux supplémentaires réchauffent **53 renderers** de vitres, lanternes traditionnelles et guirlandes ; leurs cœurs crème-dorés et leurs bords cuivrés utilisent les paramètres horaires existants, sans lumière ajoutée. Le ciel dérivé renforce le bleu et utilise un champ continu de nuages à quatre échelles, aux sommets blancs et aux côtés froids, à l’échelle de la scène. Il remplace les calottes répétées de l’étape intermédiaire ; son coût sur les appareils cibles reste à mesurer.

Les [treize captures fournies par l’utilisateur](../art-direction/references/town-2026-10-20/README.md) précisent maintenant la cible au-delà de la mention initiale d’Overwatch : canopées et jardins reliés, silhouettes sculptées, matières peintes et contraste chaud/froid. Aucune équivalence de finition n’est revendiquée. Le parvis, les pavés, le Terrain complet, les transformations et collisions d’origine ainsi que les profils de rendu restent conservés. L’affectation du ciel et les meshes de toiture sont les changements explicites de présentation. **Douze captures finales inspectées** montrent les retouches de prairie, de canopée, de fenêtres, de nuages et de raccords, dont deux compositions à hauteur de jardin ; les helpers restaurent la caméra, l’heure et la météo après capture. La prairie se lit davantage en masses continues ; les feuillages pointus et les limites de massifs restent des écarts avec les références. Les paramètres de croissance et de vent perdus sur 17 matériaux possédés ont été restaurés. **La scène est sauvegardée et laissée en Edit Mode ; les validations finales réussissent.**

Le [contrôle général](starter-island/town/lived-in-2026-10-20/validation.json) confirme **2 503 colliders et 9 768 transformations conservés**, ainsi que le Terrain et les profils inchangés. Le [contrôle des jardins](starter-island/town/lived-in-2026-10-20/reference-gardens-validation.json) examine les **132 440 brins sans intrusion** ; le [groupe de 14 arbres](starter-island/town/lived-in-2026-10-20/reference-canopy-frame-validation.json) est validé séparément. Leurs bases et contacts ont été affinés, avec **0,11993 m** de marge réelle minimale aux voies. L’[audit des obstacles et voies](starter-island/town/lived-in-2026-10-20/reference-canopy-frame-contacts.json) et les **4 249 tests** de l’[audit des surfaces de toiture](starter-island/town/lived-in-2026-10-20/reference-canopy-roof-contacts.json) ne relèvent aucun contact restant. Les shaders de ville ne présentent aucune erreur et la compilation n’est ni en cours ni en échec ; l’historique de console conserve une erreur et 33 avertissements. Aucune mesure de performance GPU ou de FPS n’est revendiquée.

La [reprise des pierres et des plantes grimpantes](starter-island/town/wall-natural-variation-2026-10-20/README.md) introduit une maçonnerie de formats variés. La [passe de relief, lierre étalé et escaliers compacts](starter-island/town/wall-depth-spread-2026-10-20/README.md) la complète avec des pierres en volume aux bords irréguliers et des nappes de végétation couvrant plusieurs travées. Les deux escaliers, environ un tiers plus courts, rejoignent directement la face de la courtine ; leurs collisions suivent les marches réelles. Les raccords en onglet et les ouvertures du chemin de ronde sont conservés. Ces dérivés se trouvent dans `Town/RampartUpgrade/SculptedMasonry/`, `SpreadingClimbers/` et `CompactWallStairs/`.

## Organisation de la scène

La racine **Aubefont - mana well town** contient les groupes suivants :

- **Buildings - ten inhabited silhouettes** : nom historique conservé ; neuf maisons actives et la maison du boulanger désactivée, gardée pour réversibilité.
- **Hotel de ville - Aubefont civic house** : mairie à deux niveaux en pierre, porche à arcades, contreforts, campanile et bannières ; échelle 1,2 et orientation 160°.
- **Civic forecourt - worn limestone apron and curved approach** : parvis de 6,4 m et approche courbe de 2,2 m vers la place.
- **Town gardens - layered planted courts and grounded facades** : plantations regroupées aux façades et en bordure de place, avec LOD.
- **Town paving - settled stones in worn street gaps** : compléments des rues existantes.
- **Town life - inhabited workyards and garden courts** : livraisons, réserves de caisses et foin, treize jardins supplémentaires ; présentation sans collider ni lumière.
- **Lush gardens - enclosed courts and flower drifts** : sous-groupe de végétation étendue, regroupée en cellules avec LOD et vent.
- **Reference gardens - layered canopy frame** : autre enfant des cours habitées, quatorze arbres de cadrage et trois meshes partagés, avec disparition à distance.
- **Reference architecture - swept roofs and broad shingles** : sous-groupe des charpentes épaissies et couvertures ajoutées ; les ensembles toit-pignon utilisent leurs meshes dérivés.
- **Town lanes V2 - intimate crooked streets** : petite place irrégulière, boucle du marché, rues, accès aux façades, cours et jardins.
- **Town life - stalls gardens lanterns and signs** : mobilier, enseignes et lumières conservés, puis replacés dans le nouveau plan.
- **Mana well • engraved stone and azure source** : marches chanfreinées, blocs gravés, margelle, pierres gardiennes, bassin azur, cristal et particules.
- **Town ramparts - two gates** : enceinte compacte, chemin de ronde, deux portes ouvertes et deux escaliers.
- **Local town glow volume** : bloom local de la ville.

L’ancien groupe **Streets - plaza ring and connected lanes** et l’ancien refuge **Refuge - painted timber thatch and garden beds** restent désactivés. Leurs assets sont conservés. Les arbres et décors qui empiétaient sur les voies et la maçonnerie sont désactivés localement.

| Bâtiment | X | Z | Niveaux de façade |
| --- | ---: | ---: | ---: |
| Auberge des Lanternes | 525 | 540 | 2 |
| Maison du boulanger — désactivée | 541 | 546 | 2 |
| Atelier des runes | 558 | 550 | 2 |
| Maison des voyageurs | 570 | 558 | 1 |
| Herboristerie | 581 | 534 | 1 |
| Forge du Refuge | 581 | 500 | 1 |
| Entrepôt des marchands | 568 | 493 | 2 |
| Maison des tisserands | 553 | 492 | 2 |
| Maison du verger | 535 | 493 | 1 |
| Relais de la crique | 519 | 515 | 1 |
| Hôtel de ville | 539 | 552 | 2 |

Le [rapport de placement](starter-island/town/ramparts-v2/streets-validation.json) conserve les positions, rotations et seuils. Les meshes de rue échantillonnent le Terrain et restent disponibles pour l’édition, avec leurs renderers désactivés au profit de la peinture douce du sol. La place pavée reste visible. Ni ces meshes ni les bâtiments ne se recalent automatiquement après une sculpture manuelle.

## Rues et espaces habités

La place a un rayon variable d’environ **9,5 à 11,5 m**, contre 14,8 m dans la première version. Son bord pavé irrégulier et les raccords de rues évitent un disque parfait. Les axes d’arrivée et de forêt mesurent **6 m** ; la boucle courbe du marché, **3,8 m** ; le passage au nord du puits, **3,6 m**. Les accès aux escaliers mesurent **2,6 m** et les branches vers les façades, **2,35 m**.

Les maisons forment des fronts plus proches des voies. Les espaces arrière accueillent potagers, arbres de cour, bordures plantées, linge et chariot. Les étals, bancs, lanternes et guirlandes reprennent ce nouveau plan. La peinture locale efface les anciennes grandes allées et raccorde les voies actuelles ; les hauteurs du Terrain restent intactes. Le [rapport de raccordement](starter-island/town/ramparts-v2/approach-repair.json) décrit l’ajustement des deux rues de porte, des accès d’escalier et des décors proches des passages.

Les identifiants des routes de l’île sont conservés. L’arrivée et la prairie rejoignent la porte sud-ouest ; la forêt et la route du port rejoignent la porte nord-est. Les accès sont raccordés aux positions actuelles des portes et les autres directions contournent l’enceinte. Le [relevé des axes de rues](starter-island/town/ramparts-v2/street-centerlines.json) sert aux contrôles de passage ; ce relevé seul ne prouve pas leur franchissement.

## Remparts, portes et chemin de ronde

L’enceinte suit douze points de contrôle asymétriques, avec des inflexions adoucies et des panneaux droits autour des portes. Son emprise de maçonnerie est d’environ **100 × 100 m**, au lieu de l’ellipse initiale de 144 × 154 m. Les fondations suivent le sol. La pierre peinte, les joints décalés, les contreforts espacés, les bandeaux et les couronnements sculptés apportent du relief aux courtines.

La [correction des jonctions du 20 octobre](starter-island/town/wall-junctions-2026-10-20/README.md) ferme les entailles en V entre panneaux : **102 raccords en onglet** rendent les murs, dalles, parapets et couronnements continus. Dix-huit maillages dérivés conservent les sources et le budget de rendu ; **14 petits volumes de collision** comblent les vides d’appui entre les collisions existantes. Le tracé, les tours, les accès et le relief sont conservés.

Le chemin de ronde est continu, y compris au-dessus des deux arches. Sa surface est à **Y = 56,105 m**, pour une largeur utile prévue de **3 m** entre protections intérieure et extérieure. Les parapets intérieurs s’interrompent aux débouchés des escaliers. Les mains courantes des volées ont été retirées à la demande, sur la muraille, les guets et les escaliers intérieurs. Le chemin traverse les tours de porte par des passages de **3,50 m de large et 2,65 m de haut** ; les plateformes de guet sont **3 m plus haut**, accessibles par vingt marches.

Il y a **exactement deux portes**, avec arche maçonnée, **une tour de chaque côté**, bannières turquoise, ferrures et lanternes. Les quatre tours portent des toitures d’ardoise culminant à **6,60 m au-dessus du chemin de ronde**. La [révision des entrées](starter-island/town/gatehouses-2026-10-20/README.md) remplace les vantaux battants par deux **herses levantes enregistrées ouvertes**, avec glissières, treuils et chaînes. Leur animation verticale parcourt **4 m en 2,4 s** ; elle est commandable en présentation, sans interaction joueur ni collision mobile autoritaire.

L’ouverture nominale entre piédroits mesure environ **8,5 m** ; les socles des tours réduisent la largeur physique minimale au sol à **6,636 m**. Les largeurs mesurées restent identiques avant et après cette révision. Les **48 parcours serveur** des entrées et les deux circuits complets des remparts réussissent, ainsi que la fermeture/réouverture des herses observée en Play Mode. Les preuves actuelles sont dans le [rapport des entrées](starter-island/town/gatehouses-2026-10-20/README.md#vérifications).

| Porte dans la hiérarchie | X | Z | Raccordement |
| --- | ---: | ---: | --- |
| `Gate - Arrival` | 518,883 | 481,295 | Arrivée et prairie, au sud-ouest |
| `Gate - Forest` | 580,760 | 565,000 | Forêt et route du port, au nord-est |

Les deux escaliers montent désormais **le long de la face intérieure des murailles**. La [correction d’implantation](starter-island/town/warmth-2026-10-20/wall-stairs-authoring.json) conserve les ouvertures dans les parapets et les rejoint par un court palier latéral plein. L’ouest comporte **28 marches**, sur **12,8 m**, et l’est **36 marches**, sur **13,4 m**, avec une largeur de **2,60 m** et un palier intermédiaire de **1,25 m**. Les contremarches sont de **17,88 et 17,16 cm** ; aucune rampe de collision ne remplace les marches. La [première révision physique](starter-island/town/watchtowers-stairs-2026-10-20/README.md) documente leur ancienne implantation perpendiculaire.

Le [raccord bas final de l’accès est](starter-island/town/warmth-2026-10-20/east-stair-wide-ground-transition.json) ajoute une marche de **75 cm de profondeur**, avec une contremarche supérieure de **12,68 cm**. Après retrait des mains courantes, la version sauvegardée valide **24 parcours d’accès, 80 parcours de guet et 44 parcours intérieurs**, ainsi que les deux circuits complets du chemin de ronde sur **397,093 m et 1 845 ticks**, sans tick aérien. Les [preuves actuelles](starter-island/town/warmth-2026-10-20/README.md#vérification) remplacent les mesures historiques ci-dessous pour cette implantation.

| Accès | Pied X, Z | Palier X, Z |
| --- | --- | --- |
| `Stairway - West` | 502,261 ; 518,997 | 504,628 ; 531,576 |
| `Stairway - East` | 590,272 ; 501,051 | 592,349 ; 514,289 |

Le déplacement serveur prend appui sur les surfaces statiques praticables, en conservant sa limite de pente de **40°**. Cet appui complète le Terrain et permet de suivre les marches et planchers. Le serveur reste propriétaire de la position. Les intérieurs et étages ont leurs [parcours de validation dédiés](starter-island/town/warmth-2026-10-20/interior-server-traversal.json) ; les toits ne constituent pas une nouvelle zone de circulation.

## Escaliers, guets et végétation — 20 octobre 2026

La [passe du 20 octobre 2026](starter-island/town/watchtowers-stairs-2026-10-20/README.md) ajoute aux deux escaliers physiques **quatre guets surélevés de 3 m**, chacun avec **20 marches de 15 cm**, et des nappes de lierre peint sur les courtines. Les guets combinent pierre, charpente et chaume ; la végétation conserve des ouvertures dans la maçonnerie et reste sous le chemin de ronde. Les nouveaux assets sont isolés sous `Town/RampartUpgrade/`.

L’authoring des deux escaliers réussit ses contrôles de géométrie. Le lierre compte **28 nappes**, **1 792 feuilles** et **cinq lots avec LOD**, avec son [contrôle séparé](starter-island/town/watchtowers-stairs-2026-10-20/climbing-plants-validation.json) réussi. Les guets passent leurs [80 parcours](starter-island/town/watchtowers-stairs-2026-10-20/watchtower-traversal.json), avec **2,039 m de largeur physique minimale**. Le [circuit complet](starter-island/town/watchtowers-stairs-2026-10-20/wall-circuit-traversal.json) réussit ses deux trajets sur **375,67 m et 1 749 ticks**, sans tick aérien. Le [parcours connecté hôte/client](starter-island/town/watchtowers-stairs-2026-10-20/connected-stair-traversal.json) rejoint ses quatre points et les [64 tests Terrain/déplacement](starter-island/town/watchtowers-stairs-2026-10-20/terrain-movement-tests.json) passent.

Les [24 parcours ciblés d’escalier historiques](starter-island/town/watchtowers-stairs-2026-10-20/stairs-server-traversal.json) ont validé la première implantation, à pied et en course, à 60 et 20 Hz, sur trois lignes latérales. Les [dix comparaisons avant/après](starter-island/town/watchtowers-stairs-2026-10-20/README.md#comparaison-visuelle) décrivent cette étape. La correction des escaliers le long des murs dispose de ses [rapports et captures actualisés](starter-island/town/warmth-2026-10-20/README.md). Les anciennes réussites V2 ci-dessous concernent la référence avec rampes continues.

## Fichiers et retouches

La révision de l’enceinte est portée par [RefineStarterIslandRamparts.cs](../../Unity3D/Arkya/AgentScripts/World/RefineStarterIslandRamparts.cs), et celle des rues par [RefineStarterIslandStreets.cs](../../Unity3D/Arkya/AgentScripts/World/RefineStarterIslandStreets.cs), hors `Assets`. Leurs ressources sont isolées sous **`Assets/Arkya/World/StarterIsland/Town/Ramparts/Refined/`** et **`Town/StreetsV2/`**. Les générateurs de révision refusent une révision déjà présente : modifier les objets existants et sauvegarder la scène pour les retouches suivantes.

`RefineStarterIslandRamparts.RebuildGeneratedRevision()` est une commande de régénération réservée à la construction du décor généré : elle supprime et remplace toute la racine de remparts possédée par le générateur. Elle ne préserve pas les retouches manuelles effectuées sous cette racine. Pour l’édition normale, conserver les objets existants.

La création initiale reste dans [BuildStarterIslandTown.cs](../../Unity3D/Arkya/AgentScripts/World/BuildStarterIslandTown.cs) et [BuildStarterIslandRamparts.cs](../../Unity3D/Arkya/AgentScripts/World/BuildStarterIslandRamparts.cs). Ces commandes décrivent la première implantation ; ne pas les relancer sur la ville actuelle. Le puits est construit par [StarterTownManaWell.cs](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Editor/StarterTownManaWell.cs), et l’ambiance par [StarterTownAmbience.cs](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Ambience/StarterTownAmbience.cs).

Les maisons réemploient les modules Arkya de `Assets/Arkya/Building/Structures/Prefabs/`. La pierre du village utilise les matériaux et textures propres de `Assets/Arkya/World/StarterIsland/Town/Warmth20261020/Surfaces/` ; la maçonnerie des remparts possède désormais ses dérivés sous `Town/RampartUpgrade/NaturalMasonry/`. `PaintedStoneV2.png` reste la référence d’une étape précédente. Les arbres et le cristal réemploient les prefabs approuvés de `Assets/Arkya/ArtDirection/Optimized/Prefabs/`. Les ressources Scinty restent séparées. Conserver les `.meta` avec tous les assets et la scène.

Les derniers accessoires, lots de végétation et dérivés de palette sont isolés sous `Town/LivedIn20261020/Props/`, `Gardens/`, `Materials/` et `Palette/`. Les ressources ajoutées à partir des références se trouvent dans `LushGardens/` — dont les anciennes versions `Meadow/` et `Canopies/`, et les dérivés actuels `MeadowFine/` et `CanopyDensity/` —, `CanopyFrame/`, `LightResponse/`, `ReferenceRoofs/`, `ReferenceWindows/` et `ReferenceAtmosphere/`, sous ce même dossier. Le [guide de cette passe](starter-island/town/lived-in-2026-10-20/README.md#sources-et-retouches) précise l’ownership de chaque helper et les limites des commandes `Main()`, qui ne doivent pas servir à écraser des retouches manuelles.

Préserver les ouvertures de portes, les deux accès d’escalier et les protections du chemin de ronde lors des retouches. Un matériau partagé modifie tous ses utilisateurs ; créer un asset distinct si une seule façade doit changer. Après une modification du Terrain, recaler les bâtiments, rues, fondations et supports, puis refaire les contrôles de passage.

## Lumière et mouvement

`StarterTownAmbience` conserve **19 lanternes ponctuelles**, dont quatre aux portes, avec les seuils de l’île : environ **0,15** d’intensité le jour et **3,8** la nuit, avec un léger vacillement. Seules les **six plus proches**, dans un rayon de **95 m** autour de la caméra active, éclairent à la fois ; la sélection est actualisée toutes les **0,75 s**.

Les deux lumières cyan du puits pulsent de **±10 %** autour des intensités d’authoring **4,5** et **2,5**. Les **21 lumières ponctuelles** ne projettent pas d’ombres. Les trois guirlandes portent **27 ampoules émissives**, sans lumière ponctuelle supplémentaire. Le bloom local utilise une intensité de **0,25**, un seuil de **1,2** et un volume de **130 × 80 × 130 m**, avec une transition de **12 m**. Les deux profils dérivés bornent son entrée HDR à **8** ; le profil de ville ajoute une légère chaleur. Le [correctif documenté du halo](starter-island/town/art-finish-2026-10-20/README.md#halo-et-ambiance) conserve les lumières, le soleil et le cycle horaire.

Les dix enseignes oscillent légèrement. Les quatre volumes de lucioles apparaissent la nuit ; cinq cheminées émettent une fumée lente et le puits conserve ses motes bleues. Le plafond cumulé est de **254 particules** : 18 par volume de lucioles, 22 par cheminée et 72 pour le puits. L’animation reste locale, sans état de gameplay répliqué, et se désactive dans les builds `UNITY_SERVER`.

Les dix lanternes flottantes jade et ambre conservent leur animation de présentation. Leurs matériaux dérivés utilisent les globals existants `_ArkyaDaylight` et `_ArkyaEnvironmentActive` : **2,5 % de l’émission source le jour, 100 % la nuit**. Les matériaux des runes et des fleurs sont conservés. Ce réglage ne change pas les lumières ponctuelles ni les profils de bloom.

La reprise visuelle du 20 octobre ajoute une réponse de lumière ambiante froide aux seuls matériaux dérivés de la passe. Cette réponse s’efface la nuit ; le soleil, la lune, les lanternes, les reflets et la météo conservent leur fonctionnement. Les fenêtres gardent une transparence partielle et gagnent une émission ambrée diurne et nocturne ; seize lanternes traditionnelles et vingt-sept ampoules de guirlande reçoivent également des matériaux distincts, sans modifier leurs composants de lumière. Le ciel dérivé concerne toute l’île : sa correction de bleu et ses nuages analytiques s’atténuent avec le crépuscule et la météo, sans modifier le cycle horaire, les lumières ou le brouillard. Le nouveau calcul des nuages augmente le travail du shader et n’a pas encore été profilé sur les appareils cibles. Les végétaux ajoutés gardent des masques de croissance pour leur vent et n’ajoutent aucune lumière dynamique.

## Passe de finition

La finition conserve le plan compact V2 et donne une fonction visuelle aux espaces libres : deux petits marchés garnis de pain et de produits, trois cours abritées avec banc, établi ou séchoir, et des bordures plantées. **385 pavés** prolongent la place et les raccords de rues ; les bordures sont jointives. Les **17 groupes plantés** utilisent le LOD 2 des petits végétaux approuvés, sans fusionner plusieurs niveaux d’un même prefab. Vingt linteaux ferment les jours entre les façades et les pignons des dix maisons. Les étals existants sont déplacés ; les références des enseignes et de l’ambiance sont conservées. Voir le [rapport des cours et du pavage](starter-island/town/polish/courts-finish.json).

La pierre des remparts reprend une échelle de texture métrique sur **65 meshes**. Des blocs de relief, du lierre au [matériau peint assourdi](starter-island/town/polish/foliage-finish.json), de la mousse et des joints de dalle complètent les surfaces. Les deux jours au-dessus des arches sont fermés ; les ouvertures de **8,5 m**, les escaliers et la largeur du chemin de ronde sont conservés. Le [rapport des remparts](starter-island/town/polish/wall-finish.json) compte 276 reliefs de pierre, 489 feuilles de lierre, 62 touches de mousse et 312 joints de sol. Ces ajouts restent décoratifs et n’ajoutent aucune lumière.

| Ajout de finition | Triangles | Renderers | Colliders |
| --- | ---: | ---: | ---: |
| Cours, pavage, plantations et linteaux | 72 308 | 12 | 51 |
| Habillage des remparts | 16 325 | 16 | 0 |

Ces comptes décrivent les ajouts, sans constituer une mesure de performance. Le relief conserve l’empreinte indiquée plus bas et l’éclairage reste à **21 lumières**, avec **six lanternes actives** au maximum. Aucun code de déplacement ou d’ambiance n’est modifié pendant cette passe.

Les sources sont [PolishStarterTownCourts.cs](../../Unity3D/Arkya/AgentScripts/World/PolishStarterTownCourts.cs) et [PolishStarterTownWalls.cs](../../Unity3D/Arkya/AgentScripts/World/PolishStarterTownWalls.cs), avec leurs assets sous `Assets/Arkya/World/StarterIsland/Town/Polish/`. Leurs commandes `Main()` refusent une finition déjà présente. `PolishStarterTownCourts.RebuildGeneratedFinish()` remplace entièrement la racine **Town finish - market courts and worn paving** : cette commande est réservée à la construction du décor généré et ne conserve pas les retouches manuelles sous cette racine. Pour les retouches normales, éditer les objets en place.

Après finition, les [passages de rues](starter-island/town/polish/street-passages.json) réussissent leurs **4 156 échantillons**, avec les dix portes de maison accessibles. Le [chemin de ronde](starter-island/town/polish/rampart-validation.json) conserve **3 483 contrôles de capsule et d’appui**, plus **306 contrôles aux portes**, sans échec. Le [parcours serveur](starter-island/town/polish/server-walk-validation.json) atteint tous ses points en **1 737 ticks**, sur environ **373 m**, sans tick aérien. Les 34 tests unitaires mentionnés ci-dessous appartiennent à la validation V2 et n’ont pas été relancés pendant cette finition.

[ReviewStarterTownPolish.cs](../../Unity3D/Arkya/AgentScripts/World/ReviewStarterTownPolish.cs) produit [neuf captures](starter-island/town/polish/captures.json) : ensemble et marché de jour et de nuit, cour du puits, cour de travail, porte, escalier et chemin de ronde. Les vues rapprochées sont à hauteur de personnage ; le helper restaure les états de présentation sans sauvegarder la scène.

## Revue et validation

Les résultats suivants documentent la référence V2, avant la passe de finition ; les contrôles actualisés sont liés ci-dessus.

Avec `StarterIsland` chargée en Edit Mode et l’éditeur connecté :

```powershell
$arkyaCli = 'C:/Program Files/Unity Hub/resources/unity.exe'
$arkyaProject = 'D:/Repositories/arkya/Unity3D/Arkya'

& $arkyaCli command run_script --file AgentScripts/World/RefineStarterIslandRamparts.cs --entry RefineStarterIslandRamparts.Validate --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/World/ReviewStarterTownV2.cs --entry ReviewStarterTownV2.Capture --project-path $arkyaProject --caller plugin --skill unity-cli --json
```

Le [contrôle géométrique de la seconde enceinte](starter-island/town/refined/rampart-validation.json) est **réussi** : **3 483 échantillons de capsule et autant de contrôles d’appui** sur les trajets du chemin de ronde et des escaliers, ainsi que **306 échantillons aux deux portes**, sans échec. Les trajets de ronde sont contrôlés sur trois lignes latérales avec une capsule de rayon **0,30 m**. Aucun script manquant ni matériau non supporté n’est détecté. L’enceinte comporte **102 408 triangles**, **75 renderers** et **593 colliders** ; ces comptes ne constituent pas une mesure de performance.

Le [rapport des rues](starter-island/town/ramparts-v2/streets-validation.json) confirme le déplacement des dix bâtiments et la conservation des hauteurs. Le [contrôle de passage des rues en Play Mode](starter-island/town/ramparts-v2/street-passages.json) est **réussi** : **4 156 échantillons**, dont les dix extrémités d’accès aux portes de maison, sans obstacle bloquant. Les axes et deux lignes décalées de **±0,40 m** sont contrôlés tous les **0,35 m** au maximum, avec une capsule de rayon **0,30 m**. Trois échantillons au départ des escaliers utilisent leur appui réel, avec une correction de hauteur bornée à **0,22 m**, uniquement sur les rampes nommées et sous la limite de **40°** ; ces rampes restent incluses dans le contrôle complet de capsule.

Le [parcours avec le déplacement serveur ECS](starter-island/town/refined/server-walk-validation.json) est **réussi** : deux trajets complémentaires couvrent l’ensemble du chemin de ronde, les deux ponts de porte, les deux montées et les deux descentes, sans saut ni téléportation entre les points. Les **1 737 ticks** parcourent environ **373 m**, avec **1 337 proxies de collision** installés, une capsule de rayon **0,30 m** et de hauteur **1,75 m**. Tous les points sont atteints, sans tick aérien.

Les [34 tests de déplacement Terrain et serveur](starter-island/town/refined/terrain-movement-tests.json) sont **réussis**, y compris montée et descente de rampe, pente trop forte, appui sur plateforme, marche trop haute et plafond bas. Les [dix contrôles d’ambiance en Play Mode](starter-island/town/refined/ambience-validation.json) sont également **réussis** : jour/nuit, enseignes, cristal, lucioles, plafond de particules, désactivation et réactivation, avec le budget de six lanternes actives.

[ReviewStarterTownV2.cs](../../Unity3D/Arkya/AgentScripts/World/ReviewStarterTownV2.cs) produit [six captures](starter-island/town/refined/captures.json) : ensemble de jour et de nuit, rue à hauteur de personnage, porte d’arrivée, escalier et chemin de ronde. Il restaure caméra, météo, horloge, éclairage, particules et profil de rendu, sans sauvegarder la scène pendant les aperçus.

Les rapports et captures des dossiers `town/` et `town/ramparts/` décrivent les implantations initiales ; leurs anciens comptes et parcours ne valident pas la seconde version. La heightmap complète conserve l’empreinte **`0fe6f9005188cc2f3f0ea5fad2a7f41f8561672201070929b767dfe9c73c0676`**, vérifiée avant et après les révisions. Les contrôles automatisés restent distincts d’un parcours manuel complet avec le personnage et ne valent ni validation de navigation des futurs PNJ ni test de charge.

## Conservation du Terrain binaire

`StarterIslandTerrain.asset` est un fichier natif binaire, malgré son extension `.asset`. Une exception `binary` dans `.gitattributes` préserve ses octets : la règle YAML générique supprimait 325 octets CR des séquences CRLF lors du passage par Git. La copie brute sauvegardée dans `artifacts/ui-integration/project/` correspond exactement au blob Git une fois cette normalisation appliquée ; elle a permis de restaurer intégralement les hauteurs et les huit couches avant la peinture locale de la ville. Ne jamais appliquer de conversion de fins de ligne à ce fichier.
