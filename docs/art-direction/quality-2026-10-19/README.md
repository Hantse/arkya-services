# Revue et finition graphique — 19 octobre 2026

Cette passe améliore la lisibilité des volumes, les transitions d'animation,
les contacts avec le décor et la cohérence entre ciel, pluie et eau dans le
projet actif `Unity3D/Arkya`, sous Unity **6000.6.6f1 / URP 17.6.0**. Elle
prolonge la [direction peinte approuvée](../resources/README.md), avec l'arbre
V3 comme référence. Les captures permettent de juger le résultat ; aucune note
artistique chiffrée n'est présentée comme acquise.

Les bâtiments, remparts et retouches du loup déjà présents au début de la
session constituent le point de départ. Ils ne sont pas attribués à cette
nouvelle passe. Les modifications décrites ici concernent la présentation ;
les règles de mouvement, de dégâts et de persistance restent sous autorité
serveur.

## Changements et portée

| Domaine | Travail réalisé | Preuve et portée |
| --- | --- | --- |
| Surfaces et matières | Occlusion ambiante utilisée par les personnages et totems ; passes de profondeur, normales et ombres des personnages cohérentes avec leur matériau peint. Contraste et saturation du profil de l'île ajustés avec mesure. | [Réglages du renderer](render-profile.json), captures du studio et comparaisons de l'île. Les couleurs de sommets, textures approuvées et modèles source sont conservés. |
| Personnages | Cadences de marche et course indépendantes pendant les fondus ; vitesse adaptée au déplacement observé et à la taille. Rotation visuelle adoucie pour le personnage local et les personnages distants. Entrée de saut plus courte, arrêt et transitions aquatiques plus souples. | Quatre rigs Humain/Elfe, deux silhouettes chacun ; six états existants vérifiés. [37 tests de locomotion](locomotion-tests.json), [8 de repos](idle-tests.json), [14 de traversée](traversal-tests.json). |
| Familiers | Loup V10, renard V2 et leurs 24 pelages revus ; leur éclairage reçoit l'occlusion ambiante sans assombrir l'émission des marques d'âme. | [Validation des 24 prefabs](totem-validation-command.json), audits de locomotion du [loup](../unity-test/totems/wolf-locomotion-audit.json) et du [renard](../unity-test/totems/fox-locomotion-audit.json), captures des deux espèces. Pas de nouvelle apparition ou de suivi du joueur dans le monde. |
| Île et sous-bois | Ajout local de 460 touffes et groupes : 438 herbes, 18 fleurs et 4 buissons, dérivés des ressources approuvées. Répartition par cellules, chemins dégagés et racines du vent conservées. | [Installation](groundcover-installation.json), [vue rapprochée du sol](after/forest-floor-day.png). La composition reste clairsemée ; ce n'est pas une reconstruction exhaustive de la forêt. |
| Pluie et impacts | Pluie sur une zone d'environ 32 m autour de la caméra, densité proche plus présente, épaisseur/vitesse/opacité variées, petites rafales et couleur liée au jour. Impacts synchronisés au contact, adaptés à la hauteur et à la normale des rues et rochers ; rebonds discrets et brume allégée. | 12 cas `RainSurfaceSamplingTests` inclus dans les 62 tests d'environnement ; [28 contrôles météo et abris](weather-command.json) et [8 contrôles de finition pluie](weather-polish-command.json) réussis en Play Mode. |
| Eau | Reflet stylisé du ciel et des nuages piloté par leur palette réelle, Fresnel, normales fines et perturbations sous la pluie. Lecture du fond peu profond, écume fragmentée au rivage et aux crêtes, transition vers la couronne violette conservée. | [29 contrôles Play Mode des vagues](waves-command.json) réussis : déplacement GPU, budgets, raccords, restauration et invariants de gameplay. Ces contrôles ne remplacent pas la revue esthétique de chaque météo. |
| Éclairage et bourg | Renderer propre à l'île, occlusion ambiante de contact, ajustement du jour/nuit. Vitres et lanternes passent d'une couleur constamment lumineuse à un pigment éclairé le jour et une émission chaude la nuit. | [Rue de jour](after/market-street-day.png), [rue de nuit](after/market-street-night.png). Aucun éclairage dynamique supplémentaire n'est requis pour ces vitres. |

