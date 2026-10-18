# Création de personnage dans Unity

La scène `Assets/Arkya/Characters/Scenes/CharacterCreation.unity`, dans le projet actif `Unity3D/Arkya/` avec Unity **6000.6.6f1**, fournit l'écran de création et de sélection en jeu. L'interface UI Toolkit personnalise un personnage animé, ouvre la connexion Discord dans le navigateur système et enregistre les choix auprès de Characters. Le bouton **Explorer l’île** ouvre désormais la [première exploration jouable de l’Île de l’Aube](world/STARTER_ISLAND_PLAYABLE.md), avec un serveur Netcode local.

## Ouvrir et essayer

1. Ouvrir `Unity3D/Arkya/` dans Unity 6000.6.6f1.
2. Ouvrir `Assets/Arkya/Characters/Scenes/CharacterCreation.unity` puis lancer **Play**.
3. Choisir la race, la silhouette, la taille, la coiffure et les couleurs. Les boutons de rotation, portrait et animation permettent d'examiner le résultat.
4. Utiliser **Explorer l’île**, disponible sans compte ni backend. Déplacer le personnage avec **ZQSD**, **WASD** ou les **flèches** ; maintenir **Maj** pour courir, le **clic droit** pour orienter la caméra et la **molette** pour zoomer. Le retour à la création arrête les deux mondes réseau et décharge l’île, en conservant l’apparence.
5. Pour conserver un personnage sur son compte, configurer les services ci-dessous, se connecter avec Discord, saisir un nom et lancer la création. La liste du compte permet ensuite de retrouver son apparence enregistrée.
6. Après connexion Arkya, ouvrir **Amis Discord** depuis la création ou l'essai local pour activer les amis, la présence et les invitations à un salon social. La configuration du portail et les limites figurent dans le [guide Discord Social SDK](DISCORD_SOCIAL.md). Le chat, la voix et l'admission au monde ne font pas partie de ce panneau.

Le nom contient de 3 à 20 lettres ASCII, sans espace intérieur ni accent, et doit être unique sans distinction de casse. Une erreur de nom, de session ou de service est affichée dans l'écran. Le profil en cours d'édition reste un aperçu tant que Characters n'a pas confirmé son enregistrement.

**Options → Interface → Thème** applique immédiatement Encre & cuivre, Carnet
d’exploration ou Pierre de mana à la création, au social et au HUD local. Le
choix est conservé sur cet appareil. Sur l’île, **Échap** ouvre les options,
**M** la carte du relief, **J** le journal et **I** le sac. Une fenêtre ouverte
bloque les entrées de déplacement et de caméra. Les barres d’actions restent
vides et les états du journal/sac reflètent les intégrations disponibles ;
voir le [périmètre de cette première intégration UI](art-direction/ui/IMPLEMENTATION.md).

## Personnalisation et rendu

