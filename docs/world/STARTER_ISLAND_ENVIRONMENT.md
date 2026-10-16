# Île de l’Aube — ciel, ambiance et finition peinte

Cette passe complète la [sculpture des niveaux 1 à 6](STARTER_ISLAND.md) dans le projet actif `Unity3D/Arkya`, sous Unity 6000.6.6f1 / URP 17.6.0. La scène reste [StarterIsland.unity](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity). Le terrain natif, les neuf routes et les données de progression restent les supports d’authoring de l’île.

## Cycle de quatre heures

[DayNightClock](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Runtime/DayNightClock.cs) calcule une phase normalisée sur **14 400 secondes réelles** : un tour complet du soleil et de la lune dure quatre heures. Une heure du monde correspond donc à dix minutes réelles.

| Phase | Heure du monde | Repère |
| --- | --- | --- |
| `0` | 00:00 | Minuit, lune haute, étoiles visibles |
| `0.25` | 06:00 | Lever du soleil |
| `0.5` | 12:00 | Midi |
| `0.75` | 18:00 | Coucher du soleil |
| `1` → `0` | 00:00 | Raccord du cycle suivant |

[DayNightEnvironment](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Runtime/DayNightEnvironment.cs) applique cette phase au ciel, aux lumières directionnelles, à l’ambiance et au brouillard. Les directions du soleil et de la lune sont opposées ; leur élévation maximale vaut environ 60°. Le shader [PaintedSky](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Shaders/PaintedSky.shader) dessine les deux disques, une lune marquée de cratères, des étoiles de deux tailles, une bande stellaire discrète et des nuages mobiles. La nuit réduit et refroidit la lumière ambiante ; les transitions du ciel, du brouillard et des lumières sont progressives. Une seule lumière directionnelle porte des ombres à la fois.

Dans l’Inspector du composant **Day Night Environment** :

- `Cycle Seconds = 14400` fixe la durée du tour complet.
- `Use Utc Clock` utilise l’horloge commune pour la présentation en Play Mode.
- `Preview Override` et `Preview Time 01` figent la revue dans l’éditeur. Le Play Mode démarre sur l’horloge réelle, même si l’aperçu était coché dans la scène.
- `Cloud Coverage`, `Cloud Speed` et `Orbit Azimuth` règlent la couverture, le mouvement et l’axe de l’orbite.

Pour un outil de capture ou de revue, `SetPreviewTime(phase)` fige explicitement le cycle, y compris en Play Mode ; `ClearPreview()` reprend l’horloge. `ApplyTime(phase)` applique seulement un échantillon et ne change pas le mode d’horloge. Le composant utilise une instance temporaire du matériau de ciel et restaure les paramètres de rendu et les lumières lorsqu’il est désactivé.

## Horloge des copies par realm

La présentation utilise par défaut les secondes UTC depuis l’époque Unix, modulo 14 400. Des copies de l’île démarrées à des moments différents retrouvent ainsi la même phase si leurs horloges système sont à l’heure. Ce calcul n’attribue aucune autorité de gameplay au client.

`SetServerTimeSample(unixSeconds)` permet de corriger cette horloge visuelle à partir d’un futur échantillon serveur de confiance ; `ClearServerTimeSample()` retire la correction. Le transport de l’heure, l’admission aux copies et l’orchestration par realm restent à brancher. Si le jour ou la nuit doit conditionner des apparitions, quêtes ou récompenses, le serveur décide avec sa propre horloge ; les clients affichent le résultat.

## Ambiance de présentation

[IslandAmbience](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Ambience/IslandAmbience.cs) porte uniquement des effets locaux : oiseaux décoratifs en vol, particules diurnes et nocturnes, lumières chaudes vacillantes et fond sonore. Les effets nocturnes augmentent lorsque le soleil passe sous l’horizon ; les chants d’oiseaux laissent place aux insectes. L’intensité forestière dépend de la position de la caméra.

La scène contient cinq volumes de pollen et trois de papillons pour le jour, cinq volumes de lucioles pour la nuit, neuf oiseaux décoratifs et trois petits foyers. Leurs trois lumières ponctuelles portent à 9 m et ne projettent pas d’ombres. Cinq boucles sonores originales — vent, ressac, oiseaux, insectes et feu — alimentent sept sources, dont trois crépitements spatialisés près des foyers. Elles sont synthétisées par [BuildIslandAmbience](../../Unity3D/Arkya/AgentScripts/World/BuildIslandAmbience.cs) puis enregistrées en WAV mono 16 bits à 22 050 Hz.

