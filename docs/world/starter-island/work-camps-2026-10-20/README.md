# Camps de travail extérieurs à Aubefont

Cette passe ajoute un camp minier à l’est d’Aubefont et un camp de bûcherons
au nord, dans la forêt. Les bâtiments, les ateliers et leurs voies d’accès sont
des éléments de décor et de circulation ; cette passe ne crée pas de règle
de récolte, d’ouvriers simulés ni de nouvel état réseau.

Les deux camps et leurs raccords sont intégrés dans Unity. Les contrôles
géométriques réussissent **24/24**, les parcours serveur **32/32 sans chute**,
et les contrôles de protection contre la pluie et de restauration **9/9**.
Les cinq vues avant et les six vues après sont conservées ci-dessous.

## Implantation

Les distances sont mesurées horizontalement depuis le puits et le centre
d’Aubefont, **X = 550, Z = 520**. Les hauteurs sont prélevées sur le Terrain
existant ; les hauteurs du relief sont conservées.

| Camp | Centre X/Z | Distance XZ au centre d’Aubefont | Arrivée |
| --- | --- | ---: | --- |
| Mine | 700 / 540 | 151,327 m | Sud, vers 700 / 522 |
| Bûcherons | 550 / 870 | 350,000 m | Ouest, vers 528 / 870 |

Les directions des deux camps forment un angle de **82,405°** depuis la ville.
Le [plan d’implantation](layout.json) fixe ces centres et orientations. Le
[relevé du site](site-survey.json) conserve les sondages du relief.

Le camp minier associe un abri accessible, une cour de tri, un wagon sur une
voie latérale et une courte galerie construite au-dessus du Terrain. La roche
peinte et les filons réutilisent les ressources approuvées. Son raccord rejoint
la route du port au sud sans installer les constructions sur cette route.
Le [rapport minier](mining-authoring.json) mesure son emprise rendue entre
X = 686,295–713,455 et Z = 529,136–558,212 ; voir aussi le
[détail de construction](MINING.md).

Le camp des bûcherons associe une cabane, un abri de sciage et une cour de travail
avec le bois stocké hors de l’allée principale. Son accès rejoint la boucle
forestière existante à l’ouest. Les chemins sont peints localement et les
marches adaptent les seuils au relief existant.
Le [rapport des bûcherons](logging-authoring.json) décrit la cabane de 8 × 6 m,
l’abri de sciage de 12 × 8 m et la clairière de 40 × 40 m. L’installation retire
28 instances naturelles locales ; les assets approuvés restent conservés.
Les [raccords peints](connections-authoring.json) mesurent 17,939 m côté mine et
52,952 m côté forêt, sur une bande nominale de 4,5 m. Leur pente maximale
échantillonnée reste inférieure à 10°. Huit instances de végétation sont
retirées sur le raccord forestier.

## Comparaison visuelle

Les rapports [avant](before/captures.json) et [après](after/captures.json) confirment cinq et six vues en
1 600 × 1 000 pixels, avec restauration de l’état de présentation et du drapeau
de modification de la scène. Les positions de caméra, cibles et champs de
vision sont figés dans la [référence initiale](baseline.json).

| Vue | Avant | Après |
| --- | --- | --- |
| Mine — surplomb local | [Image](before/miners-overview-day.png) | [Image](after/miners-overview-day.png) |
| Mine — hauteur joueur | [Image](before/miners-player-day.png) | [Image](after/miners-player-day.png) |
| Bûcherons — surplomb local | [Image](before/woodcutters-overview-day.png) | [Image](after/woodcutters-overview-day.png) |
| Bûcherons — hauteur joueur | [Image](before/woodcutters-player-day.png) | [Image](after/woodcutters-player-day.png) |
| Positionnement ville et deux camps | [Image](before/town-and-work-camps-day.png) | [Image](after/town-and-work-camps-day.png) |

`ReviewIslandWorkCamps.After()` reprend ces cinq cadrages de jour et ajoute une
[vue de la cour des bûcherons](after/woodcutters-courtyard-day.png) depuis
l’intérieur de la clairière. Ce cadrage complémentaire montre l’ensemble
derrière les couronnes des arbres conservés, qui masquent la vue de surplomb
initiale.

## Budgets mesurés

| Camp intégré | Triangles, tous LOD | Composants de rendu | Groupes LOD | Colliders |
| --- | ---: | ---: | ---: | ---: |
| Mine, pignons refermés | 317 759 | 138 | 40 | 69 |
| Bûcherons | 429 412 | 85 | 21 | 93 |
| Ensemble | 747 171 | 223 | 61 | 162 |

Ces chiffres incluent les instances de roche et de minerai réutilisées.
Le total tous LOD ne représente pas le nombre de triangles simultanément
dessinés. Les deux camps partagent **23 matériaux** référencés ; ils n’ajoutent
aucune lumière. La mine atteint au maximum 208 479 triangles si tous ses
objets utilisent leur LOD détaillé. La [fermeture des pignons miniers](mining-roof-infill.json)
ajoute 56 triangles et un Renderer en conservant les anciens maillages et
colliders. Ces budgets de contenu ne sont pas des mesures de temps GPU.

