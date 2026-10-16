# Mares de mana — références DA validées

Des mares organiques et aplaties, avec un liquide peint irisé, des courants de
surface, des volutes magiques et de petites gouttes flottantes. Les couleurs
sont **bleu, orange et vert**, chacune avec une surface et des volutes assorties.

**La mare d'origine et ses trois couleurs ont été validées par l'utilisateur
le 15 octobre 2026.** Elles rejoignent les [références DA validées](../README.md).
La validation porte sur la forme d'origine, les matières peintes et l'animation.
Les fichiers de cette première référence sont conservés à l'identique.

Les deux nouvelles silhouettes, **Petite mare allongée** et **Grande mare à lobes**,
ont ensuite été **validées par l'utilisateur le 15 octobre 2026**, chacune dans
les trois couleurs. Ces six modèles rejoignent la forme d'origine : les neuf
mares sont désormais des références DA approuvées, avec leurs matières et leurs
animations. Les [fichiers de référence](approved-reference.json), comprenant
les GLB, les sources Blender, les rendus, les textures et les générateurs,
sont conservés à l'identique et contrôlés par SHA-256.

## Variantes

La collection comprend **3 formes × 3 couleurs = 9 modèles validés**. La
[comparaison à la même échelle](comparaison-tailles.png) montre les trois formes en bleu.

### Mare d'origine — référence validée

| Matière | Modèle 3D | Source Blender | Aperçu |
|---|---|---|---|
| Bleu azur | [GLB](assets/mare-bleue/mare-bleue.glb) | [Blender](assets/mare-bleue/mare-bleue.blend) | [PNG](assets/mare-bleue/mare-bleue-preview.png) |
| Orange ambré | [GLB](assets/mare-orange/mare-orange.glb) | [Blender](assets/mare-orange/mare-orange.blend) | [PNG](assets/mare-orange/mare-orange-preview.png) |
| Vert émeraude | [GLB](assets/mare-verte/mare-verte.glb) | [Blender](assets/mare-verte/mare-verte.blend) | [PNG](assets/mare-verte/mare-verte-preview.png) |

Les trois variantes partagent exactement leur géométrie et leur animation.
L'enveloppe animée mesure **2,707 × 2,036 m au sol**, et **0,798 m de haut**,
gouttes comprises. Les dimensions sont conservées dans le [manifeste](manifest.json).
Le liquide a une épaisseur de quelques centimètres ; les volutes s'élèvent
au-dessus de sa surface.

### Petite allongée et grande à lobes — références validées

La petite mare dessine une flaque étroite et sinueuse, avec trois volutes
réparties le long du liquide. La grande mare forme une nappe asymétrique
à lobes arrondis, avec sept volutes. Leurs contours et leurs courants de
surface sont reconstruits pour chaque silhouette.

| Forme | Largeur × profondeur | Hauteur avec magie | Triangles |
|---|---|---|---:|
| Petite allongée | 1,790 × 0,752 m | 0,591 m | 12 156 |
| Mare d'origine validée | 2,707 × 2,036 m | 0,798 m | 18 172 |
| Grande à lobes | 3,290 × 2,632 m | 0,941 m | 22 136 |

| Forme | Couleur | Modèle 3D | Source Blender | Aperçu |
|---|---|---|---|---|
| Petite allongée | Bleu | [GLB](assets/allongee-bleu/allongee-bleu.glb) | [Blender](assets/allongee-bleu/allongee-bleu.blend) | [PNG](assets/allongee-bleu/allongee-bleu-preview.png) |
| Petite allongée | Orange | [GLB](assets/allongee-orange/allongee-orange.glb) | [Blender](assets/allongee-orange/allongee-orange.blend) | [PNG](assets/allongee-orange/allongee-orange-preview.png) |
| Petite allongée | Vert | [GLB](assets/allongee-vert/allongee-vert.glb) | [Blender](assets/allongee-vert/allongee-vert.blend) | [PNG](assets/allongee-vert/allongee-vert-preview.png) |
| Grande à lobes | Bleu | [GLB](assets/lobee-bleu/lobee-bleu.glb) | [Blender](assets/lobee-bleu/lobee-bleu.blend) | [PNG](assets/lobee-bleu/lobee-bleu-preview.png) |
| Grande à lobes | Orange | [GLB](assets/lobee-orange/lobee-orange.glb) | [Blender](assets/lobee-orange/lobee-orange.blend) | [PNG](assets/lobee-orange/lobee-orange-preview.png) |
| Grande à lobes | Vert | [GLB](assets/lobee-vert/lobee-vert.glb) | [Blender](assets/lobee-vert/lobee-vert.blend) | [PNG](assets/lobee-vert/lobee-vert-preview.png) |

## Matières et animation

- Base Color, Roughness et Emission du liquide : **2048 × 2048**.
- Deux cartes de volutes RGBA à bords doux : **512 × 512**.
- Budget de **25 000 triangles** par modèle ; valeurs exactes dans le manifeste.
  La mare d'origine compte 18 172 triangles. Six matériaux et cinq images incorporées par GLB.
- Rubans croisés courbés en 3D, transparence douce et émission mesurée.
- Animation intégrée **ManaFlow**, boucle de **6 secondes**. Le nombre de
  volutes et de gouttes est adapté à la forme ; la mare d'origine anime quinze objets.
- Les retours au sol se font à échelle invisible, avec phases décalées.
- Les couleurs restent lisibles sans bloom. Les reflets adoucis évitent un
  aspect métallique blanc dans l'environnement neutre du viewer.

Les PNG sources sont dans [textures](textures/), également incorporés aux
GLB et aux fichiers Blender. Chaque variante possède un aperçu 1100 × 1100
et deux poses supplémentaires aux images 10 et 100. Les sols, éclairages
et caméras des aperçus sont conservés dans Blender et exclus des GLB.

## Reproduction et contrôle

Le [générateur des deux formes supplémentaires](scripts/generate_mana_shapes.py)
et le [générateur d'origine](scripts/generate_mana_pool.py) sont conservés
comme sources historiques ; la validation artistique actuelle est portée
par le manifeste. Toute reproduction doit se faire dans une copie isolée
du dossier, avec ses dépendances, afin de préserver les fichiers figés.
Ne pas exécuter les générateurs directement sur les références validées.

Dans cette copie, `-- --shape allongee --palette bleu` cible une variante du
générateur des formes supplémentaires ; les autres choix sont `lobee`,
`orange` et `vert`. `-- --draft` produit un aperçu rapide.
Le [script de contrôle](scripts/validate_mana_pool.py) et le
[rapport de validation](validation.json)
contrôlent les géométries finies et identiques entre palettes, les textures
incorporées, le budget, les matériaux alpha, la durée, la continuité de boucle
et la préservation des fichiers approuvés.

Ces ressources de DA sont conservées hors du client Unity. L'intégration URP,
les LOD et un éventuel effet de gameplay ne font pas partie de cette livraison.
