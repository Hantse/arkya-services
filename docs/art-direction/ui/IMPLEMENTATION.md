# Intégration des thèmes UI dans Unity

18 octobre 2026. Les trois directions sont appliquées à la création de
personnage, au panneau social et au HUD de l'exploration locale de l'Île de
l'Aube. La correction en cours reprend les écarts annotés par l'utilisateur
par rapport à la [V3 Encre & cuivre validée](hud/01-encre-cuivre-v3.png).
Les composants du HUD complet sont présents dans la couche de présentation ;
leurs données de gameplay restent à raccorder aux systèmes faisant autorité.
La section de vérification ci-dessous décrit la passe précédente, avant ces
corrections.

## Thèmes et accès

**Options → Interface → Thème** propose Encre & cuivre, Carnet d'exploration
et Pierre de mana, avec un aperçu et un repère de sélection. Le changement
s'applique immédiatement aux composants communs : panneaux, cadres,
boutons, textes et fenêtres. Les couleurs d'apparence du personnage et les
couleurs fonctionnelles de gameplay sont indépendantes du thème.

Le bouton Options de la création et **Échap** ouvrent le menu. En exploration,
Échap ferme d'abord une fenêtre ouverte ; un nouvel appui ouvre les options.
Le panneau social conserve sa propre fermeture par Échap. Le focus clavier
reste dans la fenêtre active et revient au contrôle précédent à sa fermeture.
Les commandes **Amis Discord** et **Retour à la création** se trouvent désormais
dans les options d'exploration ouvertes par Échap, libérant la place du chat
en bas à gauche.

`InterfaceTheme` conserve le choix dans PlayerPrefs sous la clé
`Arkya.UI.Theme`, puis le restaure au prochain lancement. Les identifiants
stables sont `ink-copper`, `explorer-journal` et `mana-stone`. Encre & cuivre
est la valeur initiale et le repli lorsqu'un identifiant est inconnu. Cette
préférence appartient au client et ne modifie aucun contrat serveur.

## Matières et silhouettes

Les trois [kits de sprites peints](SPRITE-KITS.md) restent utilisés pour la
création, les fenêtres et les thèmes Carnet d'exploration et Pierre de mana :
cuivre et encre, bois/cuir et parchemin, ardoise aux coins taillés.
`InterfaceSkin` partage leurs textures et applique les bordures des panneaux.
Pour le HUD Encre & cuivre, `ReferenceHudChrome` remplace les cadres ornés par
des traits natifs fins : anneaux sans gemmes, cases arrondies, jauges en capsule
et nom de zone sur fond sombre discret. Les PNG des anciens kits restent
inchangés.