## Vérifications

`ValidateIslandWorkCamps.Main()` réussit [24/24 contrôles](geometry-validation.json) sur les centres, distances et directions,
les vrais colliders des accès, les meshes, matériaux, shaders et métadonnées.
La signature initiale du village comprend ses transforms, ses références
graphiques, ses colliders et ses lumières ; les hauteurs du Terrain ont leur
propre empreinte SHA-256. La peinture globale du Terrain est enregistrée pour
référence, mais les nouveaux chemins peuvent la modifier localement.

`ValidateIslandWorkCamps.Traverse()` utilise le véritable
`CharacterLocalTestServerSystem`, avec les colliders de la scène, en marche et
en course à **60 et 20 Hz**. Chaque parcours revient sans saut ni téléportation
depuis la route existante jusqu’au centre du camp, puis les branches dédiées
exercent l’entrée, l’abri et la galerie ou la cabane.
Le [rapport de circulation](server-traversal.json) confirme **32/32 réussites**,
**21 476 ticks**, **1 561,620 m parcourus** et **zéro tick en l’air**.

Le corps conserve un rayon de **0,30 m**, une hauteur de **1,75 m** et un
contrôle complémentaire de **2 m** de dégagement. Les hauteurs des destinations
et des paliers sont vérifiées à **4,5 cm** près. Aux seules marches
intermédiaires, plus courtes que le diamètre d’appui du personnage, le contrôle
vérifie un support physique sous le disque du pied : un pied encore appuyé sur
la marche voisine ne constitue pas une chute. La simulation doit rester au sol
sur tout le trajet.

`ConfigureWorkCampShelters.Main()` a [conservé les 38 références existantes et ajouté cinq couvertures](shelters-authoring.json),
pour un total de **43** : un mesh de toiture minier et les
quatre renderers des deux LOD des toits des bûcherons. Le plafond de la galerie
possède déjà son collider. `Validate()` observe ensuite les vraies frames de
pluie sous les quatre couvertures et en extérieur, avec vérification de la
surface réelle au-dessus du point de vue. Les
[neuf contrôles réussissent](shelters-runtime-validation.json) : huit positions
réelles, plus la restauration de la caméra et des réglages. Les gouttes
s’arrêtent dans l’abri minier, la galerie, la cabane et l’atelier de sciage ;
elles reprennent en sortant.

La [scène a été sauvegardée en Edit Mode](saved-state.json), sans modification
restante à enregistrer. Le [relevé final de l’éditeur](editor-console-final.json)
confirme **zéro erreur**, **zéro avertissement** et aucun échec de compilation.
Ces deux relevés reprennent les résultats de `final-save-command.json` et
`final-console-command.json` dans `artifacts/work-camps-2026-10-20`.

Ces preuves concernent l’éditeur Unity et le Play Mode. Aucun build joueur,
test réseau multi-client ni mesure de performance sur machine cible n’est
revendiqué par cette passe.

## Sources

- [StarterIsland.unity](../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Scenes/StarterIsland.unity) : scène intégrée et sauvegardée.
- [BuildMiningWorkCamp.cs](../../../../Unity3D/Arkya/AgentScripts/World/BuildMiningWorkCamp.cs) : abri, galerie et cour minière.
- [CloseMiningShelterGables.cs](../../../../Unity3D/Arkya/AgentScripts/World/CloseMiningShelterGables.cs) : fermeture des pignons et des jours sous les rives.
- [BuildLoggingWorkCamp.cs](../../../../Unity3D/Arkya/AgentScripts/World/BuildLoggingWorkCamp.cs) : cabane, sciage et stockage du bois.
- [ConnectIslandWorkCamps.cs](../../../../Unity3D/Arkya/AgentScripts/World/ConnectIslandWorkCamps.cs) : raccords aux routes et peinture locale.
- [ReviewIslandWorkCamps.cs](../../../../Unity3D/Arkya/AgentScripts/World/ReviewIslandWorkCamps.cs) : captures et référence de préservation.
- [ValidateIslandWorkCamps.cs](../../../../Unity3D/Arkya/AgentScripts/World/ValidateIslandWorkCamps.cs) : géométrie et parcours serveur.
- [ConfigureWorkCampShelters.cs](../../../../Unity3D/Arkya/AgentScripts/World/ConfigureWorkCampShelters.cs) : références des toits et validation de pluie.

Les assets propres aux camps sont isolés sous
[WorkCamps/Mining](../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/WorkCamps/Mining)
et [WorkCamps/Logging](../../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/WorkCamps/Logging),
avec leurs fichiers `.meta`. Les ressources approuvées et les preuves des
passes précédentes sont conservées.
