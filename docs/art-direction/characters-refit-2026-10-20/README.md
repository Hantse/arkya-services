# Refonte des personnages et de leur création — 20 octobre 2026

La [reprise visage adulte, cheveux, matières et deux lames](adult-pass/README.md)
succède à cette première livraison. Les résultats ci-dessous restent ceux de
la passe précédente, jugée trop enfantine par l’utilisateur.

La demande porte sur les personnages en jeu et leur écran de création, avec
les [six images fournies](../resources/references/personnages-stylises-2026-10-20/README.md)
comme référence principale. Cette passe modifie les quatre modèles du projet
Unity actif, leurs matières, le studio et l’interface.

## Livraison

- Quatre sources Blender/FBX, costume jade/or/ivoire, coiffures sculptées,
  casque à cristaux, visage et proportions repris. Les modèles et leurs
  [budgets réels](../resources/personnages/game/README.md) sont documentés.
- Interface UI Toolkit avec panneaux latéraux, palettes défilantes, aperçu
  réservé au centre, portrait, rotation, recentrage et commandes d’animation.
  Les surfaces étroites utilisent deux onglets ; le format portrait place
  l’aperçu au-dessus des contrôles.
- Cadrage calculé depuis la géométrie visible, avec marge pour la coiffure et
  les rotations. Le sol du terrain d’essai est masqué pendant la création et
  restauré lors de l’exploration.
- Matières différenciées pour métal, émail, tissu, cuir, peau et cristaux.
  Ajout de `magenta` et `jade` aux catalogues Unity, Characters et au manifeste.
  Les anciennes apparences et le contrat V1 restent compatibles.

Le brouillon initial présente une humaine B avec tresse magenta et armure jade.
Le bouton **Nouveau** reprend cette palette et la silhouette B en conservant la
race choisie : 175 cm pour l’humain, 185 cm pour l’elfe. La préférence portrait
est conservée lors du départ en exploration et réappliquée au retour.
Les fonctions existantes de compte, sauvegarde, sélection, social et exploration
restent accessibles. La mise à jour du service Characters est nécessaire pour
enregistrer les deux nouveaux identifiants de couleur ; aucun déploiement de
service n’a été effectué pour cette passe.

## Vérifications terminées

Les contrôles Unity ci-dessous ont été exécutés dans **Unity 6000.6.6f1**.
Les 40 appels directs ont été rejoués après le dernier réimport, avec succès
le 20 octobre à **21:01:31 UTC**. Ils portent sur les FBX incluant les retouches
finales du col Human A et de la bordure de cuirasse Human B. La passe UI isolée,
ses quatre captures et la restauration de l’éditeur sont terminées.

| Vérification | Résultat et preuve | Portée |
|---|---|---|
| Contrats et animations Unity | [40 appels directs réussis, aucun échec](verification/reference-direct-checks.json) | Apparences, quatre prefabs et six états d’animation, semelles marche/course aux tailles minimale, par défaut et maximale à 60/120 Hz, transitions et clips de saut/nage, API de cadrage. État des scènes conservé. |
| Création en Play isolée après réimport | [240 contrôles réussis, aucun échec](after/creation-live-review.json) | 24 combinaisons race/silhouette/coiffure/taille, rotations par pas de 45°, coiffure haute, portrait, défilement, onglets et brouillons Humain/Elfe. Surfaces logiques 1600×900, 980×700, 430×900 et 830×1736 ; quatre captures réelles terminées. |
| Serveur et client locaux | [Deux races réussies](verification/creation-local-netcode-review.json) | Humain puis Elfe, connexion réelle Netcode en arène plane ; déplacement client 1,613 m et serveur 1,613333 m, écart final d’environ 0,333 mm. Mondes nettoyés, UI et scènes restaurées. |
| Portrait → île → retour | [Trajet réel réussi et 11 vérifications de retour](verification/island-roundtrip.json) | Vrais boutons `testButton` puis `returnButton`, chargement de l’île et connexion serveur/client ; écart de position d’environ 0,399 mm. Portrait, apparence, nom, racines, caméra, viewport et nettoyage des mondes vérifiés au retour. |
| Backend Characters | **23 tests `CharacterAppearanceTests` réussis** | Création, lecture et sérialisation BSON des nouvelles couleurs ; aucun déploiement de service. |