Le mouvement du feuillage et des arbustes est porté par les matériaux de vent de la scène. `Whole Plant Sway Enabled` est **désactivé par défaut** : `IslandAmbience` ne fait alors tourner aucun arbre entier, afin de garder les troncs et les racines stables. L’API `WholePlantSwayEnabled` règle le même mode sans changer le contrat de `Configure`. Passer cette propriété à `false` restaure immédiatement les rotations de repos ; décocher la case en Play Mode les restaure à la frame suivante. Ce réglage ne coupe ni les sons, ni les oiseaux, ni les particules.

Dans la hiérarchie, **Foliage wind - leaves and shrubs** porte [FoliageWind](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/FoliageWind.cs). `Strength` règle l’intensité, `Speed` la vitesse et `Direction` la direction horizontale du vent. L’intensité livrée vaut **0,85** ; mettre `Strength` à zéro arrête la déformation. Le shader [PaintedFoliageWind](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Shaders/PaintedFoliageWind.shader) combine une brise, des rafales progressives et un frémissement plus fin des feuilles. Le bas des arbustes est masqué pour rester fixé au sol. Les positions et collisions de gameplay ne bougent pas.

`Emit Particles` commande [FoliageWindParticles](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/FoliageWindParticles.cs), désormais enregistré et configuré dans la scène `StarterIsland` : feuilles peintes qui dérivent depuis les couronnes, pollen et petits grains autour des plantes proches. Les trois systèmes suivent la direction, la force et la vitesse du vent, avec **200 particules maximum** au total. Les douze sources les plus proches sont sélectionnées dans un rayon de 48 m, une fois par seconde. Sortir de la végétation, couper le vent ou sa vitesse vide les effets ; leur désactivation libère les objets, textures et matériaux temporaires. Le composant référence les 1 218 plantes distinctes couvertes par le contrôleur de vent ; seuls ses effets temporaires sont créés en Play Mode.

Trois copies de matériaux propres à l’île couvrent **3 654 renderers** sur les trois LOD : feuillage des arbres, feuilles des arbustes et rameaux des arbustes, ces derniers avec une amplitude réduite. Les textures et les couleurs des sommets de la DA sont conservées ; les matériaux et préfabs source restent inchangés. La déformation est partagée par les passes de couleur, d’ombre, de profondeur et de normales. L’enveloppe de culling des renderers est temporairement élargie puis restaurée, sans modifier les meshes source. [AddIslandFoliageWind](../../Unity3D/Arkya/AgentScripts/World/AddIslandFoliageWind.cs) conserve l’installation dans la scène.

L’ancien balancement reste disponible en activant explicitement `Whole Plant Sway Enabled`. Il est borné à **90 objets proches**, dans un rayon configuré par `Foliage Distance`, avec une sélection toutes les 1,25 seconde ; `Wind Strength` règle uniquement l’amplitude de ce mode historique. `Volume` règle le niveau sonore d’ensemble. Les sons, particules et rotations de repos sont arrêtés ou restaurés à la désactivation. Les oiseaux et insectes décoratifs ne sont ni des ennemis ni des entités de simulation.

Pour vérifier le vent, conserver une caméra et une lumière fixes pendant plusieurs frames : les feuilles et l’extrémité des arbustes doivent bouger, tandis que la position, la rotation et l’échelle des racines restent identiques. Contrôler aussi les ombres et les trois LOD : la déformation doit suivre dans les passes de rendu et d’ombre, sans saut lors des transitions. Une variation de paramètres de matériau ou une image fixe ne prouve pas un mouvement effectif à l’écran ; comparer deux rendus espacés dans le temps et garder la console sans erreur pendant la mesure.

La [validation GPU du vent](starter-island/foliage-wind/gpu-validation.json) passe **17 contrôles en Play Mode**. Une caméra et un éclairage fixes rendent les vrais meshes à deux instants de vent : arbre et arbuste présentent des pixels différents pour chacun des trois LOD, et strictement aucun pixel différent lorsque l’intensité vaut zéro. Le contrôle vérifie aussi la conservation des textures, l’absence de rotation des arbres et la restauration des paramètres globaux. Les vues [arbre A](starter-island/foliage-wind/Arbre-pose-a.png), [arbre B](starter-island/foliage-wind/Arbre-pose-b.png), [arbuste A](starter-island/foliage-wind/Buisson-pose-a.png) et [arbuste B](starter-island/foliage-wind/Buisson-pose-b.png) conservent les rendus isolés. Les **34 contrôles d’ambiance** sont également rejoués avec le nouveau mode : racines stables, sons et particules conservés, console sans erreur.

