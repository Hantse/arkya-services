# Météo de l’île de départ

La scène `Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity` porte une météo de présentation qui accompagne le cycle jour/nuit de quatre heures : éclaircies, ciel couvert, pluie et orage. Les surfaces peintes et les matériaux des objets DA sont conservés.

## Fonctionnement

[WeatherTimeline](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Runtime/WeatherTimeline.cs) calcule les états depuis UTC et une graine commune. Par défaut, un segment dure **15 minutes**, avec **75 secondes de transition douce** depuis le segment précédent. Le choix de chaque segment est pondéré : 45 % éclaircies, 25 % couvert, 23 % pluie et 7 % orage. Des segments consécutifs peuvent avoir le même état. Un rechargement retrouve directement la météo correspondant au moment courant.

La graine commune maintient la même météo cosmétique pour les copies de l’île qui utilisent la même horloge. `SetServerTimeSample` permet une correction depuis un futur échantillon serveur de confiance. Aucun transport réseau ou service Realm n’est ajouté par cette passe. La météo ne modifie pas les statistiques, déplacements, récoltes, dégâts ou apparitions : toute conséquence de gameplay devra être décidée par la simulation serveur.

[IslandWeather](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/IslandWeather.cs) relie les composants existants. Le ciel jour/nuit reste seul propriétaire de la lumière, des nuages, des étoiles et du brouillard ; il applique les modulations météorologiques depuis ses valeurs de base, sans les multiplier cumulativement. Le vent ajoute une influence temporaire aux réglages de végétation. Les oiseaux, insectes et pollens deviennent plus discrets sous la pluie.

## Effets et son

[WeatherEffects](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/WeatherEffects.cs) présente une pluie inclinée par le vent près de la caméra, des impacts au sol et une brume légère. Les systèmes possèdent un budget total borné à **1 800 particules**. Les toits de chaume du refuge sont référencés pour les abris ; cette approximation visuelle n’ajoute pas de collisions de gameplay.

L’orage combine des éclairs à branches, un bref relèvement froid du ciel et de l’ambiance, puis un tonnerre retardé selon la distance de la caméra à l’éclair, à 343 m/s. Les éclairs restent décoratifs et ne causent aucun dégât. Les sons de pluie et de tonnerre sont des synthèses originales reproductibles par [GenerateWeatherAudio.py](../../Unity3D/Arkya/AgentScripts/World/GenerateWeatherAudio.py).

Le réglage `Volume` d’`IslandAmbience` pilote aussi la pluie et les tonnerres, y compris un grondement déjà en lecture. Sous les toits référencés, le son de pluie est atténué et les effets autour du spectateur sont coupés. Les impacts respectent le premier collider non trigger rencontré (rues, pierres, toitures), puis utilisent le Terrain en secours, avec le niveau de mer à 28 m comme minimum.

Le gain séparé `Rain Level` de `WeatherEffects` vaut **0,28** dans la scène : la pluie joue à **28 % de son niveau précédent**, soit environ **−11 dB**. Ce réglage agit uniquement sur le son de pluie, en plus du volume général et de l’atténuation sous abri. Le niveau du tonnerre et le réglage général d’ambiance conservent leur fonctionnement.

Les objets, matériaux et textures temporaires sont libérés à la désactivation. Le ciel et le vent retrouvent leur comportement sans météo. Les clips audio importés restent des assets propres à Arkya.

## Passe de finition visuelle — octobre 2026

La pluie couvre maintenant une grille d’environ **32 mètres**, légèrement décalée devant la caméra. Les gouttes varient en épaisseur, vitesse et opacité selon la profondeur, suivent de petites rafales et s’effacent près de la caméra. Leur couleur suit la lumière du jour pour éviter une pluie blanche lumineuse la nuit. Le budget reste **trois systèmes et 1 800 particules maximum** ; cette passe n’ajoute ni lumière dynamique ni collision de gameplay.

Une partie des gouttes proches déclenche une onde exactement à la fin de son trajet. La position tient compte des colliders de chaussée et de rocher, sans être écrasée ensuite par la hauteur du Terrain. Les ondes suivent la normale de la surface et produisent parfois deux petites gouttelettes de rebond. Les sprites sont filtrés avec mipmaps ; les anneaux irréguliers et la brume basse plus discrète conservent le rendu peint.

Les sondages de couverture sont répartis dans le temps (huit cellules toutes les 120 ms ; reconstruction des 25 cellules lors d’un déplacement significatif). Les contacts proches utilisent une requête exacte et une file fixe de 512 impacts différés. Les particules, la file, le mesh d’onde, les textures et les matériaux sont nettoyés à la désactivation. Les effets ne créent aucune ressource sur un build `UNITY_SERVER` ou un appareil sans rendu.

`IslandWeather` publie le vecteur shader `_ArkyaWeather` : **pluie, orage, vent, humidité visuelle**. L’humidité monte progressivement et sèche plus lentement après la pluie ; la désactivation restaure la valeur shader antérieure, normalement zéro, sans écraser un autre propriétaire éventuel. L’océan observe ce canal pour ses petites perturbations sous la pluie. Aucun état supplémentaire n’est répliqué et aucune texture source n’est modifiée.

La régression `RainSurfaceSamplingTests` contient 12 cas Edit Mode pour les rues surélevées, les normales de pente, les triggers, les surfaces immergées, le secours Terrain et les limites des sondes. [ValidateWeatherPolish](../../Unity3D/Arkya/AgentScripts/World/ValidateWeatherPolish.cs) vérifie en Play Mode les particules réellement émises sur un récepteur incliné temporaire, leur hauteur de contact, le budget, l’arrêt des impacts et le canal d’humidité. Les rapports historiques ci-dessous décrivent la passe initiale ; les résultats de cette finition doivent être lus dans le rapport de revue graphique associé.

