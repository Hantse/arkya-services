# HUD d’exploration — référence AION 2

La capture fournie le 23 octobre 2026 guide la disposition du HUD en jeu :
bloc de combat compact en bas au centre, navigation en haut à droite et centre
de l’image dégagé. Les illustrations et les données restent celles d’Arkya.

- Vie rouge à gauche et mana bleue à droite du niveau central en losange.
  L’endurance forme une ligne fine sous le mana ; son détail reste accessible
  au survol. Le portrait accompagne les compétences à gauche.
- Deux rangées de douze emplacements, avec les raccourcis existants, quantités
  et temps de recharge. L’expérience se lit sur la ligne sous les compétences.
- Mini-carte rectangulaire avec zoom, orientation, repère et heure du monde.
  Les boutons Personnage (`C`), Inventaire (`I`), Journal (`J`) et Carte (`M`)
  occupent la petite barre au-dessus ; les quêtes reçues s’affichent dessous.
- Cible en haut au centre, groupe à gauche, effets au-dessus du bloc de combat,
  conversation et annonces en bas à gauche. Les signalements `F8` et
  suggestions `F9` restent accessibles dans ce coin.

Le canevas logique de référence est de 1600 × 900. La mise à l’échelle conserve
les ancrages aux bords dans les autres formats. Le cadrage UV de la mini-carte
tient compte de sa forme rectangulaire pour ne pas étirer le relief.
Son cadre conserve un centre transparent dans les trois thèmes.

Les jauges observent les valeurs reçues ; une valeur indisponible reste « — ».
Cette refonte n’ajoute aucune règle de combat, donnée réseau ou action de
compétence. Les limites des fenêtres inventaire et journal restent indiquées
dans le jeu. Les données de démonstration du menu Editor **Aperçu HUD V3**
restent signalées par son marqueur ; elles ne sont pas introduites en jeu.

Implémentation Unity : `CharacterCreation.uxml`, `ExplorationHud.uss`,
`ExplorationHud.cs`, `ExplorationHudContent.cs`, `ReferenceHudChrome.cs` et
`InterfaceSkin.cs`, sous `Assets/Arkya/Characters/Runtime/UI/`.

Les tests ciblés `HudV3PresentationTests` et `ExplorationCombatHudTests`
contrôlent les valeurs inconnues, les jauges, la stabilité des 24 emplacements
et les nouveaux points d’ancrage.

## Vérification du 23 octobre 2026

- Unity `6000.6.6f1`, URP `17.6.0` ; compilation et import des styles dans l’Editor.
- 20 cas ciblés HUD exécutés par MCP : aucun échec.
- Exploration StarterIsland avec serveur local et client connectés : jauges
  réelles, mini-carte et portrait affichés ; barre d’expérience avec une surface
  de remplissage de 2 px à 1600 × 900.
- Ouverture des quatre fenêtres depuis leurs boutons, ouverture de la carte
  par `M` et fermeture par `Échap`.
- Captures examinées à 1600 × 900, 1280 × 720 et 1920 × 810. Le bloc de combat
  reste centré et les éléments restent dans le panneau en format large.
- Vérification visuelle des trois thèmes, notamment du relief et du repère
  visibles derrière le cadre rectangulaire de mini-carte.

[Capture du HUD réel](live.png) et [aperçu avec données de démonstration](preview.png).
Le marqueur de maquette a été placé en haut à gauche pour la capture d’aperçu ;
les valeurs de quêtes, groupe et compétences de cet aperçu ne décrivent pas
des fonctionnalités actives de l’exploration locale. Le mode Play, le thème
et les dimensions de Game View ont été restaurés après la revue.
