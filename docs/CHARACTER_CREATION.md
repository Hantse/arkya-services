# Création de personnage dans Unity

La scène `Assets/Arkya/Characters/Scenes/CharacterCreation.unity`, dans le projet actif `Unity3D/Arkya/` avec Unity **6000.6.6f1**, accueille le joueur lancé depuis le launcher Windows. La connexion Discord commence dans le launcher, qui ouvre le navigateur système et remet ensuite la session Arkya au jeu. Unity revalide cette session auprès d’Identity avant de rendre visibles la sélection, les royaumes et la création de personnage. Une vue dédiée permet ensuite de personnaliser un personnage animé. L’essai Netcode local reste réservé à l’Editor sans connexion de compte.

## Entrée native par royaume — 23 octobre 2026

Après validation de la session, sans personnage ou sans historique de connexion confirmé, le jeu affiche le choix du royaume avant la création. Un compte déjà connu retrouve son dernier royaume disponible et son dernier personnage, sans lancer automatiquement la partie. **Changer de royaume** reste disponible. Un royaume fermé, supprimé ou incompatible ramène au choix avec une explication ; aucune partie locale ne remplace un serveur indisponible.

La liste affiche les personnages du royaume choisi. Les nouveaux personnages sont liés à ce royaume dès leur création ; seuls les personnages historiques sans royaume peuvent encore être rattachés lors de leur première admission. **Jouer** demande un ticket et ouvre le client natif WebSocket vers le dédié. Le souvenir du dernier royaume provient de `GET /v1/characters/entry-context`, enregistré uniquement après consommation réussie d’un ticket côté dédié. La déconnexion et une session invalide effacent le contexte du compte en mémoire. Voir le [contrat d’entrée](CHARACTER_ENTRY.md).

Les boutons d’exploration locale demeurent disponibles dans l’Editor sans compte pour le travail artistique et les tests. Ils ne sont pas proposés dans le joueur Windows distribué. Les anciennes sections de validation ci-dessous décrivent leurs instantanés datés.

Le build Windows initial `0.1.0-beta.20261023` ne contient pas ce parcours. Sa disponibilité dans le launcher dépend de la publication d’un nouveau build validé, puis du manifeste de distribution.

## Base retenue — valkyrie_2019

Le **22 octobre 2026**, l’utilisateur a choisi le package `valkyrie_2019`
comme base des personnages. Il est importé dans
`Unity3D/Arkya/Assets/Valkyrie/`. Le modèle principal est
`BaseMesh/SK_Valkyrie.fbx`, accompagné de `Prefab/SK_Valkyrie.prefab`,
de pièces séparées dans `BaseMesh/Separate/` et `Prefab/Separate/`,
d’animations, de matériaux et de textures.

L’intégration propre à Arkya se trouve sous `Assets/Arkya/Characters/Valkyrie/` :
variante `Valkyrie.prefab`, Avatar Generic, matériaux URP et contrôleur d’animation.
Les textures peintes du fournisseur sont conservées ; les copies des cartes AO
et métalliques utilisent un import linéaire. Les originaux sous `Assets/Valkyrie/`
et leurs GUID restent inchangés. Les références visuelles du 20 octobre guident
les futures adaptations artistiques.

Le catalogue `Settings/CharacterVisuals.asset` alimente l’aperçu, le personnage
local et les personnages distants. Il utilise désormais cette base commune
pour les quatre couples historiques Humain/Elfe et silhouettes A/B. Le contrat
d’apparence version 1, ses identifiants, leur ordre réseau et les choix déjà
sauvegardés restent conservés. La création de nouveaux personnages propose
uniquement **Humain, silhouette féminine, cheveux longs**, avec taille et
couleurs de peau, cheveux, iris et tissu. Les anciennes apparences se chargent
et s’explorent avec la base commune ; elles ne constituent pas des variantes
masculines, elfiques ou de coiffure déjà réalisées.

Un nouveau brouillon utilise le profil `human/b/long`, une taille de 175 cm,
le teint sable, les cheveux magenta, les yeux verts et le tissu jade.
**Créer un perso** revient à ce profil disponible, même après avoir sélectionné un
personnage historique. Les choix de ce dernier ne sont pas réécrits ; pour
créer une autre apparence à partir d’un ancien profil indisponible, l’interface
invite à ouvrir un nouveau brouillon.

Les états **Idle**, **Walk**, **Run** et **Jump** utilisent des copies natives des
clips du package. Jump reprend sa boucle en l’air tant que le serveur signale
cet état ; l’élévation déjà enregistrée dans le bassin est retirée pour ne pas
ajouter un second saut à la trajectoire serveur. **SwimIdle** et **Swim** sont des animations de présentation générées
pour le squelette Valkyrie ; leur finition artistique reste provisoire. Les
racines sont verrouillées, le root motion est désactivé, et l’Animator observe
les déplacements serveur. Les colliders du ragdoll fournisseur sont retirés de
la variante visuelle pour ne pas participer aux collisions de déplacement.

Le menu **Arkya → Characters → Integrate Valkyrie base** régénère uniquement les
assets de cette intégration et les associations du catalogue, en conservant les
GUID déjà créés. Il ne reconstruit pas la scène de création. **Rebuild creation
assets** appelle aussi cette intégration quand le catalogue Valkyrie est actif.
Les anciennes sources Humain/Elfe et leurs tests de pose restent conservés comme
prototypes historiques. Les LOD et la mesure des budgets de foule restent à faire.

## Sélection et création