La [validation des particules](starter-island/foliage-wind/particle-validation.json) a été rejouée dans la scène `StarterIsland` réelle et réussit **neuf contrôles en Play Mode** : émission près des arbres et arbustes de l’île, absence de particules forestières sur l’océan, arrêt pour un vent ou une vitesse nuls, reprise, compatibilité des courbes de vitesse et nettoyage. Le relevé retrouve 66 particules près de la végétation, dans le budget de 200, zéro sur l’océan et aucun système vivant après désactivation. L’essai isolé initial reste un contrôle de composant ; cette nouvelle exécution vérifie son branchement dans l’île.

Le [contrôle du chargement automatique](starter-island/foliage-wind/automatic-hookup.json) vérifie également la création des trois systèmes à partir d’un LODGroup, leur désactivation par `Emit Particles` et leur réactivation. Les matériaux DA source contrôlés conservent leurs empreintes antérieures à cette passe. Ce branchement automatique reste disponible pour les scènes qui ne possèdent pas encore de composant particules ; `StarterIsland` contient désormais le composant configuré.

## Finition et direction artistique

La version courante du sol est la **V4**, issue de la
[passe d'harmonisation du 16 octobre 2026](../art-direction/alignment-2026-10-16/README.md) :
nouvelle prairie peinte, huit couches à 2048², raccords de sous-bois et de roches,
lumière du jour plus neutre et éclairage doux des deux faces des feuilles.
`HarmonizeIslandGround.Main` applique la peinture sans changer le relief ni les
routes et laisse l'enregistrement à l'appelant. Les paragraphes V3 ci-dessous
conservent l'historique de la première finition.

La [météo de l’île](STARTER_ISLAND_WEATHER.md) ajoute couvert, pluie et orage à ce cycle, avec des transitions progressives du ciel, de la lumière, du vent et des sons. Ses effets restent une présentation locale.

Les objets de végétation et de roche réemploient les [références approuvées](../art-direction/resources/README.md) et leurs [dérivés Unity à trois LOD](../art-direction/unity-test/lod/README.md). Les meshes riches, normales lissées et textures peintes de ces objets sont conservés. Les références d’origine et les packs Scinty restent distincts des ajouts de l’île.

L’habillage rassemble **756 arbres** et **environ 3 700 instances d’objets DA réutilisés au total**, répartis en bosquets, lisières et jardins de bord de route. Le refuge utilise des maisons de bois et chaume, des bancs et des repères sculptés ; le sanctuaire reçoit des marches arrondies et des colonnes travaillées. Ces assemblages prolongent le décor de revue et ne constituent pas une nouvelle approbation artistique des modules de construction. La passe conserve la heightmap native ; sa peinture et son habillage sont distincts de la sculpture des pentes.

Les nouvelles textures [PaintedMeadowV3.png](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Textures/PaintedMeadowV3.png) et [PaintedEarthV3.png](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Textures/PaintedEarthV3.png) proposent une base de prairie olive et sauge plus calme et une terre de chemin ocre brune. La prairie utilise des glacis continus plutôt que des touffes distinctes imprimées au sol : les herbes et fleurs proches restent de vrais objets 3D. Les deux PNG ont été générés avec l’outil intégré imagegen et copiés à l’identique ; les prompts et empreintes de [la prairie](starter-island/painted-meadow-v3-provenance.md) et de [la terre](starter-island/painted-earth-v3-provenance.md) sont conservés. La V2 et [sa provenance](starter-island/painted-meadow-provenance.md) restent disponibles. La génération demande un raccord sur les deux axes ; la répétition doit également être examinée dans Unity. Ces textures constituent des propositions de sol pour l’île ; elles ne modifient pas les références d’objets approuvées.

[RefineIslandGround](../../Unity3D/Arkya/AgentScripts/World/RefineIslandGround.cs) conserve le Terrain et son shader URP natif : la prairie mélange des répétitions à 7 m et 23 m pour atténuer le motif, et la terre utilise une répétition à 4 m. La peinture passe à **1 024²**, avec des bords de chemins irréguliers, des poches de terre autour des bâtiments et un relief de normales très léger. Une sixième couche, `WoodlandEarthV3`, ajoute des plaques de terre plus sombre autour des 756 troncs, avec une transition progressive sur environ 5 à 8 m selon l’échelle de l’arbre, sans occuper les chemins. Cet outil réécrit la peinture du sol ; les hauteurs et les largeurs physiques de circulation restent conservées.

Les **six couches** du sol final sont la prairie, le sable, la roche, la prairie à grande échelle, la terre du chemin et la terre de sous-bois. Elles utilisent une rugosité constante : `smoothnessSource = ConstantOnly`, avec une faible valeur de lissage. Le mode `DiffuseAlphaChannel` interprète l’alpha opaque de ces textures comme un lissage maximal et crée de larges reflets blancs aux angles rasants ; il ne convient pas à ce sol peint mat. Les cinq couches décrites dans le guide de sculpture initial correspondent à la V1/V2.

Le rendu doit être évalué à hauteur de joueur. Les anciens panoramas observaient des arbres de dix mètres depuis plusieurs centaines de mètres : le feuillage devenait une géométrie de quelques pixels, les petits objets disparaissaient et les ombres sortaient de la portée de 50 m du pipeline PC existant. Une vue de carte conserve son utilité pour le parcours, mais ne mesure pas la finition des surfaces ni les contacts au sol.

L’eau observe la lumière du cycle : ses couleurs, reflets et écume évoluent avec le jour, tandis que de larges vagues douces animent la surface. Les paramètres globaux de présentation utilisés par l’eau sont restaurés à la désactivation de l’environnement.

La [passe de vagues et de berges](STARTER_ISLAND_WAVES.md) ajoute un relief géométrique proche, des fronts d’écume vers le rivage et une houle plus forte dans l’eau mauve, avec un maillage borné autour de la caméra.

Une [couronne d’eau violette bouillonnante](STARTER_ISLAND_BOUNDARY_WATER.md) marque aussi la limite extérieure de l’île, à partir du rayon 835 m. Le bouillonnement signale le danger ; la règle de mort après cinq secondes d’exposition, déclenchée par le premier contact avec l’eau, est possédée séparément par la simulation serveur ECS.

## Rendu et déplacement de revue

[IslandRenderProfile](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/IslandRenderProfile.cs) applique le pipeline dédié référencé par `Pipeline` au moyen de l’override **global temporaire** `QualitySettings.renderPipeline`. Le Play Mode l’active automatiquement et le composant restaure la valeur précédente lorsqu’il est désactivé. `Editor Preview` est **désactivé dans la scène livrée** : les réglages PC du projet restent actifs en Edit Mode.

L’override de `QualitySettings.renderPipeline` peut être sérialisé dans `QualitySettings.asset` lors d’un `SaveAssets`. Pour enregistrer la scène, le workflow désactive donc l’aperçu du profil, restaure explicitement le pipeline PC et son anticrénelage global à 0, puis contrôle le fichier sauvegardé. Le profil de l’île reste un réglage de revue, à mesurer sur les plateformes cibles.

Après restauration par l’API Unity et enregistrement, les empreintes **SHA-256 de `QualitySettings.asset` et `GraphicsSettings.asset` sont exactement identiques à celles de la sauvegarde initiale**. Les nouveaux réglages restent portés par le profil dédié de l’île.

Le profil de l’île configure une portée d’ombres de **240 m**, une carte principale **2 048²**, **quatre cascades** et le **MSAA ×4**. La caméra conserve aussi le SMAA de haute qualité. Ces réglages servent la lecture des silhouettes et des contacts au sol ; ils ne constituent pas une validation de performance du futur client MMORPG.

En Play Mode, [IslandReviewCamera](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/IslandReviewCamera.cs) permet d’inspecter la scène :

- Maintenir le bouton droit : regarder avec la souris et se déplacer avec `W/A/S/D`.
- `Q/E` : descendre ou monter ; `Maj` : multiplier la vitesse par quatre.
- Molette : régler la vitesse ; `Échap` : libérer la souris.

La caméra reste au moins à 1,8 m au-dessus du Terrain tant qu’elle se trouve dans son emprise. La souris est restaurée à la désactivation ou à la perte de focus. Il s’agit d’une caméra de revue libre, pas du contrôleur de personnage ni de ses collisions de gameplay.

## Contrôles de revue

Captures de la passe : [refuge de jour](starter-island/environment/refuge-day.png), [coucher du soleil](starter-island/environment/refuge-dusk.png), [refuge de nuit](starter-island/environment/refuge-night.png), [forêt à hauteur proche du joueur](starter-island/environment/forest-day.png), [forêt nocturne](starter-island/environment/forest-night.png), [lune et étoiles](starter-island/environment/moon-and-stars.png), [soleil et nuages](starter-island/environment/sun-and-clouds.png). Le [script de mise à niveau](../../Unity3D/Arkya/AgentScripts/World/UpgradeStarterIslandEnvironment.cs) conserve la création et les prises de vue ; son entrée `Main` refuse de remplacer une île déjà mise à niveau pour préserver les retouches manuelles.

Pour une même caméra proche du terrain, contrôler au minimum midi, le coucher et minuit : lisibilité du chemin, ombres au contact, couleur des surfaces peintes, hauteur des végétaux, soleil/lune et étoiles réellement visibles. La nuit doit rester lisible avec une ambiance refroidie et des lumières locales contenues. Faire avancer le Play Mode pour observer nuages, feuillages, oiseaux, particules et son ; une image fixe ne prouve pas leurs animations.

Un contrôle indépendant de cinq positions du terrain avant et après l’habillage retrouve exactement les mêmes hauteurs à l’arrivée, sur le chemin, au refuge et dans la forêt. Ce contrôle ponctuel complète la conservation de la heightmap par le script ; il ne remplace pas la validation du parcours complet.

La [validation de sculpture rejouée après la peinture V3](starter-island/validation.json) passe avec **six couches de terrain et 3 696 groupes LOD**. Les neuf routes conservent leurs hauteurs et leur praticabilité géométrique contrôlée ; le relief n’a pas été resculpté par cette passe.

Les **20 tests Edit Mode** du cycle sont [passés dans Unity](starter-island/environment/tests.json) : durée, raccord et temps négatifs, paramètres invalides, directions célestes, changement de lumière et restauration des réglages de rendu.

La [validation en Play Mode](starter-island/environment/runtime-validation.json) a réussi ses **34 contrôles** avec les composants réels sur plusieurs frames : progression à quatre heures, aperçu figé puis reprise, correction par échantillon serveur, stabilité des racines et mouvement des neuf oiseaux, transitions de lumière et d’audio, particules diurnes puis lucioles nocturnes effectivement présentes, profil de rendu et garde au sol de la caméra. Les modes des courbes de vitesse sont compatibles sur tous les systèmes de particules de la scène. La console commence et termine sans erreur, et aucun événement Unity Error/Exception/Assert n’est émis pendant la validation. Les cinq clips se décodent en échantillons non silencieux et les sept sources lisent leurs boucles. Ce contrôle ne mesure pas la qualité sonore subjective ni les performances d’un build de production.

Ce rapport a été rejoué avec le vent par matériau et l’ancien balancement des arbres entiers désactivé. Il confirme la stabilité des racines ; la déformation effective du feuillage est vérifiée séparément par les rendus successifs de la validation GPU décrite plus haut.

Deux correctifs ciblés sont conservés hors d’Assets : [RepairIslandParticleVelocityModes](../../Unity3D/Arkya/AgentScripts/World/RepairIslandParticleVelocityModes.cs) harmonise les modes X/Y/Z sans recréer l’ambiance ; [RefineRefugeRoofs](../../Unity3D/Arkya/AgentScripts/World/RefineRefugeRoofs.cs) corrige l’échelle du module de porte, aligne le faîtage et ferme les six pignons des trois maisons avec leur matériau peint. Les générateurs produisent désormais directement ces réglages. Ces correctifs ne sauvegardent pas la scène automatiquement.

Le [relevé de console après les dernières captures et l’arrêt du Play Mode](starter-island/environment/console-validation.json) confirme zéro erreur, zéro avertissement et aucune compilation en cours. La scène est enregistrée en Edit Mode ; les 100 assets et dossiers du sous-arbre World possèdent leurs 100 `.meta`, sans GUID dupliqué interne. Les shaders de ciel et d’eau sont supportés et ne rapportent aucune erreur de compilation.

La scène regroupe cette présentation et la configuration de [l’eau dangereuse](STARTER_ISLAND_BOUNDARY_WATER.md). Le contrôleur de personnage, les quêtes, ennemis, collisions ECS du terrain, navigation de production et tests de charge des instances restent à intégrer. Les contrôles de sculpture et les tests d’authoring déjà décrits dans [le guide principal](STARTER_ISLAND.md) restent complémentaires aux contrôles du cycle et à l’examen visuel.

