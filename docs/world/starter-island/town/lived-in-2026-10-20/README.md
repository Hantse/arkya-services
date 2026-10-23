# Aubefont — cours habitées et reprise des références visuelles

Cette passe du **20 octobre 2026** occupe les abords des maisons, retravaille leurs toitures et différencie les matières et l’éclairage. Les [treize captures fournies par l’utilisateur](../../../../art-direction/references/town-2026-10-20/README.md) précisent désormais la cible : architectures sculptées, végétation étagée et reliée, matières peintes calmes, soleil chaud et ombres bleu-vert. Elles complètent la demande initiale d’une lisibilité proche d’Overwatch. La passe constitue une étape vers cette cible ; elle ne valide pas une équivalence de qualité.

La scène reste `Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity`, sous Unity **6000.6.6f1 / URP 17.6.0**. **La scène finale est sauvegardée et l’éditeur reste en Edit Mode.** Les contrôles finaux réussissent ; les rapports d’authoring et **douze captures actualisées et inspectées** du dossier `after/` documentent les retouches décrites ci-dessous. Ces vérifications restent distinctes d’une mesure de performance ou d’une équivalence artistique avec les références.

## Cours et accessoires appliqués

Le [rapport de composition](dressing.json) compte **24 groupes** : quatre livraisons avec charrette, cinq réserves de caisses, deux réserves de foin et treize jardins. Les objets sont combinés par secteur et par matériau sous la racine **`Town life - inhabited workyards and garden courts`**.

| Ajout | Quantité |
| --- | ---: |
| Charrettes | 4 |
| Caisses ouvertes ou fermées | 26 |
| Balles de foin | 12 |
| Buissons | 26 |
| Groupes d’herbes | 50 |
| Groupes de fleurs | 37 |
| Triangles du nouvel habillage | 566 932 |
| Renderers du nouvel habillage | 32 |
| Nouveaux colliders / lumières / MonoBehaviours | 0 / 0 / 0 |

Ces nombres décrivent le contenu, **pas des draw calls ou une mesure de FPS**. Les quatre secteurs possèdent des groupes LOD. Les végétaux proviennent des prefabs Arkya approuvés, sans modification de leurs sources. Les charrettes possèdent des roues à rayons, des planches séparées et des rebords travaillés ; les caisses et le foin sont des géométries propres à cette passe.

Le [contrôle préalable des emplacements](placement-review.json) réserve les rues et l’approche actuelle de la mairie. Il écarte quatre propositions faute d’emprise libre. La marge minimale annoncée entre une emprise acceptée et un corridor réservé est **0,357 m** ; ce contrôle de placement ne remplace pas un parcours de personnage. Aucun objet de cette passe ne possède de collision de gameplay.

La [retouche des pigments](ground-pigments.json) atténue l’orange des charrettes et raccorde les onze emprises de terre au vert de la prairie, sans modifier leurs positions, normales, UV ou triangles. La [correction des marges](plants-margins.json) recentre quatorze éléments de végétation appartenant à sept réserves de caisses ou de foin dans leur emprise prévue.

## Végétation et silhouettes

Le [rapport initial des jardins étendus](lush-gardens.json) décrit une seconde couche, **en supplément** des petits jardins ci-dessus : **10 arbres, 24 buissons, 1 051 touffes d’herbe et 30 groupes de fleurs**. Les arbres, buissons et fleurs utilisent les formes approuvées d’Arkya et des dérivés propres à la scène. Des cellules de 18 m regroupent les meshes et leurs LOD ; les masques de croissance conservent l’ancrage des plantes au sol pendant le vent.

