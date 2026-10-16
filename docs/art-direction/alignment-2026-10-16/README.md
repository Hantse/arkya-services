# Harmonisation de la DA — 16 octobre 2026

Cette passe rapproche le sol, la lumière et les quatre personnages de la finition
peinte des objets approuvés. Elle reste une proposition à examiner ; la validation
technique ne constitue pas une approbation artistique.

## Paysage et sol

La prairie V4 apporte des touches peintes plus définies et une palette sauge.
Les huit couches du Terrain mélangent des échelles et des décalages différents,
avec de la terre sous les 756 arbres et au contact de 228 roches. La peinture
2048² précise les raccords des chemins. La normal map sinusoïdale a été retirée
des trois couches de prairie. La heightmap et les neuf tracés restent identiques.

Le soleil de jour est plus neutre, l'ambiance moins contrastée et la saturation
du grading ramenée à zéro. Les feuilles d'arbres et de buissons reçoivent un léger
éclairage des deux faces ; les rameaux restent opaques. Les couleurs et textures
des références approuvées restent conservées.

| Vue Unity, même caméra et même heure | Avant | Après |
| --- | --- | --- |
| Forêt | [Avant](before/forest.png) | [Après](after/forest.png) |
| Refuge | [Avant](before/refuge.png) | [Après](after/refuge.png) |
| Crépuscule | [Avant](before/forest-dusk.png) | [Après](after/forest-dusk.png) |
| Nuit | [Avant](before/forest-night.png) | [Après](after/forest-night.png) |

![Forêt après la passe](after/forest.png)

La [correction du bois des trois maisons du refuge](refuge-wood/README.md)
traite ensuite le veinage géant et les UV étirés signalés sur un pignon.
Son [extension à toutes les constructions en bois](construction-wood/README.md)
couvre le kit modulaire, les clôtures, les panneaux et le ponton.

## Personnages

Le visage et les cheveux disposent de davantage de géométrie. Les couleurs de
sommets portent les creux de contact, les variations de pigment et les arêtes
chaudes. Peau, cuir, tissu, émail, métal et yeux ont des réponses lumineuses
distinctes. Le studio reçoit un éclairage plus neutre, deux remplissages sans
ombres, le tonemapping Neutral et le SMAA High.

Le foulard des silhouettes B retrouve ses plis et ses poids de déformation
corrects. La doublure est replacée sous les plaques des silhouettes A ; les
coques d'épaule et leurs liserés B retrouvent une géométrie continue. Les
attaches de bottes prévues par le design sont conservées. Avec une coiffure
courte, les modèles affichent environ 52 k triangles sur A et 63 k sur B
(environ 66 k avec cheveux longs sur B). Les coiffures alternatives restent
dans le fichier mais sont désactivées lorsqu'elles ne sont pas sélectionnées.

| Modèle | Corps | Portrait |
| --- | --- | --- |
| Humain A | [Vue Unity](after/human-a.png) | [Visage](after/human-a-portrait.png) |
| Humain B | [Vue Unity](after/human-b.png) | [Visage](after/human-b-portrait.png) |
| Elfe A | [Vue Unity](after/elf-a.png) | [Visage](after/elf-a-portrait.png) |
| Elfe B | [Vue Unity](after/elf-b.png) | [Visage](after/elf-b-portrait.png) |

La [capture de création antérieure](../../../Unity3D/Arkya/Assets/Screenshots/Characters/creation-active.png)
reste disponible ; son cadrage et sa résolution de rendu diffèrent des portraits
de cette revue. Les fichiers de référence, personnalisations, rigs de 26 os,
Idle/Walk/Run et contrats serveur restent conservés.

Les derniers portraits ont été rendus dans une copie de validation Unity avec
DirectX 11 après une erreur de périphérique DirectX 12 sur cet éditeur séparé.
Les quatre FBX de cette copie ont été comparés par empreinte aux fichiers du
projet actif. Aucun réglage graphique global du projet actif n'a été changé
pour ce contournement de capture.

## Vérification et coût

- Unity 6000.6.6f1 : 45 tests Edit Mode de l'environnement réussis.
- 34 contrôles réels du cycle, de l'ambiance et du rendu en Play Mode réussis.
- 17 contrôles GPU du feuillage réussis, dont mouvement à trois LOD et immobilité
  lorsque le vent est nul.