Après le choix ou le retour au royaume, la vue présente le personnage au centre
et la liste de ce royaume à droite. Les actions se trouvent au bas du panneau :
**Jouer** pour rejoindre le dédié et **Créer un perso** pour ouvrir la création.
Sans compte dans l’Editor, **Explorer l’île (Editor)** conserve l’essai local.
La sélection d’un personnage relit son apparence enregistrée sans la modifier.

La création réunit les choix d’apparence, l’aperçu et la saisie du nom dans une
vue séparée. **Retour aux personnages** abandonne ce brouillon et restaure le
personnage précédemment sélectionné, ou le profil par défaut en l’absence de
sélection. Un nouvel appui sur **Créer un perso** initialise un nouveau
brouillon avec un nom vide. Après un enregistrement confirmé par Characters,
l’interface revient à la liste et sélectionne le personnage créé. En cas
d’erreur, le nom et l’apparence en cours restent disponibles pour correction.

Dans l’Editor sans connexion, la création reste utilisable pour personnaliser
le brouillon puis **Essayer sur l’île** ; l’enregistrement demeure désactivé.
Le retour de cet essai restaure la création et son brouillon. Dans le joueur
distribué, la connexion et le choix du royaume précèdent la création ; l’entrée
en jeu demande toujours l’admission d’un personnage enregistré.

### Présentation actuelle — ville 3D d’Aubefont

La révision visuelle du **23 octobre 2026** installe la sélection et la création
dans un décor 3D natif, composé à partir des bâtiments et de la végétation
d’Aubefont. Les panneaux laissent voir la cour, les maisons et le ciel peint.
La liste reste à droite avec ses actions en bas ; la création conserve ses
réglages d’apparence et sa saisie du nom dans une vue dédiée.

La variante actuelle installe cette ville au crépuscule : lumière lunaire,
fenêtres et lanternes chaudes, brume basse et lucioles de mana. Un puits
compact anime son cristal et sa surface lumineuse sur le côté de la cour.
Les effets utilisent des particules natives et un bloom limité ; le personnage
conserve un éclairage lisible au premier plan. La sélection, la création et
le portrait avec ce décor ont été inspectés dans la Game View.

`CharacterPreviewPresenter` garde la caméra sur toute la surface de rendu.
Une projection décentrée place le personnage dans l’espace libre calculé par
l’interface ; la distance s’adapte à sa géométrie et au mode silhouette ou
portrait. Le décor et le personnage sont donc rendus ensemble, avec la même
perspective. Le cadrage personnalisé est libéré à l’entrée dans l’essai local.

Le décor relève uniquement de la présentation : ses copies ne reprennent ni
scripts de gameplay, ni colliders, ni simulation autoritaire de l’île. Les
références de DA, le générateur, les limites et la validation en cours sont
décrits dans la [révision actuelle de l’interface et du décor de ville](art-direction/character-city-stage-2026-10-23/README.md).
Le [premier rapport des deux vues](art-direction/character-flow-2026-10-23/README.md)
reste un historique fonctionnel ; sa présentation a été rejetée visuellement
et ne représente plus la direction retenue.

## Ouvrir et essayer

1. Ouvrir `Unity3D/Arkya/` dans Unity 6000.6.6f1.
2. Ouvrir `Assets/Arkya/Characters/Scenes/CharacterCreation.unity` puis lancer **Play** : la vue de sélection apparaît.
3. Cliquer sur **Créer un perso**, puis choisir la taille et les couleurs de la base Valkyrie disponible. En sélection comme en création, maintenir le **clic gauche sur l’aperçu** et glisser horizontalement pour tourner le personnage. Les boutons de rotation, portrait et animation restent disponibles.
4. Utiliser **Essayer sur l’île**, disponible sans compte ni backend. Déplacer le personnage avec **ZQSD**, **WASD** ou les **flèches** : la course est le mode par défaut, maintenir **Maj** pour marcher. **Espace** saute au sol ; en nage, **Ctrl** descend et **Espace** remonte. Le **clic gauche** permet de regarder autour du personnage sans changer la direction du déplacement au clavier ; le **clic droit** réoriente aussi cette direction. La **molette** règle la distance de caméra. Le retour de l’île arrête les deux mondes réseau, décharge l’île et retrouve le brouillon de création.
5. Pour conserver un personnage depuis l’Editor, configurer les services ci-dessous, utiliser sa connexion Discord de développement et choisir un royaume disponible. Saisir un nom et lancer **Créer mon personnage**. Après confirmation du serveur, la sélection affiche le personnage enregistré ; **Jouer** demande l’entrée sur le dédié de ce royaume. Dans le joueur Windows distribué, effectuer d’abord la connexion Discord dans le launcher, puis lancer le jeu.
6. Après connexion Arkya, ouvrir **Amis Discord** depuis la création ou le jeu pour activer les amis, la présence et les invitations à un salon social. La configuration du portail et les limites figurent dans le [guide Discord Social SDK](DISCORD_SOCIAL.md). Le chat, la voix et l'admission au monde ne font pas partie de ce panneau.

Le nom contient de 3 à 20 lettres ASCII, sans espace intérieur ni accent, et doit être unique sans distinction de casse. Une erreur de nom, de session ou de service est affichée dans l'écran. Le profil en cours d'édition reste un aperçu tant que Characters n'a pas confirmé son enregistrement.

Les contrôles révisés du 23 octobre 2026 et leur validation sont suivis dans le
[registre des signalements urgents](URGENT_REPORTS_2026-10-23.md).

