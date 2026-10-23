# Première version jouable — Île de l’Aube

La scène de création permet désormais d’explorer l’île de départ avec le personnage affiché : Humain ou Elfe, silhouette, taille, coiffure et couleurs conservées. L’arrivée se fait à l’Anse de l’Éveil ; le déplacement comprend marche, course, saut et nage, avec une caméra à la troisième personne. Le retour à la création permet de modifier l’apparence puis de repartir. La validation de la révision ajoutant le saut et la nage est suivie séparément ci-dessous.

Cette version lance un serveur et un client Netcode dans le même jeu. Le serveur possède le déplacement ; l’exploration ne dépend pas d’un compte ou des services backend. La connexion Discord et l’enregistrement d’un personnage restent disponibles par le parcours Identity/Characters existant.

## Lancer l’exploration

1. Ouvrir le projet actif `Unity3D/Arkya/` dans Unity **6000.6.6f1**.
2. Ouvrir uniquement `Assets/Arkya/Characters/Scenes/CharacterCreation.unity`, puis lancer **Play**.
3. Personnaliser le personnage. Pour retrouver une apparence enregistrée, se connecter avec Discord et choisir un personnage dans la liste du compte ; pour enregistrer un nouveau personnage, suivre le [guide de création](../CHARACTER_CREATION.md).
4. Cliquer sur **Explorer l’île**. Ce bouton fonctionne aussi hors connexion, avec l’apparence en cours d’édition. Attendre le chargement de l’île et la connexion locale.
5. Suivre le chemin depuis l’arrivée vers le refuge, puis explorer les routes et les secteurs de l’île.
6. Utiliser le bouton de retour à la création dans le HUD pour quitter l’île. L’apparence et la session de compte restent en mémoire ; une nouvelle exploration repart au point d’arrivée.

L’île est chargée de façon additive ; le décor et la caméra du studio sont masqués pendant l’exploration. Le retour décharge l’île, libère les mondes client/serveur et restaure le studio. Il faut commencer depuis la scène de création seule : une île déjà ouverte est signalée comme telle pour éviter deux copies concurrentes.

## Commandes

| Action | Commande |
| --- | --- |
| Marcher | **ZQSD**, **WASD** ou **flèches** |
| Courir | Maintenir **Maj** gauche ou droite pendant le déplacement |
| Sauter | Appuyer sur **Espace** au sol ou depuis la surface de l’eau ; relâcher avant le prochain saut |
| Nager | Entrer dans l’eau profonde puis utiliser **ZQSD**, **WASD** ou les **flèches** |
| Orienter la caméra | Maintenir le **bouton droit** de la souris et déplacer la souris |
| Régler la distance de caméra | **Molette** |
| Libérer le curseur pendant la rotation | Relâcher le bouton droit |
| Ouvrir les options ou fermer la fenêtre d’exploration ouverte | **Échap** |
| Ouvrir ou fermer la carte | **M** |
| Ouvrir ou fermer le journal | **J** |
| Ouvrir ou fermer le sac | **I** |
| Revenir au studio | Bouton de retour à la création dans le HUD |
| Ouvrir le panneau social existant | **Amis Discord** |

Le déplacement suit l’orientation horizontale de la caméra. La marche vaut **2,2 m/s**, la course **4,4 m/s** et la nage **2,2 m/s**, quelle que soit l’apparence. Maj n’accélère pas la nage. Le saut répond à un nouvel appui : maintenir Espace ne relance pas automatiquement un saut à l’atterrissage. En nage, Espace permet aussi une impulsion depuis la surface ; il n’y a pas de commande de plongée. La caméra ajuste sa distance face aux obstacles et reste au-dessus du terrain. La perte de focus ou l’ouverture des options, d’une fenêtre d’exploration ou du panneau social suspend les entrées de déplacement et libère le curseur.

Échap ferme d’abord la carte, le journal ou le sac lorsqu’une de ces fenêtres est ouverte ; sinon, il ouvre ou ferme les options. La carte affiche le relief et la position du personnage. Le journal et le sac signalent leurs fonctions encore indisponibles : ils n’ajoutent pas de quêtes ni d’inventaire à cette exploration.

## Terrain et obstacles

Le serveur lit une copie immuable des hauteurs du Terrain, construite lors de l’entrée sur l’île. Le personnage apparaît à la position d’arrivée de `StarterIslandDefinition`, avec les pieds recalés sur le sol. Au sol, la position verticale suit le relief ; pendant le saut, le serveur applique l’impulsion et la gravité jusqu’à l’atterrissage. La limite de sept mètres de l’ancienne arène ne s’applique pas à l’île.

