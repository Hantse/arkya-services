# Vagues et écume de l’île

La scène `Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity` ajoute des vaguelettes au turquoise, des lignes d’écume qui avancent sur les berges et une houle plus forte dans l’eau violette. La règle de mort après cinq secondes dans cette dernière conserve ses paramètres serveur.

## Surface et réglages

**Ocean waves - gentle coast and violet swell** porte [OceanSurface](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/OceanSurface.cs).

- `Calm Wave Height` : amplitude maximale de **0,14 m** dans l’eau turquoise.
- `Boundary Wave Height` : amplitude maximale de **0,85 m** dans l’eau mauve.
- `Wave Speed` : vitesse de l’animation, livrée à 1.
- `Boundary Transition` : montée progressive de la houle sur 22 m après le début de la zone mauve.

Ces amplitudes bornent l’écart au niveau moyen de la mer, à Y=28 m. Trois ondes de directions et de vitesses différentes composent le relief. La profondeur réduit les vagues près du sol ; l’écume forme des fronts qui avancent vers la profondeur décroissante, avec un reflux discret au contact de la côte.

Le [shader d’océan](../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Shaders/StarterIslandWater.shader) déplace réellement les sommets proches et adapte les normales du rendu. Le plan éloigné conserve une animation de surface. Un raccord de 24 m rejoint une surface plate commune entre les deux surfaces.

Le maillage proche mesure **384 m de côté**, avec une grille de 2 m : **37 249 sommets et 73 728 triangles**. Le disque rendu a un rayon de 180 m et suit la caméra par pas de 2 m. Le même matériau est partagé avec le plan d’océan existant de 4 200 m ; les réglages temporaires passent par des MaterialPropertyBlocks. Il n’y a pas de recréation de mesh ou de matériau par frame, ni de collider ajouté.

Les effets de [BoundaryWaterPresentation](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Rendering/BoundaryWaterPresentation.cs) échantillonnent la même houle pour la naissance des particules. Bulles et écume suivent ensuite la surface au moyen de buffers réutilisés ; la vapeur s’élève librement. Le budget existant de 320 particules est conservé.

## Finition de surface — 19 octobre 2026

La surface conserve les volumes de houle existants et ajoute une seconde échelle de petites ondulations dans les normales. Deux gradients de bruit se croisent en dérivant, sans les fronts périodiques qui donnaient des zébrures sur la première capture. Leur détail diminue avec l’empreinte du pixel pour éviter le scintillement au loin. La pluie ajoute des impacts circulaires éphémères seulement à proximité. Ces détails restent cosmétiques : ils ne changent ni les hauteurs de `OceanWaveMath`, ni les colliders, ni les paramètres de danger.

Les reflets utilisent désormais la palette du ciel actif, sa couverture nuageuse et ses éclairs. Le contrôleur transmet ces valeurs aux deux surfaces par leurs blocs de propriétés, sans modifier les matériaux partagés. La réflexion augmente aux angles rasants ; des reflets solaires larges et fins se complètent. Le ciel peint reste une approximation de réflexion : aucun rendu planaire supplémentaire, aucune réflexion de personnage ou de bâtiment n’est ajouté.

Sur les berges, la profondeur cuite révèle progressivement une teinte de sable humide avant le turquoise. Les fronts d’écume sont plus étroits, interrompus et irréguliers ; les crêtes au large restent discrètes. Cette transition de couleur conserve la passe opaque existante : le fond suggéré ne prétend pas montrer la géométrie sous-marine par transparence. La couronne violette conserve sa lecture de danger, avec des bulles plus petites, une écume ajourée et une vapeur moins opaque. Les textures locales de particules disposent de mipmaps, sans hausse du budget de 320 particules.

Les réglages supplémentaires du matériau sont `Sky reflection` (0,6), `Small wave normals` (0,6), `Broken surf and crests` (0,8) et `Wet sand beneath shallows`. Le reflet utilise une normale adoucie et diminue près du contact avec la berge pour conserver la lecture du sable immergé. La météo fournit `_ArkyaWeather` : pluie, orage, vent, humidité cosmétique. La surface utilise la pluie pour ses impacts et le vent pour ses petites ondulations. Les calculs des bulles violettes sont évités dans la mer turquoise. Aucun nouveau champ réseau n’est nécessaire.

## Simulation et authoring

[OceanWaveMath](../../Unity3D/Arkya/Assets/Arkya/World/Environment/Runtime/OceanWaveMath.cs) partage le profil cosmétique du shader pour les effets locaux. Il ne décide pas de l’exposition dangereuse. Le [contrat serveur de la couronne](STARTER_ISLAND_BOUNDARY_WATER.md) reste un rayon de 835 m, un niveau moyen de 28 m et un délai de cinq secondes après contact. Les vagues ne modifient ni le Terrain, ni les collisions, ni la natation ou le déplacement des personnages.

[AddIslandWaves](../../Unity3D/Arkya/AgentScripts/World/AddIslandWaves.cs) ajoute le maillage et les références dans la scène existante. Il doit être lancé en Edit Mode sur StarterIsland, avec l’aperçu du profil de rendu désactivé. Les réglages du contrôleur sont conservés lors d’une nouvelle exécution. À sa désactivation, le contrôleur masque son maillage proche et restaure les propriétés précédentes du plan éloigné.

## Vérification

Les rapports et captures sont conservés dans [starter-island/waves](starter-island/waves). Les tests déterministes du profil, les contrôles Play Mode et les captures GPU sont séparés pour distinguer les valeurs calculées du mouvement réellement rendu.

Les [cinq tests Edit Mode](starter-island/waves/editmode-tests.json) passent : amplitudes bornées, absence de déplacement sur terre, raccords progressifs, vitesse nulle et jonction avec la surface distante. Les [29 contrôles en Play Mode](starter-island/waves/runtime-validation.json) passent également, sans événement Unity Error/Exception/Assert : shader supporté, maillage borné, horloge contrôlée, déplacement de caméra, restauration des renderers et conservation du Terrain et de la règle dangereuse.

À un point turquoise profond, 60 secondes d’échantillons donnent **0,260 m de variation crête à creux**, contre **1,574 m** dans le mauve. Deux rendus isolés à caméra et lumière fixes confirment le déplacement géométrique : **463 993 pixels de silhouette différents** pour le turquoise et **34 918** pour le mauve entre les secondes 10 et 12. Avec les amplitudes à zéro, le même témoin ne trouve **aucun pixel de silhouette différent**. Ce contrôle isole le relief de l’animation de couleur et d’écume.

Les [captures](starter-island/waves/capture.json) conservent douze vues couleur et six témoins géométriques : [contact du rivage à 10 s](starter-island/waves/shore-contact-10.png), [contact à 12 s](starter-island/waves/shore-contact-12.png), [turquoise de jour](starter-island/waves/shore-day-10.png), [côte nocturne](starter-island/waves/shore-night-10.png), [houle mauve de jour](starter-island/waves/boundary-day-10.png) et [mauve nocturne](starter-island/waves/boundary-night-10.png). Une inspection supplémentaire de 95 particules d’écume dans la scène retrouve la hauteur cosmétique attendue avec un écart maximal inférieur à 0,001 m.

La [console finale](starter-island/waves/console-validation.json) ne rapporte aucune erreur et la compilation est terminée. La scène est enregistrée en Edit Mode ; les fichiers globaux Graphics/Quality conservent leurs empreintes de départ et les assets World possèdent tous leur `.meta`. Ces contrôles ne mesurent pas la performance d’un client de production, les collisions ou la natation.