La [prairie affinée](reference-meadow-fine.json) remplace les meshes de ces 1 051 touffes clairsemées par **132 440 brins courbes**, à trois triangles par brin et avec un dégradé peint vert/bleuté de la base à la pointe. Elle utilise **397 320 triangles au niveau proche et 132 441 au niveau éloigné**, contre 252 240 triangles proches pour l’herbe initiale. Cette dernière retouche remplace la première prairie de 44 921 brins ; elle repart des centres de placement d’origine. Les brins mesurent de 3,2 à 5,7 cm de large et de 31 à 64 cm de haut. Un shader propre à la prairie conserve les normales d’enveloppe orientées vers le ciel sur les deux faces, supprimant les demi-brins noirs sans supprimer l’éclairage ni les ombres. Les renderers existants sont réutilisés. Les emplacements réservent les rues, la place, les espaces de livraison et les obstacles ; le contrôle par brin tient compte de sa courbure et rapporte une marge minimale de **0,075 m** aux corridors.

La [retouche des canopées](reference-canopies.json) agrandit **neuf des dix arbres ajoutés**, dont les hauteurs résultantes sont comprises entre **9,61 et 12,56 m**. Le haut du tronc et les branches sont étirés ensemble au-dessus de 2,2 m ; les feuilles entières sont agrandies de **2,2** autour de leur centre. La [reprise de densité](reference-canopies-density.json) repart des meshes initiaux, sans cumuler l’agrandissement précédent. Le dixième arbre garde sa forme précédente à cause d’un contrefort voisin. Les racines, les UV, les couleurs, la topologie et les ressources approuvées sources restent conservés, sans ajout de triangles ni de renderers.

| Jardins étendus après retouches | Compte d’authoring |
| --- | ---: |
| Triangles au niveau proche, végétation complète | 995 024 |
| Triangles au niveau éloigné, végétation complète | 667 265 |
| Triangles au niveau distant, végétation complète | 534 824 |
| Triangles de prairie au niveau proche | 397 320 |
| Triangles de prairie au niveau éloigné | 132 441 |
| Renderers créés, tous niveaux compris | 129 |
| Marge minimale de la nouvelle prairie aux corridors | 0,075 m |
| Nouveaux colliders / lumières / comportements | 0 / 0 / 0 |

Le total proche de 995 024 correspond aux 849 944 triangles initiaux, moins les 252 240 anciens triangles d’herbe, plus les 397 320 de prairie. Les lignes de prairie sont comprises dans ce total et ne doivent pas lui être ajoutées. Ces comptes ne signifient pas que tous les renderers sont actifs simultanément. Ils ne mesurent ni les draw calls, ni la charge GPU, ni les FPS.

Une [couronne végétale complémentaire](reference-canopy-frame.json) ajoute ensuite **14 arbres, 499 240 triangles et 42 renderers** sous un enfant séparé, **`Reference gardens - layered canopy frame`**, de la racine des cours habitées. Ces arbres partagent trois meshes dérivés de la végétation approuvée et les matériaux possédés des jardins. Le niveau source le moins détaillé est conservé jusqu’à la disparition à distance ; aucun gain de géométrie progressif n’est revendiqué pour ce groupe. Ce groupe s’ajoute aux nombres du tableau précédent, sans collider ni lumière supplémentaire.

La [retouche des bases et contacts](reference-canopy-base-refinement.json) réduit la largeur des troncs à **65 %** à leur base, avec un raccord progressif à leur largeur complète entre **3 et 6 m**. Les arbres 09 et 10 sont déplacés de **0,375 et 0,50 m** ; les arbres 01 et 07 passent à **90 % et 94 %** de leur largeur, pour conserver **0,15 m** de dégagement aux toitures. La marge minimale réelle des sommets aux voies est désormais de **0,11993 m**, sans changement de triangles ou de renderers. Les [contacts aux obstacles et voies](reference-canopy-frame-contacts.json) et les [croisements des surfaces de toiture](reference-canopy-roof-contacts.json) sont contrôlés sur ces positions finales.