La passe UI finale ne charge que la scène de création : **236 contrôles pendant
la passe, puis quatre vérifications de conservation lors de `End`, soit 240**.
Les passes précédentes avec deux scènes ouvertes comptaient 238 contrôles,
puis six vérifications de conservation lors de `End`, soit 244 entrées dans
leurs rapports archivés. L’écart vient des vérifications par scène, sans
réduction des contrôles d’apparence ou d’interface. Le bouton Explorer est
réellement soumis, mais son
**callback local est intercepté : aucune île n’est chargée** par ce contrôle.
Le parcours réseau en arène utilise de vrais mondes serveur/client, sans entrer
dans l’île ni modifier le mode UI. Le trajet complet est couvert séparément
par `island-roundtrip.json`, **sans interception du callback**. La
[capture de l’île](after/island.png) est prise de nuit, conformément à l’heure
locale du cycle jour/nuit ; ce n’est pas un défaut du fond de création.
Ce trajet a réussi à **20:55 UTC**, avant les deux dernières retouches
géométriques localisées du col et de la cuirasse ; les contrôles directs,
la passe UI isolée et les captures natives de 21:01 portent sur les modèles
retouchés.

Les 40 contrôles synchrones sont une **invocation directe de méthodes de tests,
pas une exécution du Unity Test Runner**. Les coroutines `UnityTest`, le cycle
de vie du framework et une validation du player autonome ne sont pas couverts
par ce rapport. Les contrôles UI et réseau sont également exécutés directement
en Play, hors Test Runner. La compilation Unity et la compilation sémantique
indépendante des sources C# ont réussi. Le réimport et les captures natives
ci-dessous n’ont pas signalé d’erreur de shader. Le
[relevé final de console](verification/editor-console-final.json) indique
`compilationFailed=false`, **zéro erreur de console** et deux avertissements
dans l’état courant de l’éditeur.

## Modèles importés et captures natives

Les sources préservent 26 os, quatre meshes, les poids normalisés et les trois
takes FBX déformants. Les matrices de repos et les parents restent identiques
aux rigs précédents, avec une erreur maximale nulle ; voir les
[contrats et validations des sources](../resources/personnages/game/README.md).

La dernière correction affecte explicitement drapés, bordures, broderies,
cuirasse et sangle aux os du tronc et du bassin. Les deux faces du tissu
conservent les mêmes poids ; le drapé arrière ne reçoit plus les influences
des bras. La cuirasse et sa sangle sont ajustées à l’anatomie.

Les quatre FBX corrigés ont été copiés dans Unity. Le
[rapport SHA-256](source-copy-report.json) confirme l’identité source/cible
**4/4** et la conservation des `.meta` pendant la copie. Le réimport de cette
correction est terminé. Le [rapport de capture native](after/reference-refit-capture.json),
daté de **21:01:06 UTC**, documente **20 PNG** des vrais prefabs importés : corps
et portraits des quatre bases, vues couleur/argile de Human B et dix poses natives de marche, course,
saut et nage. La scène active et ses indicateurs de modification sont conservés,
sans sauvegarde d’assets pendant les captures. Ces vues fixes servent à vérifier
les intersections et le culling ; elles ne prouvent pas le timing des animations.
La [revue visuelle finale](fidelity-review.md) confirme la disparition de la
pointe de peau au col Human A et des éclats jade sur la bordure d’or Human B
dans les portraits et la pose de saut contrôlés.

Les captures UI post-réimport ont révélé un problème dans la session de revue :
le rechargement des scènes à l’entrée en Play avait réactivé la copie de l’île
ouverte en additif, dont le rendu polluait le fond du studio. Les contrôles UI
ont réussi, mais ces images restent classées dans
`iterations/additive-island-background-diagnostic/`. La passe UI finale a été
rejouée avec cette seule copie Play de l’île déchargée. Les quatre captures
finales ont été inspectées : fond jade du studio sans rendu de l’île,
personnage cadré et palettes accessibles par défilement.

## Captures finales de la création

Les images suivantes sont les sorties réelles `ScreenCapture`, sans montage.
Le [rapport de la passe](after/creation-live-review.json) conserve les quatre
fichiers horodatés et leurs dimensions : corps et portrait en 1600×900,
[format compact 980×700](after/creation-compact-980x700-210319795.png), puis
format mobile 430×900. Les copies ci-dessous ont des noms stables.

