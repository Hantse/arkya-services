# Menu Échap et réglages du joueur

Le menu **Échap** du client propose **Reprendre**, **Options**, **Déconnexion**
et **Quitter le jeu**. Il est intégré à l’écran de création/sélection des
personnages et à son parcours d’exploration.

## Navigation et sortie du monde

- **Échap** ouvre le menu. Si une fenêtre du HUD, les options d’apparence ou
  le formulaire de signalement est déjà ouvert, la touche ferme d’abord cette
  fenêtre.
- **Options** donne accès aux onglets **Graphismes** et **Son**, ainsi qu’aux
  thèmes existants par **Apparence de l’interface**.
- **Échap** dans les options revient au menu ; dans le menu, il reprend le jeu.
  Pendant un aperçu d’affichage, il annule d’abord cet aperçu.
- **Déconnexion** quitte le monde ou l’essai local et revient aux personnages.
  Le bouton est désactivé lorsqu’aucun monde n’est actif. Cette action est
  distincte de la déconnexion du compte Arkya.
- **Quitter le jeu** ferme le client natif. Dans l’Editor, il arrête le mode Play.
  Dans un navigateur, le bouton est désactivé et indique de fermer l’onglet.

Le menu bloque les commandes locales de déplacement, de saut et de caméra.
Le monde multijoueur continue sa simulation ; ouvrir le menu ne met pas le
serveur en pause. Une transition entre mondes ferme le menu et annule un
éventuel aperçu d’affichage.

## Graphismes

Les changements graphiques sont validés avec **Appliquer les graphismes**.

| Réglage | Comportement |
| --- | --- |
| Résolution | Liste des dimensions fournies par Unity pour l’écran, sans doublons, avec la taille actuelle de la fenêtre. |
| Mode d’affichage | **Fenêtré** ou **Plein écran sans bordure**. Le plein écran exclusif n’est pas proposé. |
| Qualité graphique | Niveaux disponibles dans `QualitySettings.names` pour le client utilisé. |
| Synchronisation verticale | Synchronisation avec l’écran sur les plateformes natives ; elle prend la priorité sur la limite d’images. |
| Limite d’images par seconde | **Illimitée**, **30**, **60**, **90**, **120**, **144**, **165** ou **240 IPS**. Le contrôle est désactivé quand la VSync native est cochée. |

Le projet possède actuellement les préréglages Unity **PC** et **Mobile**.
Leur disponibilité dépend de la plateforme et des niveaux inclus dans le
build. Cette interface expose ces niveaux existants ; elle n’ajoute pas de
nouveaux profils Bas/Moyen/Élevé ni de réglages individuels des ombres,
textures ou effets.

Les scènes qui utilisent `IslandRenderProfile` conservent leur pipeline URP
imposé par ce composant. Changer le niveau Unity y applique les paramètres
du niveau, mais ne remplace pas durablement le pipeline de la scène. Les
différences visuelles entre niveaux restent donc limitées par ce profil.

### Confirmation d’affichage

Un changement de résolution ou de mode démarre un aperçu de **15 secondes**
mesuré en temps réel, indépendamment de la vitesse de simulation. Le joueur
doit choisir **Conserver** pour enregistrer le nouvel affichage.

**Annuler**, **Échap**, la fermeture du menu, la destruction de l’interface
ou l’expiration du délai rétablissent l’affichage confirmé précédent. Les
changements de qualité, de VSync, de limite d’images et de son déjà appliqués
restent conservés. L’affichage testé n’est jamais écrit comme réglage de
démarrage avant confirmation : une fermeture du processus pendant l’aperçu
ne le valide pas.

Pendant la confirmation, l’onglet Graphismes reste affiché et ses champs
sont bloqués. L’onglet Son redevient accessible après confirmation ou retour
à l’affichage précédent.

## Son

**Volume général** règle le niveau de **0 à 100 %**. **Couper tous les sons**
met le volume à zéro sans perdre la valeur du curseur. Ces changements sont
appliqués et sauvegardés immédiatement.

Le réglage passe par `AudioListener.volume` et concerne les sons Unity du
client. Les ambiances existantes ne disposent pas encore de groupes de mixer
séparés : cette tranche propose uniquement le volume général et le silence,
sans curseurs Musique/Effets ni contrôle des sons externes au client Unity.

## Sauvegarde locale et limites de plateforme

Les valeurs sont conservées sur cet appareil via `PlayerPrefs`, sous la clé
**`Arkya.PlayerSettings.v1`**, au format JSON version 1. Elles sont restaurées
à la création de l’interface du client. Elles ne sont pas enregistrées dans
le personnage, envoyées aux services ou synchronisées entre appareils.
Le thème de l’interface garde sa clé indépendante `Arkya.UI.Theme`.