La [reprise des toitures](reference-roofs.json) dérive les ensembles toit-pignon des **neuf maisons actives**. Les pentes se creusent, les faîtages montent et des pièces de bois chanfreinées épaississent les silhouettes. **Cinq couvertures** reçoivent des bardeaux d’ardoise larges ; les quatre autres gardent leur chaume. Les panneaux de pignon ont ensuite été reconstruits à partir de la surface exacte du toit pour fermer les jours révélés par les captures intermédiaires. Le [rapport des raccords](reference-roof-contacts.json) compte désormais **296 638 triangles de géométrie de toiture**, avec un déplacement nul des contacts de base sur les neuf maisons ; **14 renderers** ont été ajoutés. Le nombre de triangles décrit les meshes résultants, pas uniquement un surcoût par rapport à l’état initial. Les ancrages de bâtiments, leurs ouvertures et leurs collisions restent conservés. Les captures actualisées incluent cette fermeture.

## Palette et lanternes

Le [rapport de palette](palette.json) décrit **31 matériaux dérivés affectés à 670 renderers existants**, sans nouveau slot de matériau. Les textures, leurs transformations UV et les meshes restent conservés.

- Le mélange uniforme qui délayait les couleurs et les variations sinusoïdales sont retirés. Un contraste de luminance borné agit sur la texture avant sa teinte, avec un réglage propre à chaque matière.
- Les enduits restent ivoire chaud, ocre ou sauge ; le bois devient plus brun, le chaume conserve son miel et l’ardoise de mairie redevient bleue. Les valeurs de lissage différencient bois, pierre, enduit, chaume et ardoise.
- Le shader conserve les passes URP de lumière, ombre, profondeur et normales, le brouillard existant, la réponse à l’humidité et l’instancing. Il accepte également les couleurs de sommets des nouveaux accessoires sur une texture blanche.
- Les corps jade et ambre des lanternes flottantes utilisent **2,5 % de leur émission source le jour** et **100 % la nuit**, à partir des paramètres jour/nuit déjà présents. Les runes et les fleurs qui partageaient les matériaux d’origine conservent leurs affectations.

La diminution de l’émission diurne rend de nouveau visibles la couleur et le modelé des lanternes. Elle n’ajoute aucune lumière et ne modifie ni le bloom, ni l’exposition, ni les profils de correction colorimétrique.

La [reprise des fenêtres et lanternes traditionnelles](reference-windows.json) applique **trois matériaux dérivés à 53 renderers existants** : dix ensembles de vitres de bâtiment, seize lanternes traditionnelles et vingt-sept ampoules de guirlande. La [retouche des cœurs lumineux](reference-window-cores.json) donne à chaque vitre un centre crème-doré et des bords cuivrés, avec une division 2 × 2 pour les fenêtres. Leur opacité est désormais de **0,72 le jour et 0,88 la nuit**, avec une intensité centrale pilotée par les facteurs **0,82 / 1,05**. Les facteurs sont **0,64 / 1,05** pour les lanternes et **0,72 / 1,10** pour les petites ampoules ; celles-ci gardent leur profondeur opaque. Ces réglages utilisent les globals horaires existants. Les rapports ne signalent aucune erreur de shader ; aucune nouvelle lumière, ressource ou affectation n’est ajoutée par cette dernière retouche.

La [retouche du chaume](palette-refinement.json) applique un biais de mipmap **2**, un contraste de texture **0,73** et une saturation **0,90** au seul matériau possédé. La teinte miel, la texture source et ses UV restent conservés ; aucun matériau supplémentaire n’est créé.

Le [rapport de réponse à la lumière](light-response.json) porte ensuite sur les **35 matériaux possédés** de palette et d’accessoires. Il refroidit la lumière ambiante diurne en préservant d’abord sa luminance, puis applique un facteur d’énergie explicite de **0,86 à 0,96** selon la matière. Cette teinte disparaît à la nuit. Le relief fin de la pierre, du bois et des couvertures est adouci ; le soleil, la lune, les lanternes, les reflets PBR et la réponse à l’humidité continuent d’éclairer réellement les surfaces.

