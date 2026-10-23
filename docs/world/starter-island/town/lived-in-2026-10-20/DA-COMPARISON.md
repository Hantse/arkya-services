# Comparaison visuelle avant densification — 20 octobre 2026

La demande vise explicitement la qualité graphique d’**Overwatch** : volumes travaillés, arêtes adoucies, couleurs franches, matières distinctes et éclairage qui décrit les formes. La ville présente encore un écart sensible ; ajouter des accessoires ne suffit pas à le fermer.

Les images consultées sont la [vue d’ensemble Unity](../art-finish-2026-10-20/after/village-overview-day.png), la [rue du marché](../art-finish-2026-10-20/after/market-street-day.png) et le [coin de la mairie](../art-finish-2026-10-20/after/civic-corner-day.png), comparées aux planches [Royaumes de l’aube](../../../../art-direction/01-royaumes-aube-gameplay.png), [Cités d’éther](../../../../art-direction/02-cites-ether-gameplay.png), [Terres des anciens](../../../../art-direction/03-terres-anciens.png), au [tronc V3 approuvé](../../../../art-direction/resources/references/arbre-peint-v3/03-moussu/03-moussu-tronc.png) et au [buisson mystique approuvé](../../../../art-direction/resources/buissons/assets/etale-mystique/etale-mystique-preview.png).

Les planches de ville sont des **explorations générées**, pas des captures du moteur ni une architecture approuvée. Le [kit de structures](../../../../art-direction/resources/structures/README.md) reste également une proposition. L’approbation documentée porte sur l’arbre V3 et les [familles d’objets naturels](../../../../art-direction/resources/README.md).

## Cinq écarts observables

| Écart dans les captures Unity | Direction de correction |
|---|---|
| **Maisons isolées sur des bandes d’herbe vides.** La vue d’ensemble montre une succession d’objets séparés ; les références organisent des bords occupés et plusieurs plans de profondeur. | Former des scènes d’activité compactes : un objet principal, deux ou trois volumes secondaires et une masse végétale arrière. Garder la circulation lisible entre ces groupes. |
| **Volumes répétitifs.** Les pans de toit et les longues pièces de bois restent très réguliers ; les petits accessoires ne portent pas assez de silhouette. | Roues réellement rondes, rayons épais, planches séparées, rebords adoucis et bottes de foin modelées. Les charrettes proposées corrigent les accessoires ; les grandes formes des toitures restent un travail distinct. |
| **Valeurs et couleurs trop proches.** Pierre, sol et certaines façades se rejoignent dans un gris verdâtre ; la variation peinte devient peu lisible à distance. | Séparer pierre ivoire, bois chocolat/miel et métal bleu sombre. Conserver les accents turquoise et corail des tissus. Employer de grandes variations peintes lisibles, avec une usure locale mesurée. |
| **Végétation ponctuelle et raccords faibles au sol.** Les petites touffes espacées n’occupent pas l’échelle des cours ; les objets semblent posés sur un tapis uniforme. | Grouper les buissons validés derrière les scènes d’activité, puis herbes et fleurs au pied. Des contours de terre irréguliers relient ces ensembles au terrain ; les masses végétales doivent se lire avant les fleurs individuelles. |
| **Émission et modelé déséquilibrés.** Dans la rue du marché, les lanternes sont presque blanches tandis que les façades restent ternes ; leurs volumes se lisent mal. | Conserver un corps coloré et ombré, avec un cœur lumineux contenu. Retrouver un contraste entre lumière chaude et ombres fraîches, et vérifier les arêtes sous la lumière existante. |

## Huit scènes d’activité proposées

Les pivots ci-dessous proviennent du [relevé de scène](survey.json). Les centres sont des propositions en coordonnées monde X/Z, à ajuster après contrôle du terrain, des végétaux déjà placés et des passages. Ils ne constituent pas une validation de collision.

| Propriétaire : pivot actuel X/Z | Centre proposé X/Z | Composition |
|---|---|---|
| Auberge : 525 / 540 | 529 / 531 | Livraison : charrette de caisses, une caisse ouverte au sol, tonneau et herbes à l’arrière. |
| Atelier des runes : 558 / 550 | 551 / 549 | Réserve d’artisan : caisses de tailles différentes, petit lot de bois et buisson mystique derrière. |
| Maison des voyageurs : 570 / 558 | 576 / 551 | Coin de halte : trois bottes de foin liées, caisse ouverte et végétation basse. |
| Herboristerie : 581 / 534 | 578 / 542 | Approvisionnement : charrette, caisses ouvertes et groupes de plantes corail/ivoire, orientés vers le jardin. |
| Forge : 581 / 500 | 576 / 506 | Stock de combustible : bois, caisses robustes et tonneau ; touches de métal bleu sombre. |
| Entrepôt : 568 / 493 | 563 / 486 | Départ des marchandises : charrette chargée, deux caisses au sol et bord végétal dense. |
| Tisserands : 553 / 492 | 559 / 488 | Stock latéral : empilement décalé de caisses et caisse ouverte ; reprendre un accent coloré du tissu voisin. |
| Maison du verger : 535 / 493 | 527 / 494 | Retour de récolte : charrette, foin et caisse ouverte, avec un buisson arrondi pour fond. |