La [passe d'harmonisation du 16 octobre 2026](art-direction/alignment-2026-10-16/README.md)
améliore le détail du visage et des cheveux, les pigments peints et la distinction
des matières. Le studio utilise désormais un éclairage neutre, SMAA High et
tonemapping Neutral. Le menu **Arkya → Characters → Refresh painted art direction**
actualise les matériaux et le studio sans reconstruire les prefabs ni les contrats.

| Option | Choix |
| --- | --- |
| Race | Humain (`human`), Elfe (`elf`) |
| Silhouette | Masculine (`a`), féminine (`b`) |
| Taille humaine | 160 à 195 cm, défaut 175 cm |
| Taille elfique | 170 à 205 cm, défaut 185 cm |
| Coiffure | Courts (`short`), longs (`long`), tressés (`braid`) |
| Teint | 8 options |
| Cheveux | 8 couleurs |
| Yeux | 6 couleurs |
| Tissu | 4 couleurs |

Les quatre bases race/silhouette utilisent un rig **Generic de 26 os**, des meshes skinnés et six animations procédurales : **Idle**, **Walk**, **Run**, **Jump**, **SwimIdle** et **Swim**. Jump est joué une seule fois ; les autres animations bouclent. Le studio propose Idle, Walk et Run ; l’exploration choisit automatiquement l’état observé sur le personnage réseau. Le contrôleur Animator désactive le root motion : l'animation représente le mouvement décidé par la simulation. Les coiffures se sélectionnent sur les éléments du modèle ; les couleurs passent par des propriétés de matériaux. Le shader `Arkya/Characters/Painted` conserve les couleurs de sommets et les variations peintes.

La taille modifie proportionnellement l'échelle du modèle présenté. Elle ne change ni la vitesse, ni les statistiques, ni une hitbox. La race et toutes les autres options restent cosmétiques dans cette tranche. Il n'y a pas de morphologie indépendante des membres ou de système d'équipement.

Les sources artistiques, le [manifeste d'apparence](art-direction/resources/personnages/manifest.json) et les [crédits](art-direction/resources/personnages/CREDITS.md) restent dans `docs/art-direction/resources/personnages/`. Les modèles destinés au jeu sont produits séparément sous `personnages/game/`, puis importés dans `Assets/Arkya/Characters/Models/`. L'ancien [atelier navigateur](art-direction/resources/personnages/README.md) conserve son rôle de comparaison artistique et ses préréglages propres. Les ressources Arkya demeurent séparées des packs Scinty.

## Connexion et sauvegarde serveur

Suivre d'abord le [guide backend](../backend/README.md) pour MongoDB, les secrets des services, le certificat HTTPS local et l'application Discord. Le callback Discord reste `https://localhost:7141/v1/auth/discord/callback`. Les API Identity et Characters doivent fonctionner pour se connecter et sauvegarder.

Dans l'Inspector de `Assets/Arkya/Characters/Settings/CharacterServices.asset`, renseigner les origines HTTPS :

| Réglage | Valeur locale par défaut |
| --- | --- |
| Identity Base URL | `https://localhost:7141` |
| Characters Base URL | `https://localhost:7142` |
| Request Timeout Seconds | 15 |

Les URL ne contiennent ni chemin, ni identifiant, ni secret. Le client conserve la session Arkya et son expiration en mémoire ; l'activation sociale ajoute un jeton d'accès Discord et un ticket de renouvellement opaque, également en mémoire. Aucun de ces jetons n'est enregistré dans un asset ou dans PlayerPrefs. Les secrets Discord, les URI MongoDB et les clés `X-Arkya-Service-Key` restent exclusivement dans les services.

`CharacterServiceClient` génère un vérificateur aléatoire et envoie son empreinte S256 à Identity. Il ouvre l'URL d'autorisation validée dans le navigateur système, puis interroge l'échange en respectant l'intervalle d'attente. Le protocole S256 est celui de remise Arkya entre client natif et backend ; les jetons Discord de ce login restent dans Identity. L'autorisation Social SDK est un second flux PKCE dont Identity vérifie le compte. Le bouton de déconnexion coupe le social, demande la révocation de la session Arkya et efface ses jetons locaux. L'expiration Arkya coupe également le social côté client.

La création envoie `name` et `appearance` à `POST /v1/characters` avec la session Arkya. Characters déduit le propriétaire de l'introspection Identity, impose le niveau initial et contrôle les identifiants et bornes autorisés. La réponse confirmée contient l'identifiant du personnage et l'apparence enregistrée. La liste, la lecture individuelle et la résolution interne retournent la même apparence.

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

Cet essai ne réserve aucun personnage persistant et n'émet aucun ticket d'admission. Un profil créé dans MongoDB peut être relu puis utilisé visuellement sur l’île locale ; son admission dans un realm ou un serveur dédié reste à intégrer. Les modifications de l'aperçu ne remplacent pas l'apparence d'un personnage enregistré : aucune route de modification après création n'est fournie. Voir le [guide jouable](world/STARTER_ISLAND_PLAYABLE.md) pour les limites de collision, les commandes et la validation actuelle.

## Assets, reconstruction et build

| Dossier sous `Assets/Arkya/Characters/` | Contenu |
| --- | --- |
| `Models/` | Quatre imports FBX skinnés, rig et clips |
| `Prefabs/` | Variantes race/silhouette avec Animator |
| `Animations/` | Contrôleurs Idle/Walk/Run/Jump/SwimIdle/Swim et clips de traversée |
| `Materials/`, `Shaders/` | Matériaux peints et rendu URP |
| `Settings/` | Catalogue visuel, origines des services, PanelSettings |
| `Runtime/` | Apparence, présentation, UI Toolkit, client HTTP et essai Netcode |
| `Editor/` | Génération des assets de création et build du player |
| `Scenes/` | Scène de création de personnage |

Après une modification des sources FBX, quitter Play et utiliser **Arkya → Characters → Rebuild creation assets**. Cette commande régénère les imports, contrôleurs, prefabs, catalogue et scène de création. Conserver leurs fichiers `.meta` et préserver les retouches manuelles utiles avant de reconstruire.

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

Ces clips restent des cycles de prototype sur sol plat. Le recalage des pieds sur un terrain irrégulier et les poses de pivot ne sont pas ajoutés. Les mains conservent leur forme ouverte : le rig actuel ne possède pas d'articulations des doigts.

La passe ciblée compte **66 tests réussis**, sans échec ni cas ignoré : 16 cas de géométrie et d'appuis, 18 de cadence et de transitions, 14 de locomotion et de flottaison, 8 de repos, 8 de prefabs et 2 parcours réseau. Les appuis sont contrôlés à **60 et 120 Hz**, avec un budget de **2 mm**, aux tailles minimale, par défaut et maximale de chaque race. La revue de face et de profil porte sur huit poses par cycle des quatre rigs. Un parcours supplémentaire dans l'île confirme **marche → course → marche → arrêt** sur environ **19 m**, une racine stable à l'arrêt et le nettoyage des deux mondes au retour par le HUD. Le build Windows a réussi et son démarrage répond avec Direct3D 12, sans exception applicative détectée. Le parcours complet dans le player autonome reste non rejoué ; les parcours connectés ci-dessus ont été contrôlés en Editor.
