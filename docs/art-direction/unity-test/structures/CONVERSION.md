# Préparation des structures pour Unity

Les quinze modèles utilisent le format **GLB**, déjà importé par glTFast 6.20.0
dans le projet `Unity3D/Arkya`. Aucune conversion FBX n'est nécessaire. Les textures
peintes sont incorporées aux GLB ; les sources restent dans
[`resources/structures`](../../resources/structures/README.md).

Depuis n'importe quel dossier, exécuter :

```powershell
python D:/Repositories/arkya/docs/art-direction/unity-test/structures/prepare_import.py
```

Le script vérifie les quinze fichiers et produit [import-manifest.json](import-manifest.json)
avec leurs chemins, empreintes, matériaux, textures et points d'accrochage.
Il ne modifie aucun fichier source, dossier `Assets` ou scène. Le pipeline
[`ImportStructures.cs`](../../../../Unity3D/Arkya/AgentScripts/Structures/ImportStructures.cs)
importe des copies GLB identiques aux sources selon le même principe que le
pipeline de comparaison DA existant.

## Repère Unity et points d'accrochage

Le package glTFast installé convertit les positions glTF `(x,y,z)` en
**`(-x,y,z)`** dans Unity. Il conserve les mètres, Y vertical et le pivot au centre
de la base. Les dimensions et les ouvertures restent inchangées.

Cette réflexion échange les côtés X : le nœud nommé `Snap_Left` est importé à
`+largeur/2`, et `Snap_Right` à `-largeur/2`. Le prefab doit échanger ces deux noms
ou réaffecter leurs positions pour que **Left = −X et Right = +X**.
`Snap_Front` reste à `+profondeur/2` sur Z, `Snap_Back` à son opposé,
`Snap_Top` à la hauteur du module et `Snap_Bottom` à zéro.

Le manifeste de préparation donne pour chaque nœud son nom source, sa position
Unity importée, sa normale Unity et son nom canonique. Les normales d'accrochage
subissent la même conversion `(-nx,ny,nz)`. Les rotations des empties source sont
identité : leur flèche Blender n'encode pas l'orientation d'accrochage. Le prefab
doit orienter les points à partir des normales converties si son futur système
de build utilise `Transform.forward`.

Les métadonnées GLB utilisent `arkya_pivot = bottom_center`. Ne pas réintroduire
une propriété glTF `extras.pivot` textuelle : certains lecteurs l'interprètent
comme un vecteur numérique de décalage.

## Matières

Le manifeste décrit les matériaux PBR et leurs textures originales. La rugosité
est constante par matériau, sans carte dédiée. Si l'intégration crée des matériaux
URP/Lit partagés, son équivalent est `smoothness = 1 − roughness`. Le bois scié et
l'ossature partagent leur bitmap ; les liens et le chaume partagent le leur ; la
pierre et le mortier partagent également leur bitmap avec des rugosités distinctes.
Les empreintes permettent d'éviter les copies inutiles.

La prise en charge de `KHR_materials_specular` reste celle du pipeline existant.
Le traitement des prefabs, colliders, scènes et contrôles dans l'éditeur est
décrit dans le [guide d'intégration](README.md). Les repères vides importés sont
retirés du visuel du prefab, puis remplacés par six accroches typées, en repère
Unity canonique. Cette préparation hors Unity conserve aussi les repères source
dans son manifeste pour l'audit.