Le déplacement refuse les sorties du Terrain et les montées trop raides au sol, avec un seuil de pente de **40°**. L’eau peu profonde permet de marcher sur le fond. Au-delà d’environ **1,1 m de profondeur**, le personnage passe en nage : ses pieds restent à **1,15 m sous le niveau marin**, avec une animation de nage en mouvement et de flottaison à l’arrêt. Rejoindre un rivage peu pentu permet de reprendre la marche. La règle de mort de l’[eau violette](STARTER_ISLAND_BOUNDARY_WATER.md) reste une fonctionnalité distincte ; les dégâts, la noyade et la mort par exposition ne sont pas raccordés à cet essai local.

Les collisions de déplacement sont testées côté serveur avec une capsule de **0,30 m de rayon**, dans un gabarit de **1,75 m** indépendant de la taille visuelle choisie. Elles utilisent les colliders statiques de la scène et des volumes simples ajoutés aux décors identifiés sans collision : bas des troncs, rochers, souches, rondins, murs, poteaux et barrières. Les portes conservent leur ouverture grâce à un collider de mesh ; les branches, feuillages et canopées restent traversables. Ces volumes restent des approximations : certains passages peuvent être plus étroits que leur apparence et certains détails décoratifs ne sont pas reproduits exactement.

L’atterrissage utilise le Terrain et les surfaces statiques déjà munies d’un collider. La révision des [remparts d’Aubefont](STARTER_ISLAND_TOWN.md) permet également d’acquérir cet appui en marchant depuis le Terrain et de suivre une rampe ou un plancher, jusqu’à **40°**, avec contrôle du gabarit complet et du plafond. Les petits raccords de palier restent limités à **0,22 m** ; ce mécanisme ne permet pas de franchir un mur. Un appui sur un rocher ou une plateforme peut soutenir le personnage et permettre un nouveau saut ; quitter le bord déclenche une chute. Les planchers, ponts et toits sans collider restent traversables : cette révision n’ajoute pas de collisions à tous les éléments de construction. Les obstacles dynamiques, la plongée et une navigation de production demandent une étape dédiée.

## Autorité et persistance

`CharacterLocalPlaytest` crée un monde serveur et un monde client isolés sur **127.0.0.1:17843**. La simulation tourne à **30 Hz**, avec des snapshots à **20 Hz**. Le client transmet une intention de direction, de course et de saut ; le serveur valide la connexion et les commandes, puis calcule la position, la gravité, les transitions avec l’eau et les collisions. Le rendu observe un ghost interpolé et son état de locomotion pour choisir les animations Idle, Walk, Run, Jump, SwimIdle ou Swim. Il n’y a pas encore de prédiction locale.

Le serveur ECS est propriétaire de la position. Les MonoBehaviours chargent la scène, présentent le modèle, recueillent les entrées et orientent la caméra. La géométrie statique des collisions provient de la scène Physics commune au processus local ; cette intégration ne constitue pas encore le chargement de collisions d’un serveur dédié indépendant.

Un personnage enregistré peut être sélectionné pour reprendre son apparence dans cet essai. L’entrée locale ne réserve pas son identité persistante et ne valide pas un droit d’admission à un realm. Les modifications de l’aperçu ne réécrivent pas un personnage déjà enregistré. Seule la création confirmée par Characters conserve l’apparence dans MongoDB, selon les règles existantes.

La position, les déplacements et la progression ne sont pas sauvegardés. Aucun combat, objectif de quête, ennemi, récolte ou inventaire n’est intégré à ce parcours. Il n’y a pas de session partagée entre joueurs, de choix de realm ou de connexion à un serveur dédié dans cette version. Les invitations Discord restent des invitations sociales ; elles ne réunissent pas les joueurs sur une même île.

## Configuration et build Windows

Le menu **Arkya → Characters → Configure playable starter island** associe `StarterIslandDefinition` à la scène de création et place les scènes de création et d’île dans les Build Settings. Il conserve le contenu existant de la scène. Cette commande s’exécute hors Play Mode et demande d’enregistrer la scène de création si elle est déjà ouverte avec des modifications.

Le menu **Arkya → Characters → Build playable Windows** appelle cette configuration, puis construit un player **Windows 64 bits / Development** contenant les deux scènes. La fenêtre démarre en **1600 × 900**, avec redimensionnement autorisé. La sortie par défaut, relative au projet Unity, est :

```text
Builds/StarterIsland/Arkya.exe
```

La méthode batch est `Arkya.Characters.Editor.CharacterCreationBuilder.BuildPlayer`. La variable d’environnement **`ARKYA_CHARACTER_BUILD`** remplace le chemin de sortie. Le build copie également les crédits des personnages à côté du player. Les sorties de build restent locales et ne sont pas des assets Unity à versionner.