- Peinture : zéro échantillon de hauteur modifié, neuf routes inchangées,
  poids normalisés (erreur maximale 2,98e-7), shader Terrain URP conservé.
- Huit couches restent réparties sur deux textures de contrôle : 32 Mio bruts
  RGBA8 à 2048², contre 8 Mio auparavant. Les textures peintes sont réutilisées.
- Quatre FBX réimportés et vérifiés : 26 os, quatre influences au maximum,
  poids normalisés et boucles Idle/Walk/Run continues.
- 32 tests de personnages réussis dans le projet de validation séparé, dont
  les quatre rigs, l'interface et deux connexions locales serveur/client avec
  déplacement autoritaire, convergence et destruction des mondes à la sortie.
  Le test de déformation échantillonne huit poses ; les contrôles réseau
  attendent les snapshots dans un délai réel borné au lieu d'un délai fixe.
- Les empreintes de 80 fichiers des références approuvées arbre/mana sont
  inchangées ; les réglages globaux QualitySettings et GraphicsSettings ont
  également été comparés à la sauvegarde de début de passe.

Rapports : [sol](ground-validation.json), [tests environnement](environment-tests.json),
[Play Mode](environment-runtime.json), [GPU feuillage](foliage-validation.json),
[imports personnages](after/character-capture-validation.json),
[tests personnages](character-tests.json),
[console de validation](character-console.json),
[roundtrip FBX](../resources/personnages/game/fbx-roundtrip-validation.json),
[intégrité des références](integrity.json).

Les performances d'un build et les foules de personnages n'ont pas été mesurées.
Les tests personnages portent sur l'instantané utilisé pour cette passe. La
[comparaison finale](character-source-integrity.json) confirme les modèles,
matériaux, shader, scène, builder et test de cette passe identiques ; elle
identifie aussi dix fichiers de services, d'interface et de tests sociaux
modifiés ou ajoutés en parallèle dans le projet actif. Ces changements
concurrents ne sont pas couverts par ce résultat de 32 tests.
Les LOD des personnages, le rig facial et les animations de combat restent à
préparer. La lecture du sol aux angles rasants mérite encore une revue en
mouvement ; le traitement ne prétend pas supprimer toute répétition.

## Fichiers de production modifiés

Sous `Unity3D/Arkya/` :

- `Assets/Arkya/World/Environment/Runtime/DayNightEnvironment.cs`.
- `Assets/Arkya/World/Environment/Shaders/PaintedFoliageWind.shader` et `.hlsl`.
- `Assets/Arkya/World/StarterIsland/Textures/PaintedMeadowV4.png` et son `.meta`.
- `Assets/Arkya/World/StarterIsland/Terrain/Environment/*V4.terrainlayer` et leurs `.meta`.
- `Assets/Arkya/World/StarterIsland/Terrain/StarterIslandTerrain.asset`.
- `Assets/Arkya/World/StarterIsland/Materials/Environment/PaintedGrading.asset`,
  `Wind/PaintedWind07.mat`, `PaintedWind13.mat` et `PaintedWind14.mat`.
- `Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity`.
- `Assets/Arkya/Characters/Models/{human,elf}-{a,b}.fbx`.
- `Assets/Arkya/Characters/Shaders/PaintedCharacter.shader`.
- `Assets/Arkya/Characters/Editor/CharacterCreationBuilder.cs`.
- `Assets/Arkya/Characters/Tests/Editor/CharacterCreationRuntimeTests.cs`.
- Les matériaux de personnage, `Settings/CharacterStudio.asset` et son `.meta`,
  ainsi que `Scenes/CharacterCreation.unity`.

Les générateurs et outils de reprise sont conservés dans `AgentScripts/World/`
(`HarmonizeIslandGround`, `AddIslandFoliageWind`, `UpgradeStarterIslandEnvironment`,
`CaptureArtAlignment`) et `AgentScripts/ArtDirection/CaptureCharacterAlignment.cs`.
Les sources Blender, exports, contrôles et scripts de personnages sont dans
`docs/art-direction/resources/personnages/game/` et `scripts/`.
La [provenance exacte de la nouvelle texture](../../world/starter-island/painted-meadow-v4-provenance.md)
est conservée. Les anciennes textures V3 restent disponibles.
