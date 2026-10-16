# Scène Unity de comparaison de la direction artistique

Cette scène permet de comparer les objets approuvés dans le viewer avec leur
rendu réel dans Unity, y compris la magie animée de la mare de mana. Elle utilise
le projet **`D:/Repositories/arkya/Unity3D/Arkya`**, sous **Unity 6000.6.6f1 et
URP 17.6.0**. Le projet historique `My project/` n'a pas été modifié pour cet essai.

## Ouvrir et examiner la scène

1. Ouvrir `Unity3D/Arkya` dans Unity.
2. Ouvrir [ArtDirectionComparison.unity](../../../Unity3D/Arkya/Assets/Arkya/ArtDirection/Scenes/ArtDirectionComparison.unity),
   dans `Assets/Arkya/ArtDirection/Scenes/`.
3. Lancer **Play**, puis afficher la fenêtre **Game**.
4. Utiliser les boutons **Ensemble**, **Mana**, **Arbre**, **Pierre**, **Minerai**,
   **Cristal**, **Buisson**, **Herbe**, **Champignon**, **Fleur**, **Souche** et **Tronc**.

Le glisser avec le bouton gauche fait tourner la caméra et la molette règle le
zoom. **Isoler l'objet** masque les autres objets lors d'une vue individuelle ;
la vue **Ensemble** les affiche tous. L'isolation agit sur les renderers et
conserve les animations actives. Le bouton de pause ou **Espace** fige/reprend
la magie. La touche **Début** revient à la vue d'ensemble. L'animation continue
lorsque Unity perd le focus pour permettre la comparaison avec le viewer ; le
composant restaure ensuite la valeur antérieure de `Application.runInBackground`.

Les boutons **Auto / LOD0 / LOD1 / LOD2** permettent de vérifier la réduction de
détail. **Auto** choisit le niveau selon la taille de l'objet à l'écran ; les
autres boutons imposent un niveau pour inspection. Les modèles de jeu se trouvent
dans `Assets/Arkya/ArtDirection/Optimized/Prefabs/`. Le [dossier LOD](lod/README.md)
contient les budgets, captures avant/après et contrôles de cette optimisation.

## Contenu importé

La scène contient **11 échantillons**, un pour chaque famille approuvée et l'arbre
de référence. Les copies GLB et prefabs de référence restent dans
`Assets/Arkya/ArtDirection/Models/` et `Prefabs/`. La scène utilise désormais leurs
dérivés à trois niveaux de détail dans `Optimized/Prefabs/`.

| Bouton | Référence retenue | Prefab |
|---|---|---|
| Mana | Mare d'origine, bleu azur | `Mana.prefab` |
| Arbre | Arbre peint V3, ancien moussu | `Arbre.prefab` |
| Pierre | Bloc oblique, gris bleu | `Pierre.prefab` |
| Minerai | Bloc fendu, fer | `Minerai.prefab` |
| Cristal | Bouquet azur | `Cristal.prefab` |
| Buisson | Buisson rond sylvestre | `Buisson.prefab` |
| Herbe | Touffe éventail de prairie | `Herbe.prefab` |
| Champignon | Amanite corail | `Champignon.prefab` |
| Fleur | Bouquet corail | `Fleur.prefab` |
| Souche | Souche tordue moussue | `Souche.prefab` |
| Tronc | Tronc noueux moussu | `Tronc.prefab` |

