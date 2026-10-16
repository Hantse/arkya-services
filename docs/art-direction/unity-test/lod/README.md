# LOD des échantillons de direction artistique

Cette passe prépare les **11 échantillons de la scène de comparaison** avec trois
niveaux de détail chacun. Elle ne convertit pas encore toutes les déclinaisons
de la bibliothèque DA. Les GLB et fichiers Blender approuvés restent inchangés ;
les géométries de jeu sont des dérivés distincts.

## Budgets de géométrie

Les nombres ci-dessous viennent de [geometry-report.json](geometry-report.json).
Ils comptent les triangles d'un exemplaire de chaque objet, en choisissant un
seul niveau. Pendant une transition en fondu, deux niveaux peuvent être rendus.

| Objet | Référence | LOD0, proche | LOD1 | LOD2, lointain |
|---|---:|---:|---:|---:|
| Arbre | 429 204 | 140 282 | 65 212 | 35 660 |
| Pierre | 7 820 | 4 692 | 1 954 | 782 |
| Minerai | 8 948 | 5 367 | 2 235 | 893 |
| Cristal | 2 598 | 2 598 | 1 680 | 898 |
| Buisson | 29 708 | 25 250 | 13 368 | 7 426 |
| Herbe | 10 668 | 6 400 | 3 200 | 1 600 |
| Champignon | 11 968 | 7 180 | 2 992 | 1 196 |
| Fleur | 20 968 | 17 822 | 5 242 | 2 096 |
| Souche | 21 504 | 11 826 | 5 160 | 2 150 |
| Tronc | 10 906 | 6 542 | 2 726 | 1 090 |
| Mana | 18 172 | 8 915 | 3 875 | 1 653 |
| **Total** | **572 464** | **236 874** | **107 644** | **55 444** |
| **Réduction** | — | **58,62 %** | **81,20 %** | **90,31 %** |

Le cristal garde sa géométrie complète au LOD0 : elle est déjà modeste et porte
les arêtes biseautées de la silhouette. Ses onze renderers sont regroupés par
matériau en **deux renderers par niveau** dans les prefabs Unity.

## Conservation de la DA

Le générateur réduit la tessellation selon la famille et la partie du modèle,
puis transfère les normales de la référence pour préserver son éclairage lissé.
Les UV, slots de matériaux et couleurs de sommets du feuillage restent présents.
Les textures peintes approuvées sont réutilisées par les trois niveaux ; les GLB
intermédiaires n'incorporent aucune texture supplémentaire.

Le buisson et les fleurs conservent 85 % de leurs triangles au LOD0 pour mieux
préserver les petits volumes. Les feuilles de l'arbre restent plus denses aux
LOD1 et LOD2 afin de maintenir la masse de la couronne. Sur les feuilles et
pétales fins du buisson et des fleurs, un transfert vers la surface la plus
proche peut sélectionner l'autre face. Le générateur conserve donc la normale
du mesh réduit si le transfert la tournerait de plus de 60°. Cette protection
concerne respectivement **16 / 795 / 9 902 coins** du buisson et **0 / 691 / 800**
coins des fleurs aux LOD0 / LOD1 / LOD2 ; elle évite les inversions introduites
par cette projection. Les autres familles conservent leur méthode de transfert.

Pour l'arbre, le LOD0 réduit davantage les petits rameaux que les feuilles. La
distance maximale mesurée entre les sommets échantillonnés de la référence et la
surface réduite reste d'environ **6,4 mm pour le tronc, 14,2 mm pour le feuillage
et 10,6 mm pour les rameaux**, dans l'espace local du modèle. Cette mesure de
surface ne remplace pas une comparaison du rendu et des transitions dans Unity.

Les matériaux Unity utilisent des copies des Shader Graphs glTFast permettant
le fondu de LOD. Leurs textures et paramètres visuels restent ceux de la scène
de comparaison. Les sources du package ne sont pas modifiées ; la provenance et
la licence figurent dans
[Optimized/Shaders/README.md](../../../../Unity3D/Arkya/Assets/Arkya/ArtDirection/Optimized/Shaders/README.md).

