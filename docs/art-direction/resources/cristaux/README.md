# Cristaux peints — bibliothèque Arkya

**Statut : validé par l'utilisateur le 15 octobre 2026**, pour les quatre
formes/tailles et les trois matières (12 variantes).

Douze ressources 3D : quatre formations, chacune en **Azur**, **Améthyste** et **Ambre**. La direction prolonge l’arbre V3 validé : grandes masses lisibles, faces peintes, biseaux doux, ombres colorées et détails choisis. Les corps restent opaques ; la profondeur est suggérée par les glacis et les fissures internes, avec une émission discrète.

![Les quatre formes à la même échelle](comparaison-tailles.png)

| Forme | Hauteur | Largeur | Profondeur | Triangles |
|---|---:|---:|---:|---:|
| Éclat, 3 pointes | 0,55 m | 0,4667 m | 0,3157 m | 1 894 |
| Bouquet, 7 pointes | 1,10 m | 1,0541 m | 0,9296 m | 2 598 |
| Géode ouverte | 1,90 m | 1,7557 m | 1,0366 m | 4 712 |
| Formation maîtresse | 3,00 m | 2,3802 m | 2,2057 m | 3 120 |

Les trois matières conservent exactement la géométrie et les dimensions de chaque forme. Les axes, longueurs, sections et épaules des prismes varient. La géode possède une véritable coque ouverte et des pointes orientées vers sa cavité.

## Fichiers

- [manifest.json](manifest.json) : formes, matières et douze ressources ; chemins relatifs à ce dossier, dimensions réelles en mètres.
- `assets/<forme>-<matiere>/` : GLB autonome, source Blender, aperçu et fiche JSON.
- `textures/<matiere>/` : atlas de couleur, rugosité et émission, **2048 × 2048 px** ; un atlas partagé par les quatre formes de chaque matière.
- `textures/stone/` : atlas PBR 2K commun aux supports rocheux.
- [comparaison-tailles.png](comparaison-tailles.png) : une scène orthographique à échelle commune, rendue en 1900 × 1000 px.
- [validation.json](validation.json) : résultats des contrôles automatiques sur les douze ressources.

Les UV des cristaux sont dessinés face par face dans un atlas de seize variations. Les textures sont obtenues par bake CPU de matériaux Blender : dégradés colorés, grands glacis diagonaux, variations brossées, arêtes peintes et rares fissures. Les biseaux sont portés par la géométrie ; aucune normal map n’est nécessaire. Chaque ressource utilise deux matériaux, cristal et pierre.

Les GLB contiennent uniquement la ressource, ses textures et son pivot `Resource_Root`, centré horizontalement et posé au sol. Blender utilise Z vers le haut ; l’export GLB convertit vers Y. Dans les fichiers Blender, la collection `Arkya_Crystals` regroupe les maillages ; le plateau et les lumières servent uniquement à la présentation.

## Génération et vérification

Scripts pour Blender **4.5.14 LTS**, sans script téléchargé. Le bake reste sur CPU. Les rendus utilisent le GPU uniquement avec `--gpu`.

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --disable-autoexec --python 'scripts/generate_crystals.py' -- --shape all --material all --render --gpu
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --disable-autoexec --python 'scripts/render_comparison.py' -- --gpu
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --factory-startup --disable-autoexec --python 'scripts/validate_crystals.py'
```

Exécuter ces commandes depuis ce dossier. Pour un essai CPU : `--shape bouquet --material azur --render --draft`. `--rebake` régénère les atlas. Sans `--render`, seuls les modèles sont produits. Les aperçus finaux sont des rendus Blender **1000 × 1000 px, Cycles 32 échantillons**, pas des captures du jeu.

Validation du 15 octobre 2026 : **12/12 conformes**. Contrôles des dimensions, du pivot, du budget de triangles, des UV, des textures PNG 2K intégrées, des matériaux opaques et de l’absence de dépendances externes. Les quatre silhouettes et les trois palettes ont été inspectées dans les aperçus ; le bouquet Azur a également été contrôlé dans le viewer à environnement neutre.

Ces ressources constituent une bibliothèque de direction artistique exploitable. LOD, collisions, règles de placement et intégration Unity restent à produire selon le besoin du jeu.

## Provenance

Géométries et matériaux de cristaux créés pour Arkya par le générateur de ce dossier. Aucun modèle tiers utilisé. La pierre des supports incorpore la source partagée [stone-painted.png](../sources/stone-painted.png), générée pour cette bibliothèque ; sa [provenance](../sources/stone-painted-provenance.md) est conservée avec la source. Les textures finales restent intégrées aux GLB et aux fichiers Blender même sans ce dossier partagé.

Overwatch et Palworld servent de références de langage visuel ; aucun fichier de ces jeux n’est inclus.
