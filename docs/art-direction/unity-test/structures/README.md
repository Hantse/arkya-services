# Structures modulaires dans Unity

Les **15 structures** sont intégrées au projet actif `Unity3D/Arkya`, sous Unity
**6000.6.6f1**, URP **17.6.0** et glTFast **6.20.0**. Elles conservent les modèles
et textures de la [collection peinte](../../resources/structures/README.md).

## Examiner et utiliser les pièces

- Ouvrir `Assets/Arkya/Building/Structures/Scenes/StructureKitReview.unity` pour
  examiner les quinze prefabs. Les colonnes sont fondation, mur plein, fenêtre,
  porte et plafond ; les rangées vont du chaume au premier plan à la pierre au fond.
- Glisser les prefabs depuis `Assets/Arkya/Building/Structures/Prefabs/` dans une
  scène, en conservant une échelle de **1**.
- Sélectionner la racine d'une pièce et activer **Gizmos** pour afficher son
  gabarit cyan, les six accroches orange et leurs normales.
- Le catalogue `Assets/Arkya/Building/Structures/StructureCatalog.asset`
  référence les quinze composants `BuildModuleAuthoring` et permet une recherche
  par identifiant stable.

![Les quinze prefabs rendus dans Unity URP](unity-structures.png)

La scène de revue possède son éclairage, son sol sans collider et sa caméra. Elle
ne modifie pas la scène de comparaison DA et n'est pas ajoutée aux Build Settings.

## Contrat des prefabs

Tous les modules ont une racine sans décalage ni rotation, à échelle unitaire,
avec un pivot au **centre de la base**. Les mesures sont en mètres, Y vertical.

| Module | Largeur × hauteur × profondeur | BoxColliders |
|---|---|---:|
| Fondation | 1 × 0,50 × 1 | 1 |
| Mur plein | 1 × 1 × 0,15 | 1 |
| Mur avec fenêtre | 1 × 1 × 0,15 ; baie 0,50 × 0,50, allège à 0,30 | 4 |
| Mur avec porte | 1 × 2 × 0,15 ; passage 0,72 × 1,85, sans seuil | 3 |
| Plafond | 1 × 0,10 × 1 | 1 |

Chaque prefab contient :

- `BuildModuleAuthoring` : identité, forme, matière, dimensions et références
  explicites vers le visuel, les colliders et les accroches ;
- `Visual` : géométrie et matériaux référencés dans une copie GLB identique à la
  source, avec textures incorporées ;
- `Colliders` : **BoxColliders non déclencheurs**, couvrant le gabarit utile et
  laissant les ouvertures libres ; aucun Rigidbody ou MeshCollider ;
- `SnapPoints/Snap_Left`, `Snap_Right`, `Snap_Bottom`, `Snap_Top`, `Snap_Front`
  et `Snap_Back`, chacun avec un composant `BuildSocketAuthoring` typé.

Les accroches sont au centre des six faces du gabarit : **Left = −X**, **Right =
+X**, **Bottom = −Y**, **Top = +Y**, **Front = +Z**, **Back = −Z**. Leur position
est locale à la racine ; `Transform.forward` donne la normale sortante. Les
repères vides du GLB sont retirés du visuel du prefab et remplacés par ces
accroches canoniques, pour éviter l'inversion de X de glTFast. Les sources GLB
restent intactes ; voir le [contrat de conversion](CONVERSION.md).

Les faces donnent des repères géométriques. Elles ne définissent pas encore
quelles pièces sont compatibles : un mur posé en bord de fondation nécessite
une règle de placement tenant compte de leurs gabarits, plutôt qu'un simple
alignement de centres de faces.

Les identifiants suivent `arkya.structure.<forme>.<matière>` :
`foundation`, `solid-wall`, `window-wall`, `door-wall`, `ceiling`, combinés à
`thatch`, `wood` ou `stone`. Par exemple : `arkya.structure.window-wall.wood`.
`SourceAssetId` conserve l'identifiant artistique, par exemple `mur-fenetre-bois`.

## Limite de cette intégration

Les composants dans `Assets/Arkya/Building/Authoring/` portent uniquement des
données d'authoring. Ils n'ont ni boucle de gameplay, ni état de construction
autoritaire. Aucun package n'a été ajouté. Entities, Netcode for Entities et Unity
Physics ne sont pas installés dans ce projet : les Bakers et les colliders DOTS
seront ajoutés lors de leur intégration. Les colliders livrés utilisent PhysX.

Cette étape ne fournit pas la pose interactive, les règles de support, le coût,
la résistance, la réplication ou la sauvegarde. Ces décisions appartiendront à
la simulation serveur. Aucun LOD supplémentaire n'est généré pour ce kit.

## Reproduire l'import et les contrôles

Les scripts [AgentScripts/Structures](../../../../Unity3D/Arkya/AgentScripts/Structures/)
s'exécutent dans l'éditeur connecté par le package Pipeline existant.

```powershell
$arkyaCli = 'C:/Program Files/Unity Hub/resources/unity.exe'
$arkyaProject = 'D:/Repositories/arkya/Unity3D/Arkya'

# En Edit Mode, après compilation des composants d'authoring.
& $arkyaCli command run_script --file AgentScripts/Structures/ImportStructures.cs --entry ImportStructures.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/Structures/CreateStructureReviewScene.cs --entry CreateStructureReviewScene.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/Structures/ValidateStructures.cs --entry ValidateStructures.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json

# Ouvrir la scène de revue, lancer Play, puis exécuter la même validation.
& $arkyaCli command editor_play --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command run_script --file AgentScripts/Structures/ValidateStructures.cs --entry ValidateStructures.Main --project-path $arkyaProject --caller plugin --skill unity-cli --json
& $arkyaCli command editor_stop --project-path $arkyaProject --caller plugin --skill unity-cli --json
```

L'import crée les ressources manquantes et conserve les prefabs et GUID existants.
Il refuse de remplacer une copie GLB modifiée ou un prefab non reconnu. Il ne
réécrit pas les réglages manuels de prefabs existants. Le constructeur de revue
conserve une scène déjà créée. Une évolution des sources exige donc une mise à
jour explicite des assets concernés, suivie de la validation.

Le [rapport d'import](import-report.json) conserve les chemins, empreintes et
références Unity. Les rapports [Edit Mode](validation.json) et
[Play Mode](playmode-validation.json) vérifient les quinze variantes, les
dimensions réelles des sommets, les références des matériaux/textures, les
colliders et ouvertures, les GUID, le catalogue et les accroches sous quatre
rotations. En Edit Mode, les rayons interrogent chaque collider en scène de
prévisualisation. En Play Mode, les rayons utilisent un monde physique 3D isolé.
Les références approuvées arbre/mana sont aussi contrôlées par empreinte.

Résultat de cette intégration : **15/15 prefabs validés** en Edit et Play Mode,
**30 BoxColliders**, **90 accroches** vérifiées à 0°, 90°, 180° et 270°, et
**6 765 rayons par passe**, dont **1 146 traversent les ouvertures**. Les quinze
identifiants et leurs GUID restent identiques entre les deux passes. La
compilation de l'éditeur est passée sans erreur. La capture ci-dessus provient
de la scène de revue en Play Mode.

Le [contrôle de réexécution](idempotence.json) confirme qu'un second import ne
crée ni modèle ni prefab et ne modifie aucun des 80 fichiers du nouveau dossier
Building. Le [contrôle d'intégrité](integrity.json) consigne les métadonnées
Unity et la préservation des ressources antérieures, packages et réglages.