**Unity glTFast 6.20.0**, ajouté depuis le registre officiel UPM, importe les
GLB et leurs textures incorporées. Le [rapport d'import](import-report.json)
contient les chemins source/cible, les empreintes SHA256, les meshes, les
textures et les shaders de chaque modèle. Les copies conservent les fichiers
GLB approuvés ; les sources de référence restent dans
[les ressources DA](../resources/README.md).

Le contrôleur `Animation/ManaFlow.controller` joue la boucle **ManaFlow de six
secondes**, sans déplacement de racine. Les 15 nœuds animés du GLB sont conservés.
Trois copies de matériaux dans `Materials/` restituent les intensités d'émission
des volutes et des gouttes : **1,6** pour le cœur et le halo, **1,089** pour les
gouttes. Elles évitent de modifier les matériaux intégrés aux GLB de référence.
Les couleurs de sommets **`COLOR_0` de l'arbre** sont préservées, car elles portent
une part de la teinte et de l'occlusion du feuillage.

## Comparaison du rendu

La scène utilise un éclairage de studio ajusté pour rapprocher l'aspect du viewer,
un sol mat, le tonemapping **URP Neutral** et aucun bloom. Les silhouettes,
textures peintes et l'animation sont préservées par l'optimisation, mais le résultat n'est pas une
reproduction pixel pour pixel du viewer :

- glTFast ignore ici l'extension **`KHR_materials_specular`** ; les reflets peuvent
  donc différer, même si les textures et la rugosité sont présentes.
- **URP Neutral** n'est pas le tonemapping **Khronos Neutral** du viewer.
- L'éclairage de studio Unity et l'environnement du viewer ne produisent pas
  exactement les mêmes ombres ni les mêmes reflets.

Captures de comparaison : [ensemble Unity](unity-overview.png),
[mana Unity](unity-mana.png), [arbre Unity](unity-tree.png),
[minéraux Unity](unity-minerals.png), [mana dans le viewer](viewer-mana-reference.jpg).

## Vérifications effectuées

Le [rapport de validation en Play Mode](validation.json) confirme :

- animation `ManaFlow` en boucle, durée de **6 secondes** ;
- **107 courbes** résolues et mouvement mesuré sur **15 nœuds sur 15** ;
- pause sans dérive et reprise sur plus d'une boucle complète ;
- **12 vues** contrôlées, ensemble et 11 objets ;
- **22 matériaux** présents et aucun shader non pris en charge ;
- présence des couleurs de sommets du feuillage de l'arbre.

La compilation dans Unity est passée sans erreur. Le [contrôle d'intégrité](integrity.json)
confirme les 11 copies GLB identiques aux sources, les fichiers `.meta` présents et
les 80 empreintes des références arbre/mana inchangées. Le cadrage de l'arbre
sous la barre d'outils a également été [vérifié dans la fenêtre Game](unity-controls.png).

Ces contrôles vérifient le fonctionnement de l'import et de la scène dans
l'éditeur. La comparaison visuelle est une vérification distincte, illustrée par
les captures ci-dessus. Les performances d'un build de production n'ont pas été
mesurées. Les captures et rapports ci-dessus documentent l'import initial.
La passe suivante a ajouté **trois LOD par objet**, le fondu entre niveaux,
la réutilisation des textures et la fusion des parties statiques du cristal.
Ses résultats actuels sont consignés dans le [rapport LOD](lod/README.md).
La scène ne fournit pas de colliders de gameplay.

Les quinze modules de construction disposent d'une [intégration distincte](structures/README.md)
dans `Assets/Arkya/Building/Structures/` : prefabs, colliders composés, accroches
typées et catalogue. Leur scène de revue n'altère pas cette comparaison DA.

## Rejouer les contrôles avec Unity CLI

Les scripts dans [AgentScripts/ArtDirection](../../../Unity3D/Arkya/AgentScripts/ArtDirection/)
sont des outils de reprise hors du dossier `Assets`. Ils sont exécutés dans
l'éditeur connecté via le package Pipeline déjà présent. Le script runtime de
navigation est [ArtDirectionPreview.cs](../../../Unity3D/Arkya/Assets/Arkya/ArtDirection/Runtime/ArtDirectionPreview.cs).

Exemple PowerShell avec le chemin complet du CLI fourni par Unity Hub, si
`unity` n'est pas dans le `PATH` :

```powershell
$arkyaCli = 'C:/Program Files/Unity Hub/resources/unity.exe'
$arkyaProject = 'D:/Repositories/arkya/Unity3D/Arkya'

& $arkyaCli status --project-path $arkyaProject --json
& $arkyaCli command editor_play --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/ArtDirection/ValidateArtDirectionScene.cs --entry ValidateArtDirectionScene.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json
```

Ouvrir d'abord la scène de comparaison et attendre que l'éditeur soit disponible.
Le contrôle nécessite le mode **Play** et dure environ huit secondes ; ne pas
mettre l'éditeur en pause pendant son exécution. Il réécrit `validation.json`.

Les deux scripts de création sont conservés pour une reproduction contrôlée :

```powershell
& $arkyaCli command run_script --file AgentScripts/ArtDirection/ImportArtDirection.cs --entry ImportArtDirection.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/ArtDirection/BuildArtDirectionScene.cs --entry BuildArtDirectionScene.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json
```

Ces deux commandes nécessitent le mode **Edit**. L'import refuse de remplacer
une copie GLB dont l'empreinte diffère de sa source. Le constructeur refuse une
scène ouverte avec des modifications non enregistrées et refuse de remplacer
une scène de comparaison déjà existante. Pour examiner ou ajuster la scène
livrée, ouvrir celle-ci directement plutôt que relancer sa création.
