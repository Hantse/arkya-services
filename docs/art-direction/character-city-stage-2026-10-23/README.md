# Sélection et création dans Aubefont — révision du 23 octobre 2026

Cette révision remplace la présentation de la [première passe des deux vues](../character-flow-2026-10-23/README.md), rejetée visuellement. Elle conserve son parcours de sélection et de création, tout en installant le personnage dans un véritable décor 3D de ville. Après une première version de jour, la demande d’une ambiance plus mystique conduit à la version actuelle au crépuscule, avec un puits de mana animé. Les études raster restent des recherches de composition ; elles ne constituent pas le fond affiché.

## Direction et parcours

La cible est un écran de personnages MMORPG intégré à un quartier habité : charpentes épaisses, toits sculptés, pierre peinte, végétation et lanternes. Les [références de ville fournies le 20 octobre](../references/town-2026-10-20/README.md), notamment l’atelier, le village aux lanternes et l’auberge avec son jardin, guident la composition. La cour reste dégagée pour la Valkyrie ; l’architecture et la végétation donnent de la profondeur derrière les panneaux.

La sélection s’ouvre avec la liste du compte à droite et les actions au bas du panneau. **Créer un perso** ouvre la vue dédiée, avec les choix d’apparence, l’aperçu et le nom. **Retour aux personnages** abandonne le brouillon et restaure la sélection précédente ; une création confirmée par Characters revient à la liste. L’essai natif sur l’île peut partir d’un brouillon sans compte et le restitue au retour. Ces règles restent celles du [guide de création](../../CHARACTER_CREATION.md).

L’interface utilise UI Toolkit, avec panneaux sombres, accents cuivre et commandes autour de l’aperçu. Les onglets **Apparence** et **Nom & création** donnent accès aux commandes sur les surfaces compactes. Les thèmes d’interface continuent de s’appliquer.

## Ambiance mystique

Le quartier reste entièrement en 3D. Un ciel peint bleu profond, une lumière lunaire et les fenêtres ambrées donnent la tonalité du crépuscule. Un éclairage chaud réservé au personnage maintient la lecture du visage et des matières. Les étoiles restent discrètes au fond du décor.

Des lucioles de mana cyan et vert pâle dérivent dans la cour, tandis qu’une brume basse se déplace lentement entre les bâtiments. Ces effets utilisent des ParticleSystems natifs et la texture radiale existante des motes du puits de mana. Leurs matériaux propres au studio résident dans `CityStage/Materials/`. Le bloom reste limité pour conserver les silhouettes, la lisibilité du personnage et les contrastes de l’interface.

Un puits de mana d’Aubefont, réduit à la moitié de sa taille, apporte un point magique sur le côté de la cour. Son cristal flotte et tourne ; sa surface de mana se déplace et de petites particules montent du bassin. Les deux animations natives et les particules ont été contrôlées en Play, puis le puits a été inspecté dans les vues de sélection et de création.

Le générateur intègre désormais l’atmosphère et le puits. Les scripts de mise au point dans `artifacts/character-ui-redesign-2026-10-23/` restent un historique de travail ; ils ne sont plus nécessaires à la reconstruction.

## Décor natif et séparation du gameplay

`Unity3D/Arkya/Assets/Arkya/Characters/Editor/CharacterCityStageBuilder.cs` compose la racine `Character city stage` dans `Assets/Arkya/Characters/Scenes/CharacterCreation.unity`.

Le générateur ouvre temporairement `Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity` dans une scène de prévisualisation. Sous `Aubefont - mana well town`, il reprend l’auberge, l’atelier des runes, l’herboristerie et la maison des tisserands, ainsi que leurs compléments de toiture. Les maisons sont rapprochées et orientées vers la cour ; l’hôtel de ville ferme la perspective au fond. Les prefabs optimisés `Arbre`, `Buisson` et `Fleur`, sous `Assets/Arkya/ArtDirection/Optimized/Prefabs/`, apportent la végétation.

