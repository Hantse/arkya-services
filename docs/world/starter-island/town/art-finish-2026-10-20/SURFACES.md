# Matières peintes d’Aubefont — 20 octobre 2026

Cette passe rapproche les valeurs des pavés, du chaume, du bois et de la pierre,
en conservant leurs textures et leurs formes. Elle reprend les couleurs
assourdies et les variations amples de la référence arbre peint V3. La revue
visuelle finale se fait avec les captures communes de la passe globale.

Les pavés conservent tous leurs pigments par sommet, y compris la mousse et les
inclusions colorées. Leur saturation et leur contraste sont réduits ; le fond
des joints devient un peu plus clair. Aucun pavé n’est ajouté, déplacé ou
régénéré par ce helper. Le chaume reçoit un ton miel vieilli moins jaune, un
filtrage légèrement plus doux des stries et de larges nuances de pigment.
Le bois et les pierres gardent leur dessin avec des écarts de valeur plus
modérés. Les enduits conservent les couleurs propres à chaque maison.

La [correction après revue des captures](surfaces-pigment-refinement.json)
réchauffe le chaume devenu trop beige et rétablit davantage de variation dans
les pavés. Elle touche uniquement les deux matériaux dérivés : saturation du
chaume à 0,80 et mélange de pigment miel à 0,22 ; saturation des pavés à 0,85,
mélange à 0,10 et relief à 0,010 m. Elle ne crée aucun asset supplémentaire et
conserve les textures, les matériaux sources, le terrain et les collisions.

`RefineTownSurfaces.cs` crée **29 matériaux dérivés**, affectés à **648 renderers**
actifs de la ville. Les matériaux d’origine, leurs textures, les modèles
importés et les instances situées hors de la ville restent inchangés. Les
dérivés se trouvent sous
`Assets/Arkya/World/StarterIsland/Town/ArtFinish20261020/Surfaces/`.

Le shader `PaintedTownFinish.shader` dérive de `PaintedVillageGround.shader`.
Il conserve les passes URP d’éclairage, d’ombres, de profondeur et de normales,
le brouillard, l’instancing et le mouillage cosmétique. Il ajoute un réglage de
saturation, un mélange de pigment, un biais de mip doux et une variation
spatiale de faible amplitude. Les textures originales restent référencées ;
aucune image source n’est remplacée. Les normales détaillées des enduits ne
sont plus utilisées : leur éclairage repose sur les normales de géométrie.
Le relief alpha existant des pierres reste actif, avec une amplitude réduite.
Comme son shader source, ce shader vise l’éclairage temps réel actuel et ne
contient pas de passe Meta pour de futurs lightmaps.

La prairie est modifiée uniquement dans un rayon de **49 m autour de
X550/Z520**, avec une transition entre 41 et 49 m. Une partie des poids
`SageMeadowV4` et `CoolMeadowV4` rejoint `MeadowGlazeV4`, dont la texture est
répétée à une plus grande échelle. Les textures et les huit couches de terrain
existantes sont conservées. Il n’y a aucune exclusion liée à l’ancienne
position de la mairie, ni dépendance aux positions des bâtiments.

Le [rapport d’exécution](surfaces-authoring.json) confirme :

- compilation C# et shader sans erreur ;
- 13 369 texels de prairie retouchés dans une fenêtre de 132 × 132 ;
- poids terre, chemins, roche et sable strictement identiques, écart maximal 0 ;
- somme des trois couches prairie conservée à la quantification native près,
  écart maximal 0,003921747, environ 1/255 ;
- empreintes du relief et de toute la peinture hors fenêtre inchangées ;
- géométrie, transformations, collisions, états des renderers et lumières
  inchangés ;
- aucun triangle, renderer, slot de matériau ou composant runtime ajouté.

Lors de l’authoring, le helper sauvegarde ses assets dérivés et le TerrainData,
puis laisse la scène modifiée pour la sauvegarde coordonnée. Cette sauvegarde
finale est désormais effectuée : voir la [synthèse de la passe](README.md) et
la [validation finale, 19 contrôles réussis](validation.json).
Le helper refuse une deuxième application sur le même dossier. En cas
d’échec, il restaure les affectations et la fenêtre de peinture avant de
supprimer uniquement ses dérivés.