Les nouvelles feuilles `HudV3` fournissent 31 sprites peints : objets,
icônes d'action et portraits de groupe. Carte, journal ouvert avec plume et
bourse à cordon remplacent les trois anciens pictogrammes dans tous les thèmes.
Le HUD utilise **Gelasio Bold**, sous licence SIL OFL 1.1, issue du dépôt
officiel [SorkinType/Gelasio](https://github.com/SorkinType/Gelasio) ; sa licence
est fournie dans `Art/Fonts/Gelasio-OFL.txt`. Cormorant Garamond SemiBold reste
la police de titre des autres écrans ; le corps des formulaires conserve une
police sans empattements.

Le portrait est un rendu ponctuel de l’apparence actuelle du personnage,
avec une caméra et un éclairage temporaires. Leurs objets sont désactivés
avant destruction et les layers du personnage sont restaurés après le rendu.
Les masques des éclairages du monde sont également restaurés ; ils ne
participent pas à l’éclairage ponctuel du portrait.
La texture de 192 pixels est libérée au retour à la création. La minimap
garde le terrain réel, avec échantillonnage interpolé et flèche d’orientation.
Les boutons carte et journal ont un cercle fin autour de leurs illustrations.
La minimap et le portrait Encre & cuivre utilisent également des anneaux fins,
conformément à la référence.
Un calque calme le grain sous le texte des grands panneaux, tandis que les
cadres et les petits aperçus conservent leurs matières.

Les choix d’apparence portent une coche de sélection et un repère de focus
distincts. Les couleurs des teints, cheveux, yeux et étoffes sont préservées.
La liste des personnages ne réserve plus de hauteur lorsqu’elle est vide.
Dans les options, l’aide rassemble des groupes touche/action séparés.

## Exploration locale

Le centre du jeu reste dégagé. Le HUD présente l'identité, le portrait et son
badge de niveau, la minimap et ses commandes, deux rangées de douze cases,
une barre d'expérience avec cartouche central et une bourse en bas à droite.
Le canvas du HUD est calibré sur **1586 × 992**, puis adapté uniformément à
la fenêtre indépendamment de l'échelle des panneaux de création. À cette
résolution, les rangées mesurent 703 pixels et la barre d'XP 700 pixels.
Les repères de touches **1…=** et **Shift+1…Shift+=** restent visibles dans les
cases libres. Ils décrivent les emplacements ; aucune compétence ni exécution
d'action de combat n'est ajoutée par cette intégration visuelle.

`ExplorationHudFrame` est un instantané de présentation à champs facultatifs.
`ExplorationHudContent` affiche les jauges chiffrées, effets et durées, membres
du groupe, cible, suivi de quêtes, messages, récolte, icônes, quantités,
recharges et indication de dégâts flottants lorsqu'ils sont fournis.
L'exploration jouable ne fournit actuellement que l'identité connue et le
niveau éventuel du personnage sélectionné. Les jauges absentes restent vides
avec un tiret ; les collections absentes n'affichent aucun contenu inventé.
Le statut de connexion quitte le bas du HUD cinq secondes après son dernier
changement. Un échec qui termine l'exploration reste signalé par la création.

La carte et la minimap utilisent une image de 512 pixels calculée depuis les
hauteurs et les poids de peinture du terrain réellement chargé. Les couleurs
moyennes déclarées des textures, corrigées par les teintes des TerrainLayers,
font ressortir les plages, les chemins et les zones peintes. Le niveau marin
provient de la définition de l’île ; aucun seuil d’eau n’est inventé lorsqu’il
n’est pas fourni. La construction lit une fois les hauteurs et les alphamaps,
puis travaille sur des tableaux gérés ; elle ne lit aucun pixel des textures
GPU. Lors d’un changement des textures de terrain, actualiser leurs moyennes
dans `TerrainMapPresentation.LayerColor`.

Les boutons **+** et **−** changent réellement la portion du terrain affichée
dans la minimap : facteur 1,4, étendue limitée entre 0,10 et 0,80 de la carte,
valeur initiale 0,28. L'indicateur soleil/lune lit `DayNightEnvironment` et son
heure courante ; son infobulle indique l'heure du monde. Il ne modifie pas le
cycle d'éclairage et ne possède aucune règle de gameplay.

Le repère suit la position du visuel piloté par
les snapshots Netcode. **M** ouvre la carte, **J** le journal et **I** le sac.
Le journal indique qu'aucune quête n'est active dans cette exploration ;
l'inventaire explique que son contenu et les récoltes ne sont pas encore
reliés. Le social existant reste accessible depuis les options d'exploration.

`CharacterCreationScreen.IsUiInputBlocked` regroupe les options, le social
et les fenêtres d'exploration. `CharacterLocalPlaytest` neutralise alors les
entrées de déplacement et cesse de modifier la caméra ; la simulation serveur
continue. Les thèmes et le HUD restent dans la couche de présentation.

## Aperçu de la référence dans l'Editor

En Play Mode, lorsque l'île est chargée et le HUD visible,
**Tools → Arkya → UI → Aperçu HUD V3** applique
`HudV3ReferenceFixture.Create()` aux mêmes composants que le jeu. Cette fixture
Editor contient les valeurs de l'illustration : Aëlan niveau 5, vie 840/1000,
endurance 72/100, expérience 62 %, Lyra/Borin/Néa, Sanglier, quêtes, chat,
bois et actions illustrées. Le repère doré
**APERÇU DA · données de maquette** identifie cet aperçu en bas à gauche.
**Revenir aux données du jeu** réapplique l'identité réelle, conserve le niveau
marin et retire ce repère. Ces fichiers Editor sont exclus du build ; ils
n'écrivent aucun état de simulation ou de persistance.

## Composants et limites

Les sources sont dans
[`Assets/Arkya/Characters/Runtime/UI/`](../../../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/UI/) :
`InterfaceTheme.cs` possède la préférence et les options,
`InterfaceSkin.cs` les kits, `HudV3Art.cs` les nouveaux sprites,
`ReferenceHudChrome.cs` les cadres fins Encre & cuivre,
`CharacterPortrait.cs` le rendu du visage,
`TerrainMapPresentation.cs` les couleurs et le relief de la carte,
`InterfaceThemes.uss` les palettes communes, `CharacterCreation.uss` les
composants de création et du social, `ExplorationHud.cs` les fenêtres et la
carte, `ExplorationHudFrame.cs` le contrat de présentation,
`ExplorationHudContent.cs` les contenus facultatifs, et `ExplorationHud.uss`
la disposition du HUD. Le même arbre de composants est réutilisé pour les
trois thèmes.

Les fichiers modifiés ou ajoutés pour cette correction sont `CharacterCreation.uxml`,
`CharacterCreationScreen.cs`, `CharacterPortrait.cs`, `ExplorationHud.cs`,
`ExplorationHud.uss`, `ExplorationHudContent.cs`, `ExplorationHudFrame.cs`,
`InterfaceSkin.cs`, `InterfaceTheme.cs`, `ReferenceHudChrome.cs`, `HudV3Art.cs`,
`HudMoonIcon.cs`, `DiscordSocialPanel.cs` et la référence à l'assembly du cycle jour/nuit,
les trois PNG `Art/Resources/HudV3/` et la fonte Gelasio avec sa licence.
Les outils dédiés sont `Editor/HudV3ReferenceFixture.cs`,
`Editor/HudV3PreviewMenu.cs`, `AgentScripts/UI/ImportHudV3.cs` et l'extension
du driver `AgentScripts/UI/ReviewInterface.cs` pour la capture de référence.
Les tests de présentation, de social et de retour sur l'île accompagnent ces changements.
Un import de namespace TLS manquant dans `CharacterWebSocketDriver.cs`, ajouté
par le travail parallèle sur la connexion, a également été corrigé pour compiler
le projet actuel ; cela ne modifie pas le comportement réseau.

Les composants visuels sont implémentés ; le groupe, les effets, les quêtes,
les récoltes, les ressources, l'expérience, les actions, les cibles et les
dégâts demandent encore une intégration aux données de simulation faisant
autorité dans l'île jouable. La fixture de revue reste réservée à l'Editor.
L'échelle,
le déplacement des blocs et le remappage des raccourcis restent également
des réglages à développer. Le HUD IMGUI de diagnostic du prototype d'areas
reste inchangé.

## Vérification du 18 octobre 2026

Unity **6000.6.6f1** compile les scripts et importe les feuilles USS/UXML.
Les **21 tests réussissent**, sans échec : cinq tests de présentation,
trois tests de carte, onze cas du contrat et du renderer V3, un parcours
social et un parcours connecté avec deux entrées/sorties de l'île.
Ils couvrent notamment les changements de thème, le chrome natif et sa
restauration, le focus modal, la libération du portrait, les 31 nouveaux
sprites, les valeurs inconnues et l'effacement des données de maquette.
Un défaut de retour du focus vers un bouton Discord caché a été corrigé,
puis la suite complète a été rejouée avec succès.

La revue séparée traverse création → île → retour avec client et serveur
locaux, les trois thèmes et la carte réelle. Elle réussit **14 contrôles**,
sans erreur runtime UI. Elle produit dix captures réelles de **1600 × 929**
et un [aperçu complet V3](unity-review/hud-v3-reference.png) de **1586 × 992**,
au format exact de la référence. Ce dernier utilise exclusivement la fixture
Editor et un éclairage de jour temporaire pour faciliter la comparaison.
Il ne représente pas un état de gameplay disponible dans l'île.

Le retour décharge l'île et détruit ses mondes locaux. Les préférences,
le cycle du monde, la taille de la fenêtre et la compilation des shaders
sont restaurés. Une exception de démarrage de l'indexeur **Unity Editor Search**
est enregistrée séparément dans le [rapport](unity-review/validation.json).
Les contrôles sur davantage de résolutions, les langues longues, les manettes,
le remappage des touches et un player autonome restent à couvrir.

La revue s’exécute dans une copie isolée avec
[`ReviewInterface.Begin`](../../../Unity3D/Arkya/AgentScripts/UI/ReviewInterface.cs),
afin de préserver les scènes de travail.

Captures de la création : [Encre & cuivre](unity-review/creation-ink-copper.png),
[Carnet d’exploration](unity-review/creation-explorer-journal.png),
[Pierre de mana](unity-review/creation-mana-stone.png).
Le [sélecteur de thème](unity-review/options-ink-copper.png) et la
[carte du relief](unity-review/map-mana-stone.png) sont également conservés.

![HUD Encre & cuivre exécuté dans Unity](unity-review/hud-ink-copper.png)

![HUD Carnet d’exploration exécuté dans Unity](unity-review/hud-explorer-journal.png)

![HUD Pierre de mana exécuté dans Unity](unity-review/hud-mana-stone.png)

![Aperçu V3 complet — données de maquette réservées à l'Editor](unity-review/hud-v3-reference.png)