La copie conserve les transformations, MeshFilters, MeshRenderers et groupes de LOD utiles. Pour le puits, elle reprend explicitement les deux composants Animation et le ParticleSystem natif avec son renderer, en vérifiant qu’aucune référence ne dépend d’un objet de la scène source. Elle référence les meshes, matériaux et clips existants. Elle ne copie pas les MonoBehaviours, les colliders ou les systèmes de gameplay de l’île. Le décor n’exécute donc pas de jeu autoritaire et n’exige pas le chargement de l’île jouable à l’ouverture de la sélection. La scène de prévisualisation est fermée sans sauvegarde.

La cour est assemblée en meshes par palette de pierre. Ses assets et les copies de matériaux propres à cette présentation sont conservés dans `Assets/Arkya/Characters/CityStage/`. Le ciel peint vient de `Assets/Arkya/World/StarterIsland/Town/LivedIn20261020/ReferenceAtmosphere/ClearBluePaintedSky.mat`. Le générateur règle les lumières du studio, l’ambiance, le brouillard et le profil de post-traitement, dont les composants sont enregistrés comme sous-assets. Deux lanternes complètes d’Aubefont portent des lumières ponctuelles sans ombres, centrées dans leurs verres. Les clips d’ambiance existants de vent et d’oiseaux complètent la présentation. L’ancienne poussière de jour est remplacée par les effets mystiques. Les assets sources et leurs GUID restent préservés.

L’exploration conserve son propre parcours serveur/client local. `CharacterCreationFlow` masque les racines du studio au départ et restaure leur visibilité et les réglages de rendu au retour. Le décor de sélection reste de la présentation, distincte de cette simulation.

## Caméra et reconstruction

`CharacterCreationScreen` mesure la région libre de l’interface et la transmet à `CharacterPreviewPresenter`. La caméra conserve `rect = (0, 0, 1, 1)` et rend la ville sur toute la surface. Le presenter décale le centre optique dans sa matrice de projection pour placer le personnage dans cette région. Il ajuste la distance à la géométrie visible, à la taille choisie, à la rotation et au mode silhouette ou portrait. Le départ en essai libère cette projection personnalisée.

Le menu **Arkya → Characters → Rebuild 3D city stage** reconstruit le décor complet au crépuscule, le puits animé, les effets et le profil de post-traitement. Il exige que la scène de création soit active et enregistrée, hors Play. Il remplace la racine du décor, actualise les assets qui lui appartiennent et sauvegarde la scène de création. Il ne sauvegarde pas la scène source de l’île. Cette reconstruction complète a été exécutée avec succès dans l’Editor.

## Validation

La variante mystique reconstruite passe **14 tests ciblés sur 14**, sans échec ni test ignoré, dans Unity **6000.6.6f1**, URP **17.6.0**, en **62,33 secondes**. Le [rapport JSON](validation.json) conserve le job final, ses cas et la base de jour historique.

Les tests couvrent les deux vues, les brouillons, les trois thèmes, le cadrage dans la région libre, les tailles/apparences et rotations, le portrait puis la restitution de la projection. Les parcours de l’île vérifient les mondes serveur/client locaux, leurs mouvements et leur nettoyage, ainsi que deux retours au décor avec restauration du rendu. Les layouts sont exercés en **1600 × 900**, **980 × 700**, **430 × 900** et **830 × 1736** points de panneau, avec douze personnages et des commandes accessibles à 600 points de hauteur.

La sélection, la création et le portrait avec le puits ont été inspectés dans la Game View, avec des captures de **1920 × 962 pixels**. Après reconstruction, l’inspection relève :

- **815 renderers** et **55 groupes de LOD**, niveaux de LOD et éléments désactivés inclus ; le nombre de renderers n’est pas un compte de draw calls.
- **Aucun mesh manquant, aucun mesh transitoire et aucun collider** dans le décor.
- Trois ParticleSystems actifs et bornés : **66 lucioles observées sur 80 maximum**, **15 nappes de brume sur 18** et **10 motes du puits sur 24**.
- Quatre lumières dans la racine du décor : deux points ambrés, un point cyan et un spot pour le visage ; deux sources audio d’ambiance.
- Deux animations natives actives pour le cristal et la surface du puits.