**Options → Interface → Thème** applique immédiatement Encre & cuivre, Carnet
d’exploration ou Pierre de mana à la création, au social et au HUD local. Le
choix est conservé sur cet appareil. Sur l’île, **Échap** ouvre les options,
**M** la carte du relief, **J** le journal et **I** le sac. Une fenêtre ouverte
bloque les entrées de déplacement et de caméra. Les barres d’actions restent
vides et les états du journal/sac reflètent les intégrations disponibles ;
voir le [périmètre de cette première intégration UI](art-direction/ui/IMPLEMENTATION.md).

## Historique — personnalisation et rendu des prototypes Humain/Elfe

La [référence personnages du 20 octobre 2026](art-direction/resources/references/personnages-stylises-2026-10-20/README.md)
fixe désormais la cible artistique : alignement très fidèle sur les proportions,
les volumes sculptés, les visages, les cheveux, l’équipement et les matières
des six images fournies par l’utilisateur. La refonte du 20 octobre remplace
la tenue et les coiffures des quatre bases : armure jade bordée d’or, drapé
ivoire, cuir brun, couronne de cristaux et grande queue segmentée. Les proportions
du visage sont reprises en conservant les squelettes et les clips existants.
La reprise suivante retravaille le visage vers des proportions adultes, les
paupières, le nez, les pommettes et la mâchoire, les mèches et les liaisons de
la queue segmentée. Les captures et les limites de cette passe figurent dans la
[revue visage, cheveux et lames](art-direction/characters-refit-2026-10-20/adult-pass/README.md).
Les deux lames turquoise suivent les mains sculptées en prise fermée. La pose
Idle ouvre légèrement les bras pour dégager la jupe ; les pieds, le regard et
la respiration conservent leur stabilité. L’or, le jade verni, les cheveux,
la peau et l’étoffe utilisent des réponses de matière distinctes.
Les vérifications fonctionnelles ne constituent pas une validation artistique
de conformité exacte à la référence.

Avant la séparation en deux vues, la création utilisait un studio vert profond, deux panneaux latéraux et une zone
centrale réservée au personnage. Sur une surface étroite, les onglets
**Apparence** et **Aventure** donnaient accès aux mêmes commandes ; en portrait,
l’aperçu passait au-dessus du panneau. Les palettes défilaient dans leur panneau.
Les commandes silhouette entière, portrait, rotation et recentrage permettent
d’examiner le modèle sans le masquer. Le cadrage tient compte de la géométrie
visible, y compris la couronne et la coiffure, et de la taille choisie.

Le premier brouillon des prototypes présentait la silhouette humaine B avec
tresse magenta et armure jade. À cette étape, **Nouveau** reprenait cette tenue
et cette coiffure dans la race choisie, à sa taille par défaut. Le retour d’exploration conserve
le choix portrait/silhouette de la création. Les apparences enregistrées et les valeurs par défaut du contrat
V1 restent inchangées. Les deux identifiants de couleur `magenta` et `jade`
sont ajoutés au manifeste et aux catalogues Unity/Characters ; le service
Characters doit recevoir cette mise à jour pour accepter leur enregistrement.