Le shader de végétation dérivé reprend ce même principe, tout en conservant le vent, les masques de croissance, l’alpha, l’éclairage des deux faces, les ombres, la profondeur et les LOD. Il n’ajoute pas d’émission pour éclaircir les feuilles. Un contrôle a relevé que les **17 matériaux possédés de jardins utilisés** avaient perdu leur activation du masque de croissance après la restauration de scène. La [restauration ciblée](reference-garden-material-restoration.json) réapplique leurs paramètres d’origine de vent et de lumière, sans modifier leurs textures, teintes, géométries ou sources ; le matériau séparé de prairie fine reste conservé. Cette correction est appliquée et le [contrôle final des jardins](reference-gardens-validation.json) réussit.

La [reprise du ciel](reference-atmosphere.json) affecte un matériau de ciel dérivé à l’environnement de l’île. La [retouche actuelle des nuages](reference-clouds-refinement.json) remplace les calottes répétées par un champ de densité continu, déformé et composé de quatre échelles. Les contours courts et adoucis et le relief orienté vers le soleil différencient les sommets blancs des côtés bleu-gris, sans coutures de cellules ni groupes ellipsoïdaux répétés. Cet effet s’atténue au crépuscule et avec la météo ; la nuit et le ciel entièrement couvert conservent les chemins de calcul précédents. **Le coût de cette représentation n’a pas été mesuré sur les appareils cibles** ; aucun coût identique au ciel initial n’est revendiqué.

**Ce ciel concerne toute la scène**, même si ses ressources sont rangées avec cette passe de ville. Le cycle, le soleil et la lune, la météo, les éclairs, les étoiles et le brouillard restent pilotés par leur propriétaire existant.

## Périmètre conservé

Cette passe ne déplace pas la mairie, les maisons ou leurs ouvertures. Les toitures utilisent leurs nouveaux meshes dérivés. **La géométrie du parvis, de son approche, des pavés et des voies existantes reste identique. Le Terrain est intégralement conservé : hauteurs, peinture et couches.** Les nouveaux raccords de terre sont des meshes de présentation.

Les transformations et collisions existantes restent conservées. Les profils de rendu et de post-traitement, le soleil, la météo et le cycle horaire ne sont pas retouchés ; l’affectation du matériau de ciel est la modification explicite de l’environnement. Aucun PNJ, commerce, inventaire, interaction ou état réseau n’est ajouté. La maison du boulanger demeure désactivée.

## Comparaison visuelle

Les vues avant et après correspondantes emploient les mêmes caméras et phases horaires. Les [paramètres des douze captures finales](after/captures.json) indiquent la restauration de la caméra, de l’heure, de la météo et des autres états de présentation après la capture. Les captures ne sauvegardent pas leurs états temporaires ; la scène d’authoring finale a été sauvegardée séparément. **Le dossier `after/` inclut la prairie affinée, les canopées épaissies et complémentaires avec leurs raccords corrigés, les fenêtres éclairées, le ciel continu et les raccords de pignons.**

| Vue | Avant | Après actuel |
| --- | --- | --- |
| Ensemble | [Avant](before/village-overview-day.png) | [Après](after/village-overview-day.png) |
| Mairie et cour | [Avant](before/civic-corner-day.png) | [Après](after/civic-corner-day.png) |
| Rue du marché | [Avant](before/market-street-day.png) | [Après](after/market-street-day.png) |
| Puits et place | [Avant](before/fountain-day.png) | [Après](after/fountain-day.png) |
| Mur extérieur | [Avant](before/wall-exterior-day.png) | [Après](after/wall-exterior-day.png) |
| Crépuscule | [Avant](before/town-twilight.png) | [Après](after/town-twilight.png) |
| Livraisons de l’auberge | [Avant](before/inn-market-court-day.png) | [Après](after/inn-market-court-day.png) |
| Cour de travail | [Avant](before/coopers-workyard-day.png) | [Après](after/coopers-workyard-day.png) |