Les valeurs relues sont validées : résolution présente dans la liste connue,
mode proposé, niveau de qualité borné, limite d’images autorisée et volume
fini entre zéro et un. Des données illisibles ou d’une version inconnue
reviennent aux valeurs par défaut. Une résolution sauvegardée absente du
nouvel écran revient aux dimensions actuelles.

- **Editor** : l’interface, les valeurs et le délai de confirmation fonctionnent
  en mode Play, mais `Screen.SetResolution` n’est pas appelé. Le menu ne
  redimensionne donc pas la Game View. Le son et les paramètres Unity sont
  appliqués en mode Play.
- **WebGL** : le navigateur gère l’affichage. Résolution, mode de fenêtre et
  VSync sont désactivés dans les options. La limite d’images reste proposée
  dans les limites du navigateur ; le son utilise les capacités audio du
  navigateur.
- **Batch mode** : aucun changement de résolution ou de mode n’est appliqué.
  Les changements de son et de qualité restent exécutables en mode Play pour
  les vérifications du client.
- **Serveur dédié** : les opérations d’application graphiques et audio sont
  exclues par `UNITY_SERVER`. Ces réglages appartiennent à la présentation
  cliente et ne modifient aucune règle de gameplay autoritaire.

## Repères dans le code

Dans le dépôt Unity, sous `Assets/Arkya/Characters/` :

- `Runtime/UI/EscapeMenuPanel.cs` et `EscapeMenu.uss` : menu et contrôles.
- `Runtime/UI/PlayerSettingsValues.cs` : valeurs et validation déterministe.
- `Runtime/UI/PlayerSettingsController.cs` : persistance, application et
  confirmation d’affichage.
- `Runtime/UI/CharacterCreationScreen.cs` : cycle de vie, raccourci Échap et
  actions de sortie.
- `Tests/Editor/PlayerSettingsTests.cs` : cas déterministes de validation,
  persistance et retour d’affichage.
- `Tests/Editor/EscapeMenuRuntimeTests.cs` : parcours d’interface en mode Play.

## Validation du 23 octobre 2026

Unity `6000.6.6f1` compile les changements. Le job MCP
`54911732a57d482897af2dde38064a6d` compte **15 tests réussis, aucun échec** :
14 cas de règles/persistance et un parcours en mode Play avec serveur et
client locaux réels. Ce parcours vérifie le volume et la sourdine, la
persistance, l’annulation d’affichage, l’accès aux thèmes, le blocage puis
la reprise du déplacement et la destruction des deux mondes à la déconnexion.
Les scènes et les préférences du poste sont restaurées après le test.

Les captures du menu et des deux onglets ont été inspectées dans la Game View.
Le test général `InterfacePresentationTests` a aussi été tenté ; il échoue
avant le menu sur l’assertion de visibilité de la fiche personnage après **C**,
dans les modifications parallèles du HUD. Le parcours propre au menu valide
séparément l’accès aux thèmes. Les messages Netcode de regroupement de ticks
restent présents pendant les essais locaux.

Le redimensionnement d’un client natif exporté et les contraintes réelles du
navigateur ne sont pas validés par cette exécution dans l’Editor.

## Correctifs des rapports urgents du 24 octobre 2026

Le thème autonome fournit désormais la structure des zones défilantes et des
listes déroulantes au niveau du panneau runtime. Les popups restent en position
absolue, sans déplacer le menu, et leur fond est porté par la liste plutôt que
par toute la surface de capture des clics. Les boutons de retour et de
confirmation gardent leur hauteur lorsque le contenu doit défiler. Changer
d’onglet replace le défilement en haut ; Échap ferme d’abord une liste ouverte.

Les jauges du HUD utilisent un cadre fin propre, sans étirer le sprite d’un
bouton décoratif. Vie et mana disposent de valeurs centrées et plus grandes ;
l’endurance, l’expérience et les deux rangées de raccourcis ont des espacements
distincts. Les titres longs et les actions des fenêtres peuvent revenir à la
ligne.

`RuntimeUiLayoutTests` ajoute deux tests de géométrie runtime : options sur
1440 × 810, 980 × 600 et 640 × 480, ouverture/fermeture d’une vraie liste,
défilement jusqu’à Appliquer, puis lisibilité des jauges dans les trois thèmes.
Ces tests ont été exécutés avec succès dans Unity 6000.6.6f1 après
rétablissement de MCP et recompilation. Les options et les jauges ont aussi
été inspectées dans une capture du jeu à 1198 × 584. Le client Windows
exporté et les contraintes propres au navigateur restent hors de cette
validation Editor.