Le test d’abri et sa capture se placent désormais dans **02 - Maison du boulanger**, à partir de sa transformation réelle. La construction d’Aubefont a désactivé l’ancien refuge : son ancienne position fixe `(517, 566)` se trouve à l’extérieur et ne constitue plus un test d’abri valide. Le contrôle vérifie le collider de la toiture active via le détecteur existant, sans réactiver le blockout.

[ConfigureTownWeatherShelters](../../Unity3D/Arkya/AgentScripts/World/ConfigureTownWeatherShelters.cs) remplace les références de l’ancien refuge par les toitures `plafond-chaume` et les auvents `Scalloped shop awning` actifs d’Aubefont. Cette commande Edit Mode modifie seulement les références de `WeatherEffects`, marque la scène à enregistrer et retourne la liste des couvertures ; elle ne sauvegarde aucun asset et ne crée aucun collider. La protection des auvents reste une approximation de présentation par volume englobant.

La régression météo ajoute un contrôle sous l’auvent de la boulangerie. La caméra est placée sous le centre d’un triangle intérieur du tissu réel ; seul cet auvent est temporairement conservé dans les références pendant le contrôle. L’absence de collider au-dessus est vérifiée, puis l’arrêt des gouttes, des impacts et l’atténuation sonore. Les références sont restaurées ensuite. Cela empêche qu’un volume englobant de toiture voisine valide le contrôle à la place du tissu.

Les nouvelles captures de pluie utilisent le cadrage de la rue du marché de la revue graphique : position relative à `Bakery market court`, à 1,7 m au-dessus du Terrain, champ de 66°. Le ciel, l’abri et l’éclair côtier gardent leurs cadrages spécifiques. Les captures prises depuis l’ancien refuge après la construction de la ville montraient un mur en gros plan ; elles ne constituent pas une preuve visuelle de cette finition et doivent être remplacées par cette nouvelle passe.

## Réglages et revue

Dans la hiérarchie, sélectionner **Island weather - clouds rain and storms**.

- `Automatic` activé : calendrier UTC.
- `Automatic` désactivé : choisir `Preview Kind` pour maintenir un état en Play Mode.
- `Seed` : graine du calendrier ; utiliser la même valeur pour les copies qui doivent partager la météo.
- `Segment Seconds` et `Transition Seconds` : durée des états et des transitions.
- Dans le composant `Weather Effects`, `Rain Level` : gain sonore propre à la pluie, de 0 à 1 ; valeur livrée **0,28**. Le calendrier météo conserve cette valeur lorsqu’il change l’intensité de la pluie.

Les API `SetPreview`, `ClearPreview` et `TriggerLightning` servent à la revue et aux contrôles automatisés. Le cycle jour/nuit conserve son propre aperçu : on peut ainsi examiner la pluie à midi puis la nuit sans accélérer la journée entière.

[AddIslandWeather](../../Unity3D/Arkya/AgentScripts/World/AddIslandWeather.cs) installe les références dans la scène existante et importe les sons sans recréer le terrain ni les objets. Il doit être lancé en Edit Mode, sur `StarterIsland`, avec l’aperçu du profil de rendu désactivé.

## Vérification

Les rapports et captures sont conservés dans [starter-island/weather](starter-island/weather). Le contrôle déterministe du calendrier est séparé des contrôles de scène en Play Mode et de la revue des images.

Les [45 tests Edit Mode](starter-island/weather/editmode-tests.json) passent dans Unity : 25 nouveaux cas pour le calendrier et la modulation météo, en complément des 20 tests existants du cycle et du profil de rendu. Ils couvrent les raccords UTC, les valeurs négatives et invalides, la distribution des états, la conservation de phase et la restauration de l’éclairage après un flash.

La [validation réelle en Play Mode](starter-island/weather/runtime-validation.json) réussit **27 contrôles** : gouttes et impacts réellement présents, abri au refuge, reprise à l’extérieur, vent renforcé, pluie et tonnerre effectivement en lecture, propagation sonore retardée, mute pendant un grondement, transition UTC sur plusieurs frames, phase céleste conservée, désactivation et recréation sans fuite. Aucun événement Unity Error/Exception/Assert n’est observé pendant cette exécution. Ce contrôle ne mesure ni la sortie audio matérielle ni la performance d’un client de production.

La [passe de captures](starter-island/weather/capture.json) conserve onze vues, dont [éclaircies](starter-island/weather/clear.png), [pluie](starter-island/weather/rain.png), [ciel d’orage](starter-island/weather/sky-storm.png), [pluie nocturne](starter-island/weather/night-rain.png) et [éclair sur la côte](starter-island/weather/storm-lightning-coast.png). Pour ce dernier, deux rendus à caméra, lumière et frame fixes, avec puis sans les lignes et sans flash du ciel, diffèrent de **1 321 pixels** : les branches sont effectivement rendues. Le flash est ensuite rétabli pour la capture finale.

Le [contrôle de console après arrêt du Play Mode](starter-island/weather/console-validation.json) confirme zéro erreur et aucune compilation en cours. La scène est enregistrée avec `Automatic` activé. Les réglages globaux Quality/Graphics conservent leurs empreintes de départ ; la météo n’ajoute pas de modification des Project Settings.

## Fichiers concernés

La passe ajoute `WeatherTimeline`, `IslandWeather`, `WeatherEffects`, leurs tests et deux clips audio originaux. Elle étend `DayNightEnvironment` et `PaintedSky` pour les nuages et flashs, `FoliageWind`/`FoliageWindParticles` pour l’influence du vent et du pollen, et `IslandAmbience` pour les sons et la faune. La scène enregistre les références ; les scripts d’installation et de validation restent dans `AgentScripts/World/`.