La [passe d'harmonisation du 16 octobre 2026](art-direction/alignment-2026-10-16/README.md)
améliore le détail du visage et des cheveux, les pigments peints et la distinction
des matières. Le studio utilise désormais un éclairage neutre, SMAA High et
tonemapping Neutral. Le menu **Arkya → Characters → Refresh painted art direction**
actualise les matériaux et le studio sans reconstruire les prefabs ni les contrats.

La dernière passe distingue les reflets métalliques de l’or, le vernis jade,
le tissu mat et la peau. Le shader conserve la peinture de sommets et utilise
les reflets directs et d’environnement URP ; les cheveux gardent un reflet
teinté magenta. La lumière latérale et le contre-jour rendent les plans du
visage plus lisibles. Les nouveaux rendus de revue concernent uniquement le desktop.

| Option | Choix |
| --- | --- |
| Race | Humain (`human`), Elfe (`elf`) |
| Silhouette | Masculine (`a`), féminine (`b`) |
| Taille humaine | 160 à 195 cm, défaut 175 cm |
| Taille elfique | 170 à 205 cm, défaut 185 cm |
| Coiffure | Courts (`short`), longs (`long`), tressés (`braid`) |
| Teint | 8 options |
| Cheveux | 9 couleurs |
| Yeux | 6 couleurs |
| Armure | 5 couleurs |

Les quatre bases race/silhouette utilisent un rig **Generic de 26 os**, des meshes skinnés et six animations procédurales : **Idle**, **Walk**, **Run**, **Jump**, **SwimIdle** et **Swim**. Jump est joué une seule fois ; les autres animations bouclent. Le studio propose Idle, Walk et Run ; l’exploration choisit automatiquement l’état observé sur le personnage réseau. Le contrôleur Animator désactive le root motion : l'animation représente le mouvement décidé par la simulation. Les coiffures se sélectionnent sur les éléments du modèle ; les couleurs passent par des propriétés de matériaux. Le shader `Arkya/Characters/Painted` conserve les couleurs de sommets et les variations peintes.

La taille modifie proportionnellement l'échelle du modèle présenté. Elle ne change ni la vitesse, ni les statistiques, ni une hitbox. La race et toutes les autres options restent cosmétiques dans cette tranche. Il n'y a pas de morphologie indépendante des membres ou de système d'équipement.

La finition de présentation du 19 octobre 2026 conserve ces rigs et les appuis
des clips. Les changements de direction suivent désormais une interpolation
courte du visuel, locale comme distante. Pendant un fondu marche/course, les
paramètres `WalkRate` et `RunRate` suivent chacun la vitesse réellement observée
et la taille du personnage : le clip sortant et le clip entrant représentent
ainsi le même déplacement. Le saut entre plus rapidement dans sa pose, tandis
que l'arrêt et l'entrée dans l'eau restent plus souples. La position et la
direction autoritaires restent celles du serveur.

Le menu **Arkya → Characters → Refresh locomotion playback rates** actualise
uniquement les quatre contrôleurs sans reconstruire les modèles ni les clips.
`LocomotionRate` reste disponible pour les anciens contrôleurs. Le shader peint
consomme l'occlusion ambiante du renderer lorsqu'elle est activée ; ses passes
de profondeur, normales et ombres partagent maintenant les mêmes propriétés
de matériau que la passe éclairée.

Les sources artistiques, le [manifeste d'apparence](art-direction/resources/personnages/manifest.json) et les [crédits](art-direction/resources/personnages/CREDITS.md) restent dans `docs/art-direction/resources/personnages/`. Les modèles destinés au jeu sont produits séparément sous `personnages/game/`, puis importés dans `Assets/Arkya/Characters/Models/`. Après copie des quatre FBX en conservant leurs `.meta`, le menu **Arkya → Characters → Import reference character models** actualise les associations de matières et le studio de création actif. Il conserve les prefabs, leurs GUID et les contrôleurs d’animation. L'ancien [atelier navigateur](art-direction/resources/personnages/README.md) conserve son rôle de comparaison artistique et ses préréglages propres. Les ressources Arkya demeurent séparées des packs Scinty.

## Connexion et sauvegarde serveur

Pour le développement dans l’Editor, suivre le [guide backend](../backend/README.md) pour MongoDB, les secrets des services, le certificat HTTPS local et l’application Discord. Le callback local est `https://localhost:7141/v1/auth/discord/callback`. Les API Identity et Characters doivent fonctionner pour se connecter et sauvegarder.

Dans l'Inspector de `Assets/Arkya/Characters/Settings/CharacterServices.asset`, renseigner les origines HTTPS :

| Réglage | Valeur locale par défaut |
| --- | --- |
| Identity Base URL | `https://localhost:7141` |
| Characters Base URL | `https://localhost:7142` |
| Request Timeout Seconds | 15 |

L’asset de scène conserve ces adresses de développement. Pour le build Windows
distribué, le workflow Unity fournit `ARKYA_CLIENT_IDENTITY_URL`,
`ARKYA_CLIENT_CHARACTERS_URL` et `ARKYA_CLIENT_REPORTS_URL` avec la valeur
`https://api.arkya.online`. `CiBuild.ClientEndpointOverride` les applique pendant
le build et restaure l’asset source ensuite. L’import de session launcher utilise
ces réglages intégrés au build : le message du pipe ne fournit aucune URL et
ne remplace aucune origine au démarrage du jeu.

Les URL ne contiennent ni chemin, ni identifiant, ni secret. Le client conserve la session Arkya et son expiration en mémoire ; l'activation sociale ajoute un jeton d'accès Discord et un ticket de renouvellement opaque, également en mémoire. Aucun de ces jetons n'est enregistré dans un asset ou dans PlayerPrefs. Les secrets Discord, les URI MongoDB et les clés `X-Arkya-Service-Key` restent exclusivement dans les services.

Dans le parcours Windows distribué, `LauncherIdentityClient` réalise le challenge S256 et l’échange Arkya auprès de l’origine fixe `https://api.arkya.online`. Il ouvre uniquement l’URL d’autorisation attendue sous cette origine, refuse les redirections HTTP inattendues et vérifie `GET /v1/auth/me` après la connexion puis juste avant de démarrer le jeu. Le jeton Arkya reste en mémoire ; ce parcours ne demande pas de cookie de renouvellement. La configuration du canal de téléchargement ne change pas l’origine d’authentification. Le build Unity doit intégrer les origines des services publics décrites dans le [guide du launcher](WINDOWS_LAUNCHER.md).

`LauncherSessionHandoff` remet la session une seule fois sur un pipe nommé Windows local et aléatoire, limité à l’utilisateur courant (`CurrentUserOnly`). Avant d’écrire, le launcher vérifie que le PID connecté est celui du jeu qu’il vient de démarrer. Le message JSON UTF-8 contient la version du contrat, le jeton, son expiration, le compte et son nom ; sa taille et les délais de connexion/lecture sont bornés. Seul le nom du pipe passe dans `ARKYA_LAUNCHER_PIPE`, puis Unity efface cette variable à la réception. Le jeton ne passe ni par les arguments de lancement ni par un fichier. L’usage unique concerne cette remise, pas les requêtes HTTPS ultérieures avec le bearer.

`CharacterNativeSessionBridge` reçoit ce message uniquement dans le joueur Windows, hors Editor et serveur dédié. `CharacterServiceClient.ImportLauncherSessionAsync` appelle `/v1/auth/me` sur l’origine intégrée au build, vérifie le compte et conserve la plus courte des deux expirations. La simple présence du bearer ne déverrouille pas l’interface : la sélection et la création restent masquées tant que cette vérification n’a pas réussi. Une session absente, invalide ou expirée laisse ces vues masquées ou les masque de nouveau ; le joueur doit quitter le jeu et se reconnecter dans le launcher. Une révocation distante est prise en compte lorsqu’un appel authentifié la signale.

Dans l’Editor, `CharacterServiceClient` conserve la connexion Discord directe pour le développement : vérificateur aléatoire, empreinte S256, navigateur système et interrogation de l’échange. Le protocole S256 appartient à la remise Arkya ; les jetons Discord de ce login restent dans Identity. Le parcours WebGL conserve `CharacterWebSessionBridge`, la session fournie par le site et sa revalidation dans Unity. L’autorisation Social SDK est un second flux PKCE dont Identity vérifie le compte. La déconnexion dans Unity coupe le social, demande la révocation de la session Arkya et efface ses jetons locaux ; l’expiration Arkya coupe également le social côté client.

La création envoie `name`, `appearance` et le `realmId` choisi à `POST /v1/characters` avec la session Arkya. Characters déduit le propriétaire de l'introspection Identity, impose le niveau initial et contrôle les identifiants et bornes autorisés. La réponse confirmée contient l'identifiant du personnage, son royaume et l'apparence enregistrée. La liste, la lecture individuelle et la résolution interne retournent la même apparence. L’exemple historique suivant reste accepté par le contrat ; la nouvelle création Valkyrie émet `human/b/long`.

```json
{
  "version": 1,
  "race": "elf",
  "silhouette": "b",
  "heightCm": 185,
  "skin": "warm",
  "hairStyle": "braid",
  "hairColor": "brown",
  "eyeColor": "green",
  "outfitColor": "sage"
}
```

Le catalogue serveur en lecture seule se trouve à `GET /v1/characters/appearance-options`. Les champs inconnus et les profils incomplets sont refusés. Les nouveaux documents MongoDB utilisent le schéma 2 ; les personnages historiques du schéma 1 reçoivent à la lecture le profil humain par défaut, sans changement de propriétaire ou de progression. Les contrats HTTP complets sont documentés dans le guide backend.

## Essai réseau local

`CharacterLocalPlaytest` construit un monde serveur et un monde client dédiés, connectés sur **127.0.0.1:17843**. Le client envoie sa direction, l'intention de courir et une séquence de pression pour le saut. Le serveur associe les commandes à la connexion, rejette les valeurs non finies et les séquences anciennes, normalise la direction et calcule la position dans une zone bornée. Une commande trop ancienne arrête le déplacement horizontal ; la gravité et la flottabilité restent simulées.

La marche vaut 2,2 m/s et la course 4,4 m/s. L’exploration actuelle suit le relief de l’île et ses obstacles ; l’ancienne arène de 7 m reste utilisée par les tests de locomotion isolés. Les snapshots sont transmis à 20 Hz et le client interpole le ghost avant de déplacer le visuel et de choisir l'animation. Le mode local ne possède pas de prédiction client. Il ne modifie pas les mondes du prototype d'areas déjà présents. Quitter l'essai détruit ses connexions et ses mondes.

Cet essai réservé à l’Editor sans compte ne réserve aucun personnage persistant et n’émet aucun ticket d’admission. Le joueur Windows authentifié utilise l’admission au dédié décrite plus haut. Les modifications de l’aperçu ne remplacent pas l’apparence d’un personnage enregistré : aucune route de modification après création n’est fournie. Voir le [guide jouable](world/STARTER_ISLAND_PLAYABLE.md) pour les limites de collision, les commandes et les validations historiques.

## Assets, reconstruction et build

| Dossier sous `Assets/Arkya/Characters/` | Contenu |
| --- | --- |
| `Valkyrie/` | Base active : prefab, Avatar, matériaux, textures de données et six animations |
| `CityStage/` | Meshes et matériaux propres au décor de présentation d’Aubefont |
| `Models/` | Quatre imports FBX historiques Humain/Elfe |
| `Prefabs/` | Prototypes historiques race/silhouette avec Animator |
| `Animations/` | Contrôleurs et clips des prototypes historiques Humain/Elfe |
| `Materials/`, `Shaders/` | Matériaux historiques, shaders peints et shader URP Valkyrie |
| `Settings/` | Catalogue visuel, origines des services, PanelSettings |
| `Runtime/` | Apparence, présentation, UI Toolkit, client HTTP et essai Netcode |
| `Editor/` | Génération des assets de création et build du player |
| `Scenes/` | Scène de création de personnage |

Pour la base active, quitter Play et utiliser **Arkya → Characters → Integrate Valkyrie base**. Les anciens outils de génération Humain/Elfe concernent les prototypes historiques ; ils ne produisent pas des variantes Valkyrie. Conserver les fichiers `.meta` et les retouches manuelles utiles avant toute reconstruction.

Pour reconstruire le décor de ville, ouvrir et enregistrer
`CharacterCreation.unity`, quitter Play, puis utiliser **Arkya → Characters →
Rebuild 3D city stage**. `CharacterCityStageBuilder` lit Aubefont dans une scène
de prévisualisation temporaire, copie ses composants visuels et ferme cette
scène sans la sauvegarder. Il remplace la racine `Character city stage`,
reconstruit le puits animé et les effets mystiques, actualise l’éclairage et
le post-traitement, puis enregistre la scène de création. Les meshes,
matériaux et le profil propres au décor sont écrits dans `CityStage/` ; les
assets sources de la ville et de la végétation restent référencés sans retouche.

Le point d'entrée batch pour Windows est `Arkya.Characters.Editor.CharacterCreationBuilder.BuildPlayer`. Il construit la création et l’île en mode Development. `ARKYA_CHARACTER_BUILD` définit le chemin de sortie ; la valeur par défaut est `Builds/StarterIsland/Arkya.exe`, relative au projet Unity. Le menu **Arkya → Characters → Build playable Windows** utilise ce même point d’entrée. Exemple avec un chemin d'éditeur adapté à l'installation :

```powershell
$characterUnity = '<chemin Unity 6000.6.6f1>/Editor/Unity.exe'
$characterProject = (Resolve-Path 'Unity3D/Arkya').Path
$env:ARKYA_CHARACTER_BUILD = Join-Path $characterProject 'Builds/CharacterCreation/ArkyaCharacters.exe'
& $characterUnity -batchmode -quit -projectPath $characterProject `
    -executeMethod Arkya.Characters.Editor.CharacterCreationBuilder.BuildPlayer `
    -logFile (Join-Path $characterProject 'Builds/character-build.log')
```

Ne pas lancer un second éditeur sur le même projet ouvert. Une copie isolée permet de compiler et tester sans remplacer les scènes en cours de travail.

## Vérification et limites

### Connexion par le launcher — 23 octobre 2026

Le parcours est implémenté dans le launcher et Unity. Les tests ajoutés ciblent
les origines autorisées, la validation et l’expiration des sessions, l’annulation
tardive, les bornes du message, les délais et l’annulation du pipe, ainsi que
la fermeture de l’interface avant vérification et après expiration. Ils utilisent
des services simulés et des sessions de fixture.

La connexion OAuth Discord avec un compte réel et le parcours complet
`launcher → jeu Windows construit → admission au dédié` n’ont pas été validés
de bout en bout pour cette révision. Les résultats artistiques et les essais
locaux ci-dessous ne remplacent pas cette validation ni la publication d’un
nouveau build Windows.

### Interface et ville 3D — révision actuelle du 23 octobre 2026

La variante mystique complète, reconstruite par le générateur avec le puits
animé, compile dans Unity **6000.6.6f1** et passe **14 tests ciblés sur 14**,
sans échec ni test ignoré, en **62,33 secondes**. Ils couvrent les deux vues,
les formats compacts, le cadrage silhouette/portrait et les parcours
serveur/client locaux avec retour à la ville.

Les captures réelles montrent la sélection, la création et le portrait au
crépuscule. Les trois systèmes de particules et les deux animations du puits
sont actifs. Après reconstruction, le décor compte 815 renderers et 55 groupes
de LOD ; aucun mesh ne manque et le décor ne contient aucun collider. L’éditeur
est laissé hors Play, sur la scène propre ; le ciel et le profil mystique
sont conservés, la Game View retrouve son rectangle initial et la console
finale contient **zéro erreur**. Le [rapport et ses captures](art-direction/character-city-stage-2026-10-23/README.md)
détaillent ces contrôles et conservent la première version de jour en historique.

Les tests réseau consignent des avertissements de regroupement de ticks,
sans constituer un benchmark. Les fixtures de liste restent en mémoire :
aucun compte réel modifié, aucune connexion Discord réelle ni nouveau build
autonome/Web validé dans cette passe.

### Historique — sélection et création séparées, première passe du 23 octobre 2026

Le parcours dispose désormais de deux vues, avec tests de navigation, annulation,
conservation du brouillon et présentation d’une liste de douze personnages sur
plusieurs tailles d’écran. Les parcours existants d’interface et d’exploration
ont été adaptés à l’entrée explicite dans la création.

La compilation dans Unity 6000.6.6f1 et les **13 tests ciblés passent**, sans
échec ni test ignoré. Les tests exercent les deux vues, les changements de
format, le brouillon, les options d’interface et les allers-retours vers l’île
avec ses mondes serveur/client locaux. La réinitialisation du curseur de taille
ignore désormais les notifications devenues obsolètes lors d’un changement de
personnage.

Les deux écrans ont été inspectés dans la Game View. Le
[rapport et les captures](art-direction/character-flow-2026-10-23/README.md)
documentent cette première passe, ensuite rejetée visuellement. Les personnages de démonstration sont injectés en
mémoire pour la capture de la liste ; aucune création sur un compte réel n’a
été effectuée. Cette révision n’a pas fait l’objet d’un nouveau build autonome
ou Web. Les résultats Valkyrie ci-dessous précèdent cette refonte d’interface.

### Intégration Valkyrie — 22 octobre 2026

La passe ciblée dans Unity compte **67 tests : 66 réussis, aucun échec et un
test ignoré**. Le cas
`RealServicesAdmitTwoOwnersRejectInvalidAndReplayedTicketsAndReleaseLeases`
est ignoré parce que la variable `ARKYA_WEB_BETA_TICKETS_FILE` n’est pas
fournie. L’admission avec les services réels n’est donc pas validée par cette
passe. Ce bilan concerne la sélection exécutée pour l’intégration, pas
l’ensemble des suites Unity du projet. Le [rapport et les captures](art-direction/valkyrie-integration-2026-10-22/README.md) détaillent les vérifications visuelles et les limites de cette passe.

### Résultats historiques des prototypes Humain/Elfe

Les résultats historiques ci-dessous concernent la création et la locomotion. La validation propre à l'intégration sociale et les contrôles Discord restant à réaliser sont suivis dans le [guide Discord Social SDK](DISCORD_SOCIAL.md#vérification).

La suite backend a été exécutée avec une instance MongoDB de test réelle : **201 tests réussis, aucun échec ni test ignoré**. Elle comprend la validation des options, la persistance BSON, la compatibilité des personnages historiques, les droits du propriétaire et le parcours entre Identity et Characters. Les tests Identity simulent le fournisseur Discord ; ils ne constituent pas une autorisation manuelle avec l'application Discord du projet.

Vérification Unity du 16 octobre 2026 : les scripts compilent dans Unity 6000.6.6f1 et la scène a été générée puis ouverte dans le projet actif. L'interface, le changement Humain/Elfe et les connexions locales ont été exercés en Play. Le déplacement humain a atteint la même position `(7, 0, 0)` sur le serveur et le client ; le retour à la création puis une connexion avec l'Elfe ont fonctionné, sans nouvelle erreur de console. Les captures sont dans `Unity3D/Arkya/Assets/Screenshots/Characters/`.

La suite Unity a ensuite été relancée dans une copie de validation séparée : **32 tests sur 32 réussis** ([rapport](art-direction/alignment-2026-10-16/character-tests.json)). Ce résultat porte sur l'instantané de la passe artistique, avant les ajouts sociaux effectués en parallèle. Le test d'intégration vérifie le bouton de race, la déformation des quatre rigs sur huit poses, deux connexions locales successives, le déplacement autoritaire et la convergence client/serveur. Il attend désormais les snapshots dans un délai réel borné, et contrôle la destruction des mondes avec l'itération sans allocation d'Entities. L'écran conserve la mise à jour en arrière-plan et rétablit ses abonnements après rechargement des scripts. Le build Windows n’avait pas encore été exécuté à cette étape.

La première exploration de l’île du **17 octobre 2026**, avant le saut et la nage, avait passé **101 tests Unity** et produit un build Windows dont le démarrage avait été contrôlé. Son [rapport historique](world/starter-island-playable-validation.json) reste disponible.

Pour la révision **course, saut et nage**, les tests du **17 octobre 2026** passent à **129 réussites sur 129**, sans échec ni test ignoré : 79 dans `Arkya.Characters.EditorTests` et 50 dans `Arkya.World.Environment.Tests`. Ils couvrent notamment 29 cas de terrain et de simulation serveur, 10 cas d’animation et les deux parcours réseau d’île et de rivage. Le [résultat NUnit](../artifacts/starter-island-movement/movement-final-tests.xml) conserve cette passe.

Le nouveau build **Windows 64 bits / Development** a réussi. `Unity3D/Arkya/Builds/StarterIsland/Arkya.exe` démarre avec Direct3D 12, son processus répond et son journal de démarrage ne contient aucune exception gérée. Le parcours complet dans le player autonome reste **non testé**.

Les contrôles interactifs dans la Game View, effectués dans la nuit du **17 au 18 octobre 2026**, confirment la course au clavier **W + Maj** avec une animation Run qui progresse, puis le saut avec **Espace** reçu par le serveur et le client. Le saut passe de **36,16811 m** à **37,36809 m** avant de revenir au sol ; il ne déclenche pas le bouton de retour même lorsque celui-ci a le focus. Sur le rivage réel, les deux mondes reçoivent Swim puis SwimIdle, avec les animations correspondantes et les pieds à **26,85 m** pour une eau à **28 m**. La tête reste entre **28,00935 et 28,31743 m** pendant la nage.

Le retour à terre atteint **(404,8331 ; 30,63253 ; 329,6278)**, puis Idle, sans téléportation après l’apparition initiale. Le bouton de retour du HUD décharge ensuite l’île, laisse **zéro monde réseau local** et restaure la scène active de création et le curseur libre. La console de la revue contient **zéro erreur et 20 avertissements**.

Le [rapport de cette révision](world/starter-island-movement-validation.json) conserve les résultats et les fichiers modifiés. Le [guide jouable](world/STARTER_ISLAND_PLAYABLE.md) détaille les commandes, les mesures et les limites, avec des captures de la [nage](world/starter-island-swim.png) et de la [flottaison](world/starter-island-swim-idle.png). Les sources runtime n’ont pas changé depuis le build validé ; une modification de test UI parallèle a déclenché un rechargement de domaine pendant la revue, puis l’île a été rejointe de nouveau par son interface.

### Révision de la posture au repos — 18 octobre 2026

Les quatre rigs utilisent désormais un clip **Idle natif Unity de 8 secondes**, qui remplace uniquement le clip Idle importé du FBX dans leur contrôleur. Les pieds restent plantés et légèrement décalés, avec une jambe d’appui plus droite ; les bras sont détendus, la tête stable et la respiration discrète. Les autres états d’animation restent inchangés. Le menu **Arkya → Characters → Rebuild relaxed idle animations** régénère ces clips et leur association, sans réglage manuel dans l’Inspector.

La vérification ciblée passe **26 tests sur 26** : 8 nouveaux cas de repos contrôlent les pieds, la racine, la stabilité du regard, la posture et la fermeture de boucle par échantillonnage à **60 Hz** ; 10 cas vérifient les transitions de locomotion et 8 les prefabs. Cette passe complète le bilan historique des 129 tests ci-dessus ; elle ne constitue pas une nouvelle exécution de toute cette suite.

Le contrôle sur l’île est **réussi** : après une course, le client et le serveur reviennent à l’arrêt, puis l’Animator déroule plus d’une boucle Idle sans déplacement parasite de la racine. Le retour au studio libère les deux mondes locaux. Le nouveau build Windows a réussi et son démarrage répond sans exception applicative ; le parcours autonome complet n’a pas été rejoué. L’éditeur est laissé hors Play, sur la création enregistrée, avec zéro erreur dans la console. Le [rapport de repos](world/character-idle-validation.json) conserve ces résultats et les fichiers modifiés ; la [capture de la posture](world/character-idle.png) montre la revue de face et de profil.

Les animations couvrent seulement la locomotion ; combat, expressions, IK des pieds sur le terrain et transitions d'équipement restent à réaliser. L’IK du générateur Idle sert à préparer ses poses ; il ne recale pas les pieds sur le relief pendant le jeu. Le rig Generic ne fournit pas un avatar Humanoid prêt au retargeting. Les LOD et l'optimisation pour de grandes foules ne sont pas finalisés. La sélection d'un realm, l'admission durable, les connexions simultanées d'un même personnage et le chargement de son apparence sur le serveur dédié restent des étapes distinctes.

### Révision des cycles du corps — 18 octobre 2026

La marche et la course utilisent désormais huit clips natifs Unity produits par `CharacterLocomotionAnimationBuilder`. Les anciens takes FBX produisaient des appuis trop courts pour les vitesses de jeu et des bras projetés vers l'avant. Les nouveaux cycles opposent bras et jambes, gardent les genoux vers l'avant et déroulent les semelles réelles, y compris les bottes arrondies des silhouettes B. La marche dure **0,72 s** avec **54 % d'appui par pied** ; la course dure **0,60 s** avec **30 % d'appui** et deux phases de suspension. Le bassin et le regard restent sobres. La flottaison a également été corrigée pour supprimer la flexion inversée des genoux.

Le paramètre Animator `LocomotionRate` concerne uniquement Walk et Run. Le client l'ajuste à la vitesse planaire reçue et à l'échelle visuelle, avec un lissage de 0,1 s ; les références restent **2,2 et 4,4 m/s**. Le passage marche–course conserve la phase du pas. La simulation conserve la position, sans root motion ni nouveau champ réseau. Le menu **Arkya → Characters → Rebuild planted locomotion animations** régénère les clips ; le [rapport des cycles](world/character-locomotion-validation.json) conserve la validation de cette révision.

Ces clips restent des cycles de prototype sur sol plat. Le recalage des pieds sur un terrain irrégulier et les poses de pivot ne sont pas ajoutés. Le rig actuel ne possède pas d'articulations des doigts. La passe du 20 octobre sculpte une prise fermée dans la géométrie des mains et ajoute deux lames en croissant liées à `LeftHand` et `RightHand`, dans `CharacterMesh`. Cette tenue visuelle fixe n’ajoute ni inventaire d’armes, ni dégâts, ni animations de combat.

La passe ciblée compte **66 tests réussis**, sans échec ni cas ignoré : 16 cas de géométrie et d'appuis, 18 de cadence et de transitions, 14 de locomotion et de flottaison, 8 de repos, 8 de prefabs et 2 parcours réseau. Les appuis sont contrôlés à **60 et 120 Hz**, avec un budget de **2 mm**, aux tailles minimale, par défaut et maximale de chaque race. La revue de face et de profil porte sur huit poses par cycle des quatre rigs. Un parcours supplémentaire dans l'île confirme **marche → course → marche → arrêt** sur environ **19 m**, une racine stable à l'arrêt et le nettoyage des deux mondes au retour par le HUD. Le build Windows a réussi et son démarrage répond avec Direct3D 12, sans exception applicative détectée. Le parcours complet dans le player autonome reste non rejoué ; les parcours connectés ci-dessus ont été contrôlés en Editor.

### Mobilité du buste et de la tête — 20 octobre 2026

Les huit clips Walk/Run donnent désormais une torsion visible aux épaules, un léger balancement du buste et une réponse retardée de la tête aux pas. Le cou amortit partiellement la rotation du torse ; la tête n'est plus verrouillée dans son orientation initiale. La course amplifie ces mouvements. Ces courbes sont préparées dans `CharacterLocomotionAnimationBuilder`, sans calcul supplémentaire de pose au runtime ni donnée réseau ajoutée.

Sur les quatre rigs, l'amplitude angulaire totale du torse passe d'environ **1,8° à 8,6° à la marche**, et de **1,9° à 14,9° à la course**. Celle de la tête passe de **0° à 4,0° et 6,9°** respectivement. Mesures en espace racine à 120 Hz : [avant](world/character-upper-body/baseline/capture-report.json), [après](world/character-upper-body/after/capture-report.json). Les planches associées montrent huit phases par cycle, marche en haut et course en bas.

Unity `6000.6.6f1` compile sans erreur. Les **37 tests Edit Mode de locomotion passent**, dont les appuis à 60/120 Hz, la continuité des boucles, les cadences et les transitions : [résultats](world/character-upper-body/test-results.json). Les nouveaux seuils de mobilité échouaient sur les huit anciens clips avant régénération. La revue visuelle utilise les prefabs et clips sauvegardés dans un studio temporaire ; elle préserve les scènes ouvertes. Cette passe n'inclut pas un nouveau build ni un parcours manuel en Play Mode.

Pour régénérer, utiliser **Arkya → Characters → Rebuild planted locomotion animations**. Le script `AgentScripts/ArtDirection/ReviewCharacterUpperBody.cs`, exécuté via Unity CLI avec l'entrée `ReviewCharacterUpperBody.Main` et un libellé en argument, reproduit les mesures et les planches.