Le premier build peut prendre plusieurs dizaines de minutes pour compiler les variantes DirectX 11 et DirectX 12 des shaders déjà présents dans le projet. Unity conserve ce cache pour les builds suivants.

Si la connexion locale échoue, vérifier qu’aucun autre essai n’occupe le port **17843**, puis revenir à la création et réessayer. Une indisponibilité d’Identity ou de Characters empêche leur parcours de connexion/enregistrement ; l’exploration hors compte reste accessible.

## Vérification

Révision **course, saut et nage**, avec Unity **6000.6.6f1** : tests exécutés le **17 octobre 2026**, puis contrôles interactifs dans la nuit du **17 au 18 octobre 2026**.

- Compilation Editor et exécution des suites : **réussies**. Le [résultat NUnit](../../artifacts/starter-island-movement/movement-final-tests.xml) contient **129 tests réussis, aucun échec ni test ignoré** : 79 dans `Arkya.Characters.EditorTests` et 50 dans `Arkya.World.Environment.Tests`.
- Terrain et autorité serveur : **29 tests réussis** — 16 tests de traversée et 13 tests serveur, comprenant le saut, la nage, les transitions de rivage et les appuis sur des colliders statiques.
- Animations : **10 tests réussis** pour les transitions et poses de locomotion, dont la correction de la transition de course et les états Jump, SwimIdle et Swim.
- Intégration client/serveur : les **deux tests** de `CharacterIslandRuntimeTests` passent. Le parcours réel entre deux fois sur l’île, suit environ 28 m de la route d’arrivée, contrôle le relief, la convergence réseau, l’apparence et la restauration complète du studio. Le nouveau parcours teste le saut et la traversée d’un rivage avec un client réellement connecté.
- Build **Windows 64 bits / Development** : **réussi**, confirmé dans le [journal du build](../../artifacts/starter-island-movement/build.log). Le nouveau player `Unity3D/Arkya/Builds/StarterIsland/Arkya.exe` démarre avec Direct3D 12, son processus répond et son journal de démarrage ne contient aucune exception gérée. Le parcours complet dans cet exécutable autonome reste **non testé** ; les parcours ci-dessous ont été exercés dans la Game View de l’éditeur.
- Course au clavier **W + Maj** : état Run sur le serveur et animation Run observée, avec un temps normalisé progressant de **0,269 à 0,872**. La transition ne maintient plus l’animation au début du cycle.
- Saut avec **Espace** : état Jump reçu par le serveur et le client, avec animation Jump. La hauteur passe de **36,16811 m** à **37,36809 m**, puis revient à **36,16811 m** à l’atterrissage. Le bouton de retour avait le focus ; l’appui sur Espace a bien déclenché le saut en conservant l’exploration de l’île.
- Nage sur le rivage réel : le personnage a atteint **(391,9466 ; 26,85 ; 300,7197)**. Le serveur et le client ont observé Swim et SwimIdle, avec les animations correspondantes. Les pieds flottent à **26,85 m** pour une eau à **28 m** ; pendant la nage, la tête reste entre **28,00935 m et 28,31743 m**. À l’arrêt, elle a été mesurée à **28,313 m**. Les captures ci-dessous montrent la nage et la flottaison.
- Retour au rivage sec : **réussi**, jusqu’à **(404,8331 ; 30,63253 ; 329,6278)**, puis état Idle. Aucun acteur n’a été téléporté après son apparition initiale.
- Retour par le HUD et nettoyage : **réussis**. L’île est déchargée, il reste **zéro monde réseau local**, la création redevient la scène active sans modification non enregistrée et le curseur est libéré. La console de cette revue contient **zéro erreur et 20 avertissements**.

Le test réseau `ConnectedCharacterJumpsLandsWadesSwimsAndReturnsToDryGround` utilise un Terrain de test de 32 × 32 m : plateau sec, pente douce de rivage, puis fond immergé. Il vérifie l’impulsion de saut reçue par le serveur et le client, l’atterrissage sans répétition quand la touche reste enfoncée, le passage à gué puis à la nage, la flottaison à l’arrêt et le retour sur la terre ferme. Aucun acteur n’est téléporté après l’apparition initiale.

Le [rapport de validation de la révision de locomotion](starter-island-movement-validation.json) conserve les tests, les mesures interactives et les fichiers modifiés. Les tests utilisent le réglage réel de rechargement du domaine du projet et vérifient explicitement qu’ils s’exécutent en Play Mode. Un rechargement de domaine provoqué par une modification de test UI parallèle a interrompu la revue interactive au passage à minuit ; l’exploration a ensuite été relancée depuis son interface. Les sources runtime n’ont pas changé depuis le build validé.

