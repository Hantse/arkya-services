# Wild Wolf — adaptation Arkya

Source fournie par l'utilisateur : `free_wild_wolf.zip`, contenant `wild wolf.glb`
et son aperçu. Le modèle est un lycan bipède. Le GLB original n'a ni squelette
ni animation. Aucun fichier de licence n'accompagne l'archive.

La copie Arkya conserve sa silhouette et ses couleurs avec un mesh de 17 950
triangles, 8 983 sommets source, un squelette de 20 os et des textures de 2 048².
Les textures sont intégrées au fichier Blender final. Les poids sont calculés
par diffusion thermique, limités à quatre influences et normalisés. `Root`
ne déforme pas le mesh. La hauteur est de 2,2 m, avec les pieds au sol.

Fichiers finaux à conserver :

- `prepared/WildWolf.blend` : source éditable, textures intégrées.
- `prepared/WildWolf.fbx` : mesh, squelette et court clip `WolfIdle`.
- `prepared/WildWolfAlbedo.png` et `prepared/WildWolfNormal.png` : textures runtime.
- `prepared/WildWolf-provenance.json` : paramètres, empreinte source et validation.
- `prepared/WildWolf-review.png` et `prepared/WildWolf-skin-check.png` : captures Blender.
- `retopologize_wolf.py`, `adapt_wolf.py`, `check_wolf_skin.py` : reconstruction.

Ne pas retenir les sauvegardes `.blend1` ni les premiers essais de réduction.

## Reconstruction

Outil utilisé : Blender 4.5.14 LTS portable. Les scripts ne modifient pas Unity.
Ils travaillent dans leur dossier, ou dans le dossier défini par
`ARKYA_CREATURE_BUILD_DIR`.

1. Extraire uniquement le GLB original dans
   `source/free_wild_wolf/wild wolf.glb` sous le dossier de travail.
2. Exécuter Blender en arrière-plan avec `--python retopologize_wolf.py`.
   La surface est consolidée à une taille de voxel de 0,0028 unité source,
   réduite à 17 950 triangles, dépliée et ses couleurs/normales sont rebakées
   depuis l'original. Cette étape produit `wolf_reduced.blend` et deux textures
   intermédiaires `WolfRebaked*.png`.
3. Exécuter `--python adapt_wolf.py` pour créer le rig, les poids, le FBX,
   les textures et le fichier Blender éditable dans `prepared/`.
4. Exécuter `--python check_wolf_skin.py` pour vérifier les poids et une pose
   de marche et produire la capture correspondante.

Exemple PowerShell :

```powershell
$env:ARKYA_CREATURE_BUILD_DIR = 'D:/chemin/de/travail'
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python './retopologize_wolf.py'
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python './adapt_wolf.py'
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --python './check_wolf_skin.py'
```

Le FBX est exporté avec `axis_forward=-Z`, `axis_up=Y` depuis Blender
(orientation native -Y, verticale +Z). Vérifier l'orientation après import
Unity. La présentation du jeu observe l'état serveur et anime les os en
offset de leur pose de repos ; elle ne dépend pas du clip de démonstration.

Validation Blender du dérivé final : tous les 8 983 sommets ont des poids,
quatre influences au maximum, somme minimale 0,999999847. La pose de marche
déplace 7 872 sommets, sans valeur non finie. Les captures de repos et de
flexion ont été inspectées. Ces contrôles ne remplacent pas la vérification
du modèle et des animations dans Unity.