Deux gros plans montrent [la charrette de livraison](after/delivery-cart-detail.png) et [la réserve de foin du verger](after/orchard-hay-detail.png). Deux nouvelles vues de composition montrent [l’allée du jardin](after/garden-lane-day.png) et [les canopées du village](after/village-canopy-day.png).

Les captures de l’auberge et de l’allée confirment une prairie plus continue, une gamme verte plus cohérente et la disparition des demi-brins noirs, avec des accès encore lisibles. Les fenêtres chaudes et les arbres de premier plan renforcent les plans de profondeur. Les feuilles restent plus pointues et les bords de massifs plus découpés que dans les références ; la composition détaillée des façades et la silhouette droite de la mairie constituent également des écarts. Ces observations ne valent pas une équivalence de finition ni un contrôle de toutes les intersections géométriques. Une augmentation globale de saturation ne résoudrait pas ces écarts.

La [comparaison artistique](DA-COMPARISON.md) et les [treize références reçues](../../../../art-direction/references/town-2026-10-20/README.md) décrivent les critères utilisés. Les anciennes planches conceptuelles et le kit architectural ne deviennent pas approuvés du fait de cette passe. Les images de référence ne constituent pas des assets importés ni une autorisation d’utiliser les packs qu’elles montrent.

## Sources et retouches

Tous les ajouts sont isolés sous **`Assets/Arkya/World/StarterIsland/Town/LivedIn20261020/`** :

| Ressources possédées | Source d’authoring |
| --- | --- |
| `Palette/` : shader et 31 matériaux dérivés | [EnrichTownPalette.cs](../../../../../Unity3D/Arkya/AgentScripts/World/EnrichTownPalette.cs), [PaintedTownColor.shader](../../../../../Unity3D/Arkya/AgentScripts/World/PaintedTownColor.shader) |
| `Props/` : charrette, caisses, foin | [TownLifePropGeometry.cs](../../../../../Unity3D/Arkya/AgentScripts/World/TownLifePropGeometry.cs) |
| `Gardens/`, `Materials/` et racine de composition | [BuildLivedInTown.cs](../../../../../Unity3D/Arkya/AgentScripts/World/BuildLivedInTown.cs) |
| Retouches des emprises et pigments possédés | [AdjustLivedInTownPlants.cs](../../../../../Unity3D/Arkya/AgentScripts/World/AdjustLivedInTownPlants.cs), [RefineLivedInSurfaces.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineLivedInSurfaces.cs) |
| `LightResponse/` : shaders de surfaces et de feuillage | [RefineTownLightResponse.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineTownLightResponse.cs), [PaintedTownLight.shader](../../../../../Unity3D/Arkya/AgentScripts/World/PaintedTownLight.shader), [PaintedFoliageLight.shader](../../../../../Unity3D/Arkya/AgentScripts/World/PaintedFoliageLight.shader) |
| `LushGardens/` : cellules végétales, matériaux et LOD | [BuildLushTownGardens.cs](../../../../../Unity3D/Arkya/AgentScripts/World/BuildLushTownGardens.cs) |
| `LushGardens/Meadow/`, `MeadowFine/`, `Canopies/` et `CanopyDensity/` : versions conservées et dérivés actuels | [RefineReferenceMeadow.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineReferenceMeadow.cs), [RefineReferenceCanopies.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineReferenceCanopies.cs) |
| `CanopyFrame/` : trois meshes partagés par quatorze arbres supplémentaires | [FrameReferenceTownGardens.cs](../../../../../Unity3D/Arkya/AgentScripts/World/FrameReferenceTownGardens.cs) |
| Paramètres de vent et de lumière des matériaux de jardins existants | [RestoreGardenWindSettings.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RestoreGardenWindSettings.cs) |
| `ReferenceRoofs/` : ensembles toit-pignon et charpentes dérivés | [SculptReferenceTownRoofs.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SculptReferenceTownRoofs.cs) |
| `ReferenceWindows/` : vitres ambrées et panneaux de lanternes | [WarmReferenceWindows.cs](../../../../../Unity3D/Arkya/AgentScripts/World/WarmReferenceWindows.cs), [RefineWarmPaneCores.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineWarmPaneCores.cs), [ReferenceAmberGlass.shader](../../../../../Unity3D/Arkya/AgentScripts/World/ReferenceAmberGlass.shader) |
| `ReferenceAtmosphere/` : ciel dérivé | [RefineVillageReferenceAtmosphere.cs](../../../../../Unity3D/Arkya/AgentScripts/World/RefineVillageReferenceAtmosphere.cs), [ReferencePaintedSky.shader](../../../../../Unity3D/Arkya/AgentScripts/World/ReferencePaintedSky.shader) |
| Relevé, captures et contrôles | [SurveyTownLife.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SurveyTownLife.cs), [ReviewLivedInTown.cs](../../../../../Unity3D/Arkya/AgentScripts/World/ReviewLivedInTown.cs) |