Après les captures, l’Editor est **hors Play**, la scène `CharacterCreation` est propre et la console finale contient **zéro erreur**. Aucun mesh ne manque ; le ciel à étoiles discrètes et le profil `Mystic courtyard grading.asset` sont conservés. La fenêtre Game View retrouve exactement le rectangle relevé avant cette passe : **x 0, y 23, largeur 1920, hauteur 983**. Ce rectangle de fenêtre est distinct des dimensions de l’image rendue.

Les trois personnages des captures sont des fixtures injectées uniquement en mémoire. Aucun compte réel n’a été modifié. L’authentification Discord et la création auprès des services réels ne sont pas validées par cette passe. Aucun nouveau build autonome ou Web n’a été produit. Les sorties des tests réseau contiennent des avertissements de regroupement de ticks serveur ; cette passe n’est pas une mesure de performance. Cette revue ne vaut pas validation artistique par l’utilisateur.

### Historique — base de jour

Le décor de jour avait passé la même sélection de **14 tests**, sans échec ni test ignoré, en **59,57 secondes**. Il comptait 717 renderers incluant les LOD et objets désactivés, 54 groupes de LOD, deux lumières ponctuelles et une émission de poussières plafonnée à 25 particules. Ce bilan est conservé comme comparaison historique ; il précède le puits et les effets mystiques.

## Fichiers modifiés

Dans `Unity3D/Arkya/Assets/Arkya/Characters/` :

- `Editor/CharacterCityStageBuilder.cs` et `Editor/CharacterCreationBuilder.cs` : composition et reconstruction du décor.
- `Scenes/CharacterCreation.unity` et `CityStage/` : scène, meshes de cour, matériaux, profil de post-traitement et fichiers `.meta`.
- `Runtime/CharacterPreviewPresenter.cs` : projection plein écran, cadrage et remise à zéro.
- `Runtime/UI/CharacterCreation.uxml`, `CharacterCreation.uss`, `CharacterCreationScreen.cs`, `InterfaceSkin.cs` : panneaux, liste, commandes et styles.
- `Tests/Editor/CharacterPreviewFramingTests.cs` et `CharacterCreationLayoutTests.cs` : géométrie du cadrage et accessibilité des commandes.

Le README racine, le guide de création et ce rapport décrivent la révision. Les packs tiers et les assets sources de l’île restent inchangés par cette passe.

## Vidéo d’ambiance

[Voir la vidéo de l’ambiance mystique](../../../artifacts/character-atmosphere-video-2026-10-23/Arkya-ambiance-mystique.mp4) : **15,00 secondes**, **360 images à 24 i/s**, **1280 × 640**, H.264 et audio AAC mono à 48 kHz, environ **4,8 Mo**.

L’enregistrement provient de la Game View réelle, capturée en fin de frame avec `MediaEncoder` en **1920 × 962**, puis redimensionnée. Un travelling temporaire et l’interface masquée permettent d’observer les lucioles, la brume et le cristal animé. Ces réglages de capture n’ont pas été sauvegardés dans la scène.

La capture vidéo étant muette, la piste sonore a été remontée avec les mêmes clips `Wind.wav` et `Birdsong.wav`, aux volumes respectifs **0,045** et **0,025**. Les images du début, de **7 s** et de **14 s** ont été contrôlées visuellement ; le décodage des **360 images** s’achève sans erreur.

## Captures

Sélection avec liste de démonstration :

![Sélection au crépuscule dans Aubefont, avec puits de mana](mystic-selection.png)

Création dédiée :

![Création dans la cour mystique avec aperçu Valkyrie](mystic-creation.png)

Portrait :

![Cadrage du visage dans la ville au crépuscule](mystic-portrait.png)

### Historique — décor de jour

[Sélection peuplée](selection-roster.png), [création](creation.png), [portrait](portrait.png), [sélection vide](selection-empty.png) et [thème Journal](selection-journal.png) conservent la première version du décor. Ces images précèdent la variante mystique actuelle.