La mare conserve **un seul Animator**, son clip `ManaFlow` de six secondes et
les transformations animées d'origine. Chaque mesh de LOD est associé au même
nœud animé ; les niveaux supplémentaires n'ajoutent pas d'Animator. Les GLB
intermédiaires n'exportent pas l'animation. Il faut reprendre uniquement leurs
meshes : leurs transformations de mana représentent une pose importée et ne
doivent pas remplacer celles de la hiérarchie d'origine.

## Emplacement et reproduction

- Générateur Blender : [generate_lods.py](../../../../Unity3D/Arkya/AgentScripts/ArtDirection/generate_lods.py).
- Fichiers intermédiaires : `Unity3D/Arkya/ArtDirectionGenerated/Models/<source>-LOD0.glb`, `-LOD1.glb`, `-LOD2.glb`.
- Géométries, matériaux, shaders et prefabs Unity : `Unity3D/Arkya/Assets/Arkya/ArtDirection/Optimized/`.
- Préparation de l'import : [PrepareArtDirectionLods.cs](../../../../Unity3D/Arkya/AgentScripts/ArtDirection/PrepareArtDirectionLods.cs).
- Construction des prefabs : [BuildArtDirectionLods.cs](../../../../Unity3D/Arkya/AgentScripts/ArtDirection/BuildArtDirectionLods.cs).
- Finalisation des matériaux et meshes : [FinalizeArtDirectionLods.cs](../../../../Unity3D/Arkya/AgentScripts/ArtDirection/FinalizeArtDirectionLods.cs).
- Mise à jour des trois familles retouchées : [RefreshArtDirectionLodMeshes.cs](../../../../Unity3D/Arkya/AgentScripts/ArtDirection/RefreshArtDirectionLodMeshes.cs).
- Contrôles Unity : [ValidateArtDirectionLods.cs](../../../../Unity3D/Arkya/AgentScripts/ArtDirection/ValidateArtDirectionLods.cs).
- Contrôles des transitions par rendu : [ValidateArtDirectionLodSwitching.cs](../../../../Unity3D/Arkya/AgentScripts/ArtDirection/ValidateArtDirectionLodSwitching.cs).

Depuis `D:/Repositories/arkya`, régénérer les seuls dérivés de géométrie :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python 'Unity3D/Arkya/AgentScripts/ArtDirection/generate_lods.py'
```

Contrôler les fichiers existants et leurs empreintes sans régénérer les meshes :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python 'Unity3D/Arkya/AgentScripts/ArtDirection/generate_lods.py' -- --verify-only
```

La génération ne réintègre pas automatiquement les dérivés dans Unity. Le script
de construction des prefabs refuse de remplacer une installation déjà présente
et exige la scène enregistrée en mode Edit. Pour examiner les objets livrés,
ouvrir la scène de comparaison existante plutôt que relancer sa construction.

Dans la fenêtre **Game**, choisir **Auto** pour le détail selon la distance, ou
**LOD0 / LOD1 / LOD2** pour inspecter un niveau. La molette permet de s'éloigner.
Le niveau PC actuel utilise `lodBias = 2` ; les seuils du LODGroup sont corrigés
par ce réglage Unity. Les références complètes restent dans `Prefabs/`, les
prefabs utilisés par la scène sont dans `Optimized/Prefabs/`.

Une mise à jour de géométrie recrée les buffers natifs, réassocie les meshes
aux prefabs puis force leur réimport. Un simple `CopySerialized` avait laissé
des données de rendu périmées dans l'éditeur ; les captures après rechargement
complet font donc partie de la vérification.

## Mémoire et limites