![Création complète — 1600×900](after/creation-desktop.png)

![Portrait dans la création — 1600×900](after/creation-portrait.png)

![Création mobile — 430×900](after/creation-mobile.png)

## Historique visuel conservé

- La première livraison a été évaluée **6/10 par l’utilisateur**. La
  [revue de fidélité](fidelity-review.md) décrit les écarts et les corrections
  successives ; elle ne constitue pas une approbation artistique finale.
- `before/` conserve les anciens prefabs. Les premières comparaisons utilisent
  les couleurs historiques du catalogue ; les rendus Blender de la seconde
  sculpture utilisent la palette magenta/jade.
- `import-orientation-review/` conserve le diagnostic des faces inversées,
  bordures ouvertes et croisements tissu/lamelles de la première refonte.
- `iterations/native-drape-diagnostic/` conserve les captures natives de la
  seconde sculpture qui ont révélé le drapé arrière étiré vers les bras en
  saut et en nage. Ce sont des images de diagnostic, **pas une validation des
  animations finales**.
- `iterations/live-baseline/`, `iterations/live-draft-return/` et les captures
  intermédiaires de création documentent les étapes de l’UI et du modèle.
  Les anciennes images ne sont pas remplacées par les nouvelles.
- `iterations/additive-island-background-diagnostic/` conserve les images au
  fond pollué par la copie additive de l’île, ainsi que la passe UI réussie
  correspondante. Les chemins de capture inscrits dans ce rapport désignent
  leur emplacement d’origine avant archivage.
- `iterations/native-surface-diagnostic/` conserve les portraits antérieurs
  aux deux dernières corrections localisées du col et de la cuirasse.

## Méthode de revue et restauration de l’éditeur

Le [rapport de restauration](verification/editor-restored.json), enregistré
à **21:04:40 UTC**, confirme la sortie de Play, l’île originale seule ouverte,
ses **18 racines actives**, `isDirty=false`, la session de revue effacée et
le réglage `runInBackground` restauré. Les copies de scènes utilisées pendant
la revue n’ont pas remplacé la scène de travail originale.

Si le lanceur de tests demande de sauvegarder l’île ouverte, annuler le lancement.
Le framework appelle `SaveModifiedSceneTask` puis `ReloadModifiedScenesTask`
avant les setups des tests : refuser l’enregistrement pourrait perdre les
modifications en mémoire. Le helper des tests protège leurs propres transitions
de scène, pas ce préambule du framework.

Les outils de revue sont dans `Unity3D/Arkya/AgentScripts/Characters/`.
`ReviewReferenceRefit` utilise les vrais prefabs importés et écrit hors de
`Assets`. Aucun rendu ne vaut approbation artistique de fidélité exacte à la
référence.

Les principaux points d’intégration sont le
[Presenter de cadrage](../../../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/CharacterPreviewPresenter.cs),
le [code de création](../../../Unity3D/Arkya/Assets/Arkya/Characters/Runtime/UI/CharacterCreationScreen.cs)
et ses fichiers UXML/USS voisins,
l’[import des modèles et du studio](../../../Unity3D/Arkya/Assets/Arkya/Characters/Editor/CharacterCreationBuilder.cs),
le [shader peint](../../../Unity3D/Arkya/Assets/Arkya/Characters/Shaders/PaintedCharacter.shader)
et les quatre [FBX du jeu](../../../Unity3D/Arkya/Assets/Arkya/Characters/Models/).

## Budgets et limites

Les [budgets détaillés des sources de jeu](../resources/personnages/game/README.md)
vont actuellement de **145 415 à 176 772 triangles visibles** selon la
silhouette et la coiffure. Le corps utilise 11 matériaux ; les options de
cheveux inactives restent stockées dans le FBX. Ce coût n’est pas un budget de
foule MMORPG validé : une mesure et des LOD dédiés restent nécessaires.

Les bases anatomiques et faciales restent Rain/Snow. Les doubles lames de la
référence, les animations de combat, le rig facial, les doigts animés
individuellement, l’IK des pieds et la simulation physique des tissus ne sont
pas livrés dans cette passe. Des poses contrôlées ne garantissent pas l’absence
d’intersections dans toutes les animations possibles.