La pluie conserve **trois systèmes et 1 800 particules maximum**. Ses sondages
de couverture sont répartis dans le temps et les impacts proches utilisent une
file bornée. Le canal `_ArkyaWeather` transmet pluie, orage, vent et humidité
visuelle à l'eau ; il retrouve sa valeur précédente à la désactivation. Le
[guide météo](../../world/STARTER_ISLAND_WEATHER.md) détaille ces responsabilités.
Les références des six toits de l'ancien refuge désactivé sont remplacées par
**20 pans de toiture et 7 auvents actifs du bourg**. Le
[rapport de configuration](shelters-command.json) décrit cette opération sans
sauvegarde automatique ; la scène a ensuite été enregistrée. Le contrôle
runtime vérifie la maison du boulanger et son auvent sans collider de gameplay :
pluie et impacts s'arrêtent sous l'abri, le son s'atténue, puis la pluie reprend
à l'extérieur. Le test d'auvent isole sa référence pour éviter qu'un toit
voisin valide le résultat à sa place.

## Comparer les images

Les paires suivantes utilisent les mêmes cadrages et phases de jour/nuit.
Les [métadonnées avant](before/captures.json), [métadonnées après](after/captures.json)
et le [rapport de capture](capture-command.json) donnent positions, cibles et
champs de vision. Ce sont des rendus en Edit Mode : ils montrent les matières,
la composition et l'éclairage, mais ne prouvent pas à eux seuls la pluie en
mouvement ou le fonctionnement du gameplay.

| Vue | Avant | Après |
| --- | --- | --- |
| Forêt, jour | [Image](before/forest-day.png) | [Image](after/forest-day.png) |
| Forêt, nuit | [Image](before/forest-night.png) | [Image](after/forest-night.png) |
| Bourg, vue générale de jour | [Image](before/town-overview-day.png) | [Image](after/town-overview-day.png) |
| Bourg, vue générale de nuit | [Image](before/town-overview-night.png) | [Image](after/town-overview-night.png) |
| Rue du marché, jour | [Image](before/market-street-day.png) | [Image](after/market-street-day.png) |
| Rue du marché, nuit | [Image](before/market-street-night.png) | [Image](after/market-street-night.png) |
| Porte d'arrivée | [Image](before/arrival-gate-day.png) | [Image](after/arrival-gate-day.png) |
| Cour du puits | [Image](before/well-court-day.png) | [Image](after/well-court-day.png) |
| Cour abritée | [Image](before/sheltered-court-day.png) | [Image](after/sheltered-court-day.png) |
| Escalier du rempart | [Image](before/rampart-stair-day.png) | [Image](after/rampart-stair-day.png) |
| Chemin de ronde | [Image](before/rampart-walk-day.png) | [Image](after/rampart-walk-day.png) |

La [vue du couvert végétal](after/forest-floor-day.png) est un cadrage
supplémentaire rapproché, sans image avant équivalente. Elle montre les touffes
et quelques fleurs au pied des arbres ; le cadrage général de forêt reste
utile pour évaluer la continuité du chemin et les grandes masses.

Les captures météo et eau ont été rejouées en Play Mode après les derniers
correctifs. Elles complètent les comparaisons en Edit Mode ci-dessus ; les
[états météo capturés](weather-capture-command.json) et les
[paramètres des vagues](waves-capture-command.json) sont conservés.