Les données brutes de géométrie de l'arbre occupent environ **22,53 Mo dans le
GLB d'origine**. Les fichiers GLB dérivés, sans textures, pèsent **9,81 Mo pour
le LOD0**, **6,35 Mo pour le LOD1** et **4,35 Mo pour le LOD2**. Les trois fichiers
totalisent donc 20,50 Mo ; une scène chargée conserve généralement plusieurs
niveaux en mémoire,
même si un seul est affiché. Ce sont des tailles de données GLB, pas une mesure
de mémoire CPU/GPU dans le lecteur Unity.

Les nombreuses coutures UV de l'écorce et les attributs du feuillage expliquent
une part du nombre de sommets. Fusionner uniquement les positions abîmerait les
textures ou l'éclairage. L'audit du LOD0 n'a trouvé que 68 sommets de feuillage
ayant les mêmes position, UV et couleur mais d'autres normales ou tangentes :
supprimer les normales transférées n'apporterait pas un gain substantiel.

Les meshes de rendu finalisés sont marqués non lisibles avec
`UploadMeshData(true)` après les combinaisons et vérifications, pour libérer leur
copie CPU. Cela conserve les buffers graphiques mais empêche la lecture ou la
modification des tableaux de sommets au
runtime. Il faut garder des meshes lisibles pour les usages qui en dépendent,
par exemple une combinaison ou un bake NavMesh au runtime. Voir les références
Unity [UploadMeshData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.UploadMeshData.html)
et [isReadable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-isReadable.html).

Cette passe ne fournit ni collisions de gameplay, ni imposteurs pour une forêt
très lointaine, ni mesure sur une carte MMO complète. Les renderers de mana
animés restent séparés afin de conserver leurs mouvements. Les budgets devront
être évalués avec la densité réelle d'objets, les ombres et les plateformes
cibles. Une réduction du nombre de triangles ne constitue pas une promesse de
gain de FPS équivalent.

## État de validation

**Géométrie validée :** 33 GLB, empreintes des 11 sources inchangées, attributs
requis conservés, indices valides, valeurs finies, normales unitaires et aucun
triangle dégénéré. Le détail est enregistré dans
[geometry-report.json](geometry-report.json).

**Validation Unity réussie**, sous Unity 6000.6.6f1 / URP 17.6.0 :

- [Structure et animation](validation.json) : 11 groupes, 33 niveaux, 108 meshes,
  budgets décroissants, 114 slots de matériaux comparés à la référence,
  43 textures réutilisées exactement et 108 dispositions d'attributs vérifiées.
  Un seul Animator, 107 courbes résolues, mouvement des 15 nœuds, boucle complète,
  pause sans dérive et enveloppe animée contenue dans les limites du groupe.
- [Sélection et transitions](switching-validation.json) : les 33 sélections
  automatiques donnent le même rendu que le niveau attendu imposé ; les
  11 disparitions à distance passent le contrôle par pixels. Les fondus
  LOD0 → LOD1 de l'arbre et du mana présentent un mélange stable des deux
  niveaux, sans disparition. Captures 1280 × 960, `lodBias = 2`.
- [Intégrité](integrity.json) : 11 GLB de référence identiques aux sources,
  80 fichiers approuvés arbre/mana inchangés et fichiers `.meta` présents.
- Les 108 meshes finaux sont non lisibles au runtime. Compilation sans erreur.

Les captures `*-original.png` et `*-lod0/1/2.png` utilisent les mêmes caméras,
le même éclairage et la même phase de magie. Les niveaux lointains y sont aussi
montrés de près pour révéler leurs simplifications ; ce grossissement ne
représente pas leur taille normale en jeu.

Exemples : [arbre original](arbre-original.png), [arbre LOD0](arbre-lod0.png),
[arbre LOD2](arbre-lod2.png), [buisson LOD0](buisson-lod0.png),
[fleur LOD0](fleur-lod0.png), [mana LOD0](mana-lod0.png),
[commandes dans Unity](unity-controls.png).

La revue visuelle finale ne relève plus de défaut bloquant. Les petits détails
se simplifient avec la distance ; il ne s'agit pas d'une identité pixel pour pixel.