Les herbes, fleurs, buissons, troncs et souches peuvent reprendre les prefabs sous `Assets/Arkya/ArtDirection/Optimized/Prefabs/`. Les tonneaux détaillés existent sous `Assets/Arkya/World/StarterIsland/Town/VillageUpgrade/Barrels/`. Les anciens lots combinés de paille ne doivent pas être instanciés en bloc : ils contiennent déjà plusieurs placements.

Le nouveau [TownLifePropGeometry.cs](../../../../../Unity3D/Arkya/AgentScripts/World/TownLifePropGeometry.cs) prépare les géométries isolées de charrette, caisse ouverte/fermée et balle de foin. Il ne place aucun objet et ne modifie aucun matériau ou collider. La compilation, les captures finales et les contrôles de circulation appartiennent à la passe d’intégration ; cette comparaison ne constitue ni une validation finale de qualité ni une mesure de performance.

## Références concrètes fournies pendant la passe

L’utilisateur a ensuite fourni [treize captures de référence](../../../../art-direction/references/town-2026-10-20/README.md), dont un village densément planté, une auberge, des ruines et une église. Elles précisent et prennent le pas sur le raccourci « proche d’Overwatch » pour cette passe. Elles ne sont pas des ressources importées dans le jeu. La provenance exacte et les droits sur les ressources qu’elles représentent ne sont pas établis.

La comparaison ci-dessous a été effectuée sur les captures de la passe après ajout des accessoires, dérivation des matériaux et atténuation des pigments des charrettes : [vue d’ensemble](after/village-overview-day.png), [rue du marché](after/market-street-day.png), [cour de l’auberge](after/inn-market-court-day.png) et [crépuscule](after/town-twilight.png). Ces chemins peuvent être actualisés par les captures suivantes ; ce constat décrit l’état observé avant l’intégration des corrections guidées par les nouvelles références.

| Priorité | Écart encore visible | Correction à vérifier |
|---|---|---|
| **1 — Végétation et raccord au sol** | La vue d’ensemble reste dominée par un tapis d’herbe jaune-vert, avec de petits groupes séparés. Au niveau du joueur, de grandes surfaces planes séparent les façades des chemins. Dans les références du village, prairie, buissons et canopées relient les bâtiments en plusieurs plans. | Former des bords de jardins continus et asymétriques, avec de l’herbe en nappes, des buissons groupés à différentes hauteurs et quelques arbres aux emplacements structurants. Vérifier les parcours réservés et conserver des ouvertures sur les portes et la mairie. |
| **2 — Lumière, ciel et profondeur** | Les façades et le terrain partagent une dominante gris-verte ; le ciel reste pâle. Le crépuscule assombrit l’ensemble sans établir de foyers chauds de fenêtres ou de lanternes. Dans les références, les ombres bleu-vert et la lumière chaude dessinent les volumes, même de jour. | Équilibrer soleil chaud, ambiance fraîche et ciel plus bleu ; contrôler les fenêtres et lanternes à deux heures de la journée. Comparer les pierres et le bois dans les mêmes vues pour éviter qu’une saturation globale remplace le modelé. |
| **3 — Silhouettes architecturales** | Les charrettes ont gagné des roues et des rebords lisibles, mais les bâtiments restent surtout des façades rectangulaires sous deux pans presque droits. Les références utilisent des pentes creusées, des débords épais et des volumes secondaires plus marqués. | Travailler ensemble toit, pignon et bargeboards pour obtenir une courbe visible sans fissure entre pièces. Préserver murs, fenêtres, seuils et collision ; revoir d’abord une silhouette lisible en vue de rue avant de généraliser. |

Le diagnostic des toits est cohérent avec l’outil existant [SoftenVillageSilhouettes.cs](../../../../../Unity3D/Arkya/AgentScripts/World/SoftenVillageSilhouettes.cs) : sa flèche longitudinale est d’environ dix centimètres, sa remontée d’égout de quatre centimètres et son déplacement maximal de quinze centimètres. C’est une correction discrète de régularité, pas la grande courbe des références. Le script historique attend par ailleurs dix maisons et un ancien état de toiture de mairie ; le relancer tel quel sur la scène actuelle ne constitue pas une méthode sûre. Un nouveau dérivé de présentation doit partir des meshes actuellement utilisés et appliquer une déformation cohérente aux éléments joints.

La densification et la palette déjà réalisées sont utiles, mais ces captures ne permettent pas de revendiquer le niveau de qualité des nouvelles références. Les trois priorités ci-dessus restent des critères de comparaison visuelle après intégration, et non des changements déjà validés par ce document.