La version initiale du **17 octobre 2026**, avant les ajouts de saut et de nage, avait passé **101 tests**, produit un build Windows et fait l’objet d’un contrôle de déplacement, caméra et retour au studio. Son [rapport historique](starter-island-playable-validation.json) conserve ces résultats. Des avertissements des packages et de certaines variantes de shaders subsistent pendant le build. Le parcours OAuth réel, l’enregistrement backend et un serveur dédié séparé n’ont pas été retestés dans cette révision.

![Nage du personnage sur le rivage de l’île](starter-island-swim.png)

![Flottaison du personnage à l’arrêt](starter-island-swim-idle.png)

### Révision de la posture au repos — 18 octobre 2026

Un clip **Idle natif Unity de 8 secondes** remplace uniquement l’Idle importé du FBX pour chacun des quatre rigs. Les pieds restent plantés et légèrement décalés, avec une jambe d’appui plus droite, des bras détendus, une tête stable et une respiration discrète. Le menu **Arkya → Characters → Rebuild relaxed idle animations** régénère les clips et leur association aux contrôleurs, sans modification manuelle dans l’Inspector.

La passe ciblée compte **26 tests réussis** : 8 nouveaux cas Idle vérifient à **60 Hz** les pieds, la racine, le regard, la posture et la fermeture de boucle ; 10 cas portent sur les transitions de locomotion et 8 sur les prefabs. Le bilan des 129 tests de locomotion présenté plus haut reste celui de la révision précédente.

Le contrôle dans l’île est **réussi** : course, retour à Idle côté client et serveur, puis plus d’une boucle complète sans dérive de la racine. Le retour par le HUD libère les deux mondes locaux. Le build Windows est à jour et son démarrage a été vérifié sans exception applicative ; le parcours complet dans l’exécutable autonome n’a pas été rejoué. L’éditeur est laissé hors Play sur la création enregistrée, avec zéro erreur dans la console. Le [rapport de repos](character-idle-validation.json) et la [capture de la posture](character-idle.png) conservent cette validation.

La description générale des secteurs et des routes reste dans [Île de départ](STARTER_ISLAND.md). Le [guide d’environnement](STARTER_ISLAND_ENVIRONMENT.md) documente le ciel et les ambiances ; sa caméra libre de revue est désactivée pendant l’exploration avec le personnage.

### Révision des cycles du corps — 18 octobre 2026

La marche et la course utilisent désormais huit clips natifs Unity produits par `CharacterLocomotionAnimationBuilder`. Les anciens takes FBX produisaient des appuis trop courts pour les vitesses de jeu et des bras projetés vers l'avant. Les nouveaux cycles opposent bras et jambes, gardent les genoux vers l'avant et déroulent les semelles réelles, y compris les bottes arrondies des silhouettes B. La marche dure **0,72 s** avec **54 % d'appui par pied** ; la course dure **0,60 s** avec **30 % d'appui** et deux phases de suspension. Le bassin et le regard restent sobres. La flottaison a également été corrigée pour supprimer la flexion inversée des genoux.

Le paramètre Animator `LocomotionRate` concerne uniquement Walk et Run. Le client l'ajuste à la vitesse planaire reçue et à l'échelle visuelle, avec un lissage de 0,1 s ; les références restent **2,2 et 4,4 m/s**. Le passage marche–course conserve la phase du pas. La simulation conserve la position, sans root motion ni nouveau champ réseau. Le menu **Arkya → Characters → Rebuild planted locomotion animations** régénère les clips ; le [rapport des cycles](character-locomotion-validation.json) conserve la validation de cette révision.

Ces clips restent des cycles de prototype sur sol plat. Le recalage des pieds sur un terrain irrégulier et les poses de pivot ne sont pas ajoutés. Les mains conservent leur forme ouverte : le rig actuel ne possède pas d'articulations des doigts.

La passe ciblée compte **66 tests réussis**, sans échec ni cas ignoré : 16 cas de géométrie et d'appuis, 18 de cadence et de transitions, 14 de locomotion et de flottaison, 8 de repos, 8 de prefabs et 2 parcours réseau. Les appuis sont contrôlés à **60 et 120 Hz**, avec un budget de **2 mm**, aux tailles minimale, par défaut et maximale de chaque race. La revue de face et de profil porte sur huit poses par cycle des quatre rigs. Un parcours supplémentaire dans l'île confirme **marche → course → marche → arrêt** sur environ **19 m**, une racine stable à l'arrêt et le nettoyage des deux mondes au retour par le HUD. Le build Windows a réussi et son démarrage répond avec Direct3D 12, sans exception applicative détectée. Le parcours complet dans le player autonome reste non rejoué ; les parcours connectés ci-dessus ont été contrôlés en Editor.