Les commandes `Main()` d’authoring refusent leurs dossiers ou racines déjà présents. Elles décrivent une application initiale : **ne pas les relancer pour écraser les retouches de la scène**. Modifier les dérivés possédés, conserver leurs `.meta`, puis sauvegarder et refaire les contrôles utiles. Les sources de matières, textures et végétaux ainsi que les packs Scinty restent séparés et inchangés.

## État des vérifications

- Les rapports d’authoring indiquent l’application de la composition, des matériaux, des jardins étendus et de leurs retouches, des fenêtres, des toitures et de leurs raccords, et du ciel avec ses nuages retouchés. Les rapports de palette, de réponse lumineuse, de fenêtres et d’atmosphère ne signalent pas d’erreur de shader.
- La baseline complète est conservée dans [baseline.json](baseline.json), avec l’[état de l’éditeur](editor-baseline.json) et l’[inventaire initial de lumière](lighting-before.json).
- Le [contrôle final des jardins](reference-gardens-validation.json) réussit après restauration des 17 matériaux : **132 440 brins examinés, zéro intrusion dans les emprises réservées et moins de 0,001 mm d’erreur d’appui au Terrain**. Les trois niveaux totalisent **995 024 / 667 265 / 534 824 triangles**.
- Le [contrôle du groupe complémentaire](reference-canopy-frame-validation.json) confirme **14 arbres, 499 240 triangles et 42 renderers**. L’[audit des contacts](reference-canopy-frame-contacts.json) ne relève aucun contact avec les colliders pris en charge, aucun chevauchement restant à examiner sur les colliders non convexes et aucune intrusion dans les voies. L’[audit des surfaces de toit](reference-canopy-roof-contacts.json) ne relève **aucune intersection sur 4 249 tests de triangles** ; il concerne les surfaces statiques, hors déplacement des feuilles par le shader.
- La [validation générale finale](validation.json) réussit avec **2 503 colliders et 9 768 transformations conservés**, ainsi que le Terrain, les profils et les sources inchangés. La scène est sauvegardée et laissée en Edit Mode. Les [vérifications du ciel](reference-atmosphere-validation.json) et des [neuf toitures](reference-roofs-validation.json) réussissent également, avec **296 638 triangles de toiture**.
- Les shaders utilisés dans la ville ne signalent aucune erreur. Le [relevé de console](../../../../../artifacts/reference-console-status.json) indique qu’aucune compilation n’est en cours ni en échec ; la console conserve toutefois **une erreur et 33 avertissements**. Son historique n’est donc pas présenté comme vide.
- Aucun nouveau test de parcours réseau, de charge ou de performance GPU n’est revendiqué pour cette passe.