| Sujet | Jour / premier état | Nuit / autre état |
| --- | --- | --- |
| Pluie | [Pluie de jour](../../world/starter-island/weather/rain.png) | [Pluie de nuit](../../world/starter-island/weather/night-rain.png) |
| Abri et orage | [Pluie sous l'abri](../../world/starter-island/weather/rain-shelter.png) | [Orage côtier](../../world/starter-island/weather/storm-lightning-coast.png) |
| Eau près de la berge | [Berge de jour](../../world/starter-island/waves/shore-day-10.png) | [Berge de nuit](../../world/starter-island/waves/shore-night-10.png) |
| Contact avec le rivage | [Vague à 10 s](../../world/starter-island/waves/shore-contact-10.png) | [Vague à 12 s](../../world/starter-island/waves/shore-contact-12.png) |
| Couronne violette | [Eau de jour](../../world/starter-island/waves/boundary-day-10.png) | [Eau de nuit](../../world/starter-island/waves/boundary-night-10.png) |

| Sujet | Corps / repos | Portrait / marche |
| --- | --- | --- |
| Humain A | [Corps](characters/human-a-body.png) | [Portrait](characters/human-a-portrait.png) |
| Humain B | [Corps](characters/human-b-body.png) | [Portrait](characters/human-b-portrait.png) |
| Elfe A | [Corps](characters/elf-a-body.png) | [Portrait](characters/elf-a-portrait.png) |
| Elfe B | [Corps](characters/elf-b-body.png) | [Portrait](characters/elf-b-portrait.png) |
| Loup, pelage lunaire | [Repos](characters/wolf-lunar-idle.png) | [Phases de marche](characters/wolf-walk-phases.png) |
| Renard, pelage braise | [Repos](characters/fox-ember-idle.png) | [Phases de marche](characters/fox-walk-phases.png) |

Les planches de marche montrent les phases 0 %, 25 %, 50 % et 75 %, en profil
puis en trois-quarts. Elles échantillonnent les vrais clips importés ; elles ne
constituent pas une nouvelle animation ni une validation des appuis sur pente.
Le [rapport du studio](characters/capture-report.json) consigne les sujets,
clips, profils et paramètres réellement utilisés.

Les premières captures du studio présentaient des liserés dorés hachés et des
contours irréguliers. La cible de `PreviewRenderUtility` étant monoéchantillonnée,
les détails très fins manquaient de couverture par pixel. La reprise du
19 octobre à **21:19:59 UTC** rend chaque axe à une résolution double puis
résout l'image sur le GPU à la taille finale. Les modèles et shaders restent
ceux du jeu ; aucune retouche d'image n'est appliquée. Comparer
[l'échantillonnage initial](characters/sampling-baseline-human-a-body.png) au
[rendu repris](characters/human-a-body.png). Ce suréchantillonnage est propre
à la capture et ne démontre pas la qualité d'anticrénelage ni le coût d'une
image du client à sa résolution native.

## Animations et audit des ressources

Les clips natifs existants et les squelettes sont conservés. `WalkRate` et
`RunRate` suivent séparément la vitesse observée : durant un fondu, les deux
clips représentent le même déplacement au lieu de partager un multiplicateur
inadapté à leurs longueurs de foulée. `LocomotionRate` reste disponible pour
les anciens contrôleurs. Les tests vérifient notamment les contacts des
semelles, les boucles, les coudes et genoux, les tailles autorisées, les
interruptions de transition et l'absence de root motion.

Le contrôle des ressources retrouve **11 prefabs d'environnement optimisés**,
chacun avec trois LOD et un fondu ; leurs références GUID se résolvent dans
les assets Arkya. Le prefab `Mana` conserve un seul Animator. Il s'agit de onze
échantillons de la [bibliothèque de 117 modèles approuvés](../resources/README.md),
pas de l'intégration de toutes ses variantes. La répétition du même arbre et
du même buisson reste donc visible. Les [budgets et contrôles LOD antérieurs](../unity-test/lod/README.md)
demeurent la référence pour leurs géométries.

Le nouveau couvert utilise des dérivés du dernier LOD : les brins retirés le
sont par composants complets pour conserver les feuilles restantes. Le
rapport d'installation compte **56 cellules de 24 m, 80 renderers, quatre
matériaux et 413 452 triangles au total**, avec une borne géométrique de
127 684 triangles dans le voisinage de revue. Les ajouts reçoivent les ombres
mais n'en projettent pas ; ils n'ajoutent ni colliders ni gestionnaire exécuté
chaque frame. Ces comptes ne sont pas une mesure de temps GPU ou de FPS.

Les herbes et fleurs utilisent leurs propres matériaux de vent, alimentés
par les paramètres globaux déjà publiés par l'île. Le masque de hauteur en
`UV1.x` ancre chaque plante, même après regroupement en cellules, et les
bounds sont élargies pour la déformation. Ce couvert n'a pas besoin d'être
inscrit dans le registre des arbres qui émettent des particules de feuilles.
Les quatre personnages et les deux espèces de totems n'ont toujours pas de
LOD de production.

## Vérification acquise et contrôle final

Les rapports de cette passe confirment **121 tests Edit Mode réussis**, sans
échec ni test ignoré :

| Groupe | Résultat | Rapport |
| --- | ---: | --- |
| Environnement, horloge, météo, vagues et sondage des surfaces de pluie | 62 / 62 | [Résultats](environment-tests.json) |
| Locomotion et présentation des personnages | 37 / 37 | [Résultats](locomotion-tests.json) |
| Repos des personnages | 8 / 8 | [Résultats](idle-tests.json) |
| Saut, nage et transitions de traversée | 14 / 14 | [Résultats](traversal-tests.json) |

Les validations ciblées ont été rejouées sur les scripts et shaders finaux :

| Contrôle Play Mode | Résultat | Rapport |
| --- | ---: | --- |
| Météo, transitions, pluie, audio et abris actifs | 28 / 28 | [Résultats](weather-command.json) |
| Contacts des impacts, budget et état shader météo | 8 / 8 | [Résultats](weather-polish-command.json) |
| Vent réellement rendu sur arbre et buisson, aux trois LOD | 17 / 17 | [Résultats](foliage-command.json) |
| Vagues et raccords avec le shader final | 29 / 29 | [Résultats](waves-command.json) |
| Horloge, éclairage, ciel, oiseaux, audio et restauration de l'environnement | 35 / 35 | [Résultats](environment-runtime-command.json) |
| Couvert végétal : structure, matériaux, UV1, vent GPU et restauration | 11 / 11 | [Résultats](groundcover/gpu-validation.json) |

Ces six suites totalisent **128 contrôles Play Mode réussis**. Le contrôle du
couvert a permis de corriger l'activation du masque UV1 et les amplitudes des
quatre matériaux : les deux phases rendent des pixels différents, tandis que
le témoin sans vent reste identique. Les [poses A](groundcover/wind-pose-a.png)
et [B](groundcover/wind-pose-b.png) utilisent une cellule réellement installée.

Les **29 contrôles de vagues en Play Mode**, rejoués à **21:23:23 UTC**,
passent avec zéro erreur Unity observée. Ils vérifient des rendus GPU
différents lorsque les vagues bougent, des silhouettes identiques lorsque
leur hauteur vaut zéro, la restauration des renderers, le Terrain et la règle
d'exposition dangereuse conservés. Le [rapport des totems](totem-validation-command.json)
valide les deux modèles et 24 prefabs : poids normalisés, matériaux et rigs
présents, shader sans erreur, déformation des trois clips et transitions du
contrôleur sur un représentant de chaque espèce.

Les audits détaillés du [loup](../unity-test/totems/wolf-locomotion-audit.json)
et du [renard](../unity-test/totems/fox-locomotion-audit.json) ont également
été rejoués : **Idle, Walk et Run passent pour les deux espèces**, avec
échantillonnage à 120 Hz, contrôle des boucles, du glissement pendant l'appui
et de la déformation du mesh. Cela valide les clips existants, sans ajouter
de suivi de terrain ni de comportement de familier en jeu.

Les premiers échecs météo provenaient d'un test visant le refuge désactivé
et d'un contrôle chargé avant recompilation. Les rapports actuels ci-dessus
correspondent aux nouvelles exécutions réussies. Le
[contrôle général de l'environnement](environment-runtime-command.json)
passe également ses **35 contrôles**. Sa fixture impose temporairement un ciel
clair puis restaure la météo ; les vérifications audio portent sur le PCM
décodé par Unity plutôt que sur la fréquence du fichier source. Les résultats
historiques des guides ne remplacent pas une nouvelle exécution.

La scène est sauvegardée en Edit Mode. Sa caméra de revue locale s'ouvre sur
la rue du marché ; elle ne démarre plus face au rempart. Le profil global est
restauré, et les fichiers `QualitySettings.asset` / `GraphicsSettings.asset`
restent identiques à la sauvegarde initiale. Les nouveaux assets conservent
leurs `.meta` ; les 1 578 `.meta` Arkya ne présentent aucun GUID dupliqué.
Les [shaders contrôlés](shaders-command.json) compilent, et
l'[état final de l'éditeur](final-editor-state.json) confirme une scène propre.

## Principaux fichiers modifiés

Les chemins ci-dessous sont relatifs à `Unity3D/Arkya/`. Cette liste cible
la passe actuelle et exclut les modèles de loup, textures et constructions
du bourg déjà modifiés avant son lancement.

| Domaine | Sources et assets principaux |
| --- | --- |
| Personnages | `Assets/Arkya/Characters/Runtime/CharacterPreviewPresenter.cs`, `Runtime/Playtest/CharacterLocalPlaytest.cs`, `Editor/CharacterLocomotionAnimationBuilder.cs`, `Shaders/PaintedCharacter.shader`, les quatre `Animations/{human,elf}-{a,b}.controller` et `Tests/Editor/CharacterLocomotionPresentationTests.cs`. Les chemins abrégés de cette ligne restent sous `Assets/Arkya/Characters/`. |
| Familiers | `Assets/Arkya/Totems/Shaders/PaintedTotem.shader` ; contrôle de compatibilité dans `AgentScripts/Totems/ValidateTotems.cs`. |
| Pluie et abris | `Assets/Arkya/World/Environment/Rendering/WeatherEffects.cs`, `IslandWeather.cs` dans ce même dossier ; `Runtime/RainSurfaceSampling.cs` et `Tests/Editor/RainSurfaceSamplingTests.cs` sous `Environment/` ; `AgentScripts/World/ConfigureTownWeatherShelters.cs`, `ValidateIslandWeather.cs` et `ValidateWeatherPolish.cs`. |
| Eau et ciel | `Assets/Arkya/World/StarterIsland/Shaders/StarterIslandWater.shader` ; `Environment/Rendering/OceanSurface.cs`, `BoundaryWaterPresentation.cs`, `Environment/Runtime/DayNightEnvironment.cs` et `Environment/Shaders/PaintedSky.shader`, sous `Assets/Arkya/World/`. |
| Couvert végétal et vent | `AgentScripts/World/AddIslandGroundcoverPolish.cs`, `Assets/Arkya/World/Environment/Shaders/PaintedFoliageWind.shader` et `.hlsl` ; meshes et matériaux dérivés dans `Assets/Arkya/World/StarterIsland/GroundcoverPolish/`, avec leurs `.meta`. |
| Lumière et matières du bourg | `Assets/Arkya/World/Environment/Shaders/WarmGlass.shader` ; `IslandRenderPipeline.asset`, `IslandRenderer.asset`, `PaintedGrading.asset` sous `Assets/Arkya/World/StarterIsland/Materials/Environment/` ; `Town/Materials/Lantern warm glass.mat` et `Warm interior window.mat` sous `StarterIsland/` ; `AgentScripts/World/UpgradeVisualQuality.cs`. |
| Scène et revue | [StarterIsland.unity](../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity), avec le couvert végétal et les 27 références d'abri ; `AgentScripts/World/ReviewVisualQuality.cs` et `AgentScripts/ArtDirection/ReviewCharacterQuality.cs` pour les captures reproductibles. |

## Limites de cette livraison

La finition conserve les silhouettes, tenues, poids et clips source. Elle
n'ajoute pas de rig facial, de clignement des yeux, d'animations de combat ou
de nouveaux équipements. Les corrections de cadence et de rotation sont
visuelles ; elles ne constituent pas un système complet de placement des
pieds sur le relief.

Le loup et le renard restent des assets de présentation à examiner. Le choix
persistant d'un familier, son apparition, son suivi, ses capacités et sa
réplication dans l'île restent à intégrer. Les oiseaux existants sont des
éléments décoratifs, pas une simulation de faune.

Le reflet de l'eau est une approximation stylisée du ciel ; cette passe ne
livre pas de réflexion planaire des bâtiments ni de simulation de fluide.
L'occlusion ambiante travaille en espace écran. La composition végétale,
les transitions lointaines et la variété des exemplaires peuvent encore être
enrichies. Aucun build de production, benchmark WebGL/mobile, profil complet
de frame ou essai réseau multi-joueurs n'est annoncé par ce bilan.

Les outils de revue sont
[ReviewVisualQuality](../../../Unity3D/Arkya/AgentScripts/World/ReviewVisualQuality.cs),
[ReviewCharacterQuality](../../../Unity3D/Arkya/AgentScripts/ArtDirection/ReviewCharacterQuality.cs),
[ValidateWeatherPolish](../../../Unity3D/Arkya/AgentScripts/World/ValidateWeatherPolish.cs)
et les contrôles de vagues et totems déjà présents. Ils conservent la
distinction entre construction d'assets, capture visuelle et validation
runtime ; une image flatteuse ne remplace pas leurs preuves respectives.
