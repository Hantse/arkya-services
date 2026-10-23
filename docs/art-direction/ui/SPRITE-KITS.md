# Kits graphiques UI intégrés

18 octobre 2026. Production avec le skill `imagegen` et l’outil intégré `image_gen`, fond transparent. Les PNG originaux sont conservés dans `Unity3D/Arkya/Assets/Arkya/Characters/Runtime/UI/Art/Resources/ArkyaUI/` : `ink-copper.png`, `explorer-journal.png`, `mana-stone.png` (1774 × 887 pixels chacun). Aucun montage des maquettes V3 n’est affiché à la place de l’interface.

Chaque PNG est importé en huit sprites : `slot`, `ring`, `plaque`, `panel`, `map`, `journal`, `bag`, `button`. Le découpage utilise les pixels alpha d’origine ; aucun pixel n’est retouché. Les rectangles et les bordures du découpage en neuf zones sont écrits par les fournisseurs de données du Sprite Editor et conservés dans les `.meta`. Le script reproductible est `Unity3D/Arkya/AgentScripts/UI/ImportInterfaceSkins.cs` (exécution dans une copie isolée du projet, voir son commentaire initial).

`InterfaceSkin` recopie explicitement les bordures des sprites vers les propriétés de découpage UI Toolkit. Les coins des panneaux restent ainsi de taille constante. Les mipmaps et le filtrage trilinéaire évitent le scintillement des détails quand les illustrations sources sont affichées en petits boutons.

Ces trois kits restent inchangés pour la création, les fenêtres et les thèmes Carnet d'exploration et Pierre de mana. Pour le HUD Encre & cuivre, `ReferenceHudChrome` remplace leurs anneaux, cases, jauges et cartouche de zone par des cadres natifs fins, sans gemmes ni rivets décoratifs, conformément à la V3 validée. Les anciens pictogrammes restent présents dans les PNG sources ; le HUD utilise désormais les illustrations `HudV3` décrites ci-dessous. Les cadres des ressources et les cases gardent un intérieur sombre, y compris avec le Carnet.

Les titres des écrans de création et des fenêtres utilisent **Cormorant Garamond SemiBold**, source [Cormorant](https://github.com/CatharsisFonts/Cormorant/blob/master/fonts/ttf/CormorantGaramond-SemiBold.ttf), licence SIL Open Font License 1.1 fournie dans `Art/Fonts/OFL.txt`. Le HUD utilise **Gelasio Bold**, issue du dépôt officiel [SorkinType/Gelasio](https://github.com/SorkinType/Gelasio), avec sa licence SIL OFL 1.1 dans `Art/Fonts/Gelasio-OFL.txt`. Le corps des formulaires conserve la police Unity déjà utilisée. Aucune de ces fontes n'est une copie de police système.

## Feuilles de pictogrammes HUD V3

Les nouveaux PNG ont été générés avec l'outil intégré `image_gen`, à partir de la première image fournie par l'utilisateur pour comparer la réalisation à la [V3 Encre & cuivre approuvée](hud/01-encre-cuivre-v3.png). Ils sont conservés sans retouche des pixels dans `Unity3D/Arkya/Assets/Arkya/Characters/Runtime/UI/Art/Resources/HudV3/`. La référence sert à produire des objets isolés ; aucune capture complète n'est plaquée sur le jeu.

| Feuille | Dimensions obtenues | Grille | Noms des sprites, de gauche à droite puis de haut en bas |
| --- | --- | --- | --- |
| `objects.png` | 1774 × 887 | 4 × 2 | `map`, `journal`, `bag`, `wood`, `sun`, `chat`, `quest`, `arrow` |
| `actions.png` | 1402 × 1122 | 5 × 4 | `heal`, `eye`, `vortex`, `paw`, `horn`, `lantern`, `bread`, `sword`, `shield`, `fire`, `leaf`, `pickaxe`, `red-potion`, `run`, `crystal`, `star`, `bow`, `green-potion`, `shield-buff`, `spiral-buff` |
| `portraits.png` | 2172 × 724 | 3 × 1 | `lyra`, `borin`, `nea` |

`AgentScripts/UI/ImportHudV3.cs`, méthode `Begin`, prépare les **31 sprites** dans la copie isolée du projet. Chaque cellule est bornée par son alpha supérieur à 32, avec deux pixels de marge ; les coordonnées proviennent du PNG original. L'importeur vérifie les capacités de modification du Sprite Editor, écrit les correspondances nom/identifiant, conserve les identifiants existants, vérifie les dimensions et le SHA-256 des PNG, puis produit un manifeste. Les textures sont non compressées, limitées à 4096 pixels, avec alpha transparent, mipmaps et filtrage trilinéaire. Les `.meta` proviennent de cette exécution Unity ; ils ne sont pas découpés à la main. L'import des trois feuilles et des 31 noms a réussi dans Unity 6000.6.6f1 ; le [manifeste](unity-review/hud-v3-import.json) conserve les rectangles, identifiants et SHA-256. Les PNG d'origine sont inchangés.

`HudV3Art.Find(key)` partage les sprites importés sans recadrage ni création de textures à l'exécution. `HudV3Art.Apply(root)` installe la carte, le journal et la bourse communs aux trois thèmes ; les contenus du HUD résolvent les autres clés au besoin. Le portrait du joueur reste le rendu de son apparence réelle. Les trois portraits illustrés sont les références de groupe de la fixture Editor.

### Résumé des consignes de génération V3

Ces gabarits résument les consignes utilisées ; ils ne prétendent pas reproduire les prompts mot pour mot.

```text
Shared: use the approved Encre & cuivre HUD image as the visual reference. Produce only isolated painted game UI artwork, front-facing, on a truly transparent background. No scene, labels, letters, numbers, frames, copper rims, gems or added ornament. One object per cell with transparent gutters; preserve recognizable silhouettes at small HUD size.

Objects: exactly 4 equal columns × 2 equal rows. Top row: folded parchment map; open cream-page journal with quill; simple brown drawstring pouch; single wood log. Bottom row: sun; three-dot chat bubble; golden quest diamond; cream player-direction arrow. The journal stays open and the bag stays a simple pouch.

Actions: exactly 5 equal columns × 4 equal rows. Row 1: green healing cross, blue eye, violet vortex, golden paw, horn. Row 2: lantern, bread, sword, shield, fire. Row 3: leaves, pickaxe, red potion, blue running silhouette, blue crystal. Row 4: golden star, bow, green potion, shield buff, blue spiral buff. Bright painted symbols without slot backgrounds.

Portraits: exactly 3 equal columns × 1 row. Lyra, dark-haired human woman; Borin, brown-haired bearded human man; Néa, pale blonde elf woman. Head-and-shoulders portraits, no circular borders, no badges, no names; transparent background and equal visual scale.
```

## Prompts des kits antérieurs

Les prompts exacts ci-dessous documentent la génération des trois kits conservés. Leurs ornements ne constituent plus la consigne pour le HUD Encre & cuivre corrigé.

## Prompt commun exact

```text
Use case: stylized-concept. Production game UI sprite atlas for Unity, not a mockup screenshot. Exactly 4 equal columns and 2 equal rows on a 1536 by 768 pixel transparent canvas; 8 isolated sprites, one sprite centered in each 384x384 cell, with at least 16px transparent padding at every cell edge, absolutely no overlaps or labels. Orthographic front-facing painted fantasy MMORPG interface matching the reference: tactile bevels, carefully painted edge light, restrained surface grain, readable at 48px. NO text, letters, numbers, watermarks, scenery or characters.
Top row left to right: 1 empty square action slot 320x320px outer size, dark recessed center and slim handcrafted rim, corner details; 2 circular minimap rim outer diameter340px, inner diameter300px, center truly transparent with NO fill, no compass; 3 wide horizontal blank title plaque 340x100px centered with calm interior, shaped ends; 4 square UI panel 340x340px, filled calm surface and thin decorative bevel rim concentrated in the outer32px, corners suitable nine slicing.
Bottom row left to right: 5 painted folded paper map icon 230x230px, cutout without frame; 6 painted closed tan leather travel journal with folded corner 230x230px, cutout without frame; 7 warm leather drawstring adventurer satchel 230x230px, cutout without frame; 8 wide blank horizontal button 340x100px centered with subtle inset face and rim suitable nine slicing. Map/book/bag natural warm painted colors consistent across themes. Fully transparent background outside sprites and inside circular ring. NO drop shadows extending into other cells.
```

## Ajouts de thème exacts

### Encre & cuivre

```text
Encre & cuivre theme. Rich dark teal ink #192C30 surfaces, fine softly worn copper #B78B59 rims, small jade #81B8A4 highlights, ivory tiny etched detail. Fine warm metallic light, elegant thin rounded handcrafted corners. This is the dark teal and fine copper style from reference image1. Avoid filigree or heavy ornate decoration.
```

### Carnet d’exploration

```text
Carnet d’exploration theme. Warm parchment #E8D8B5 panel and title/button surfaces, honey wood #8E6542 thick painted rims and small leather straps at corners and ring quarters, moss #6F855B stitching. Action slot center remains very dark forest #182D2B to support bright icons. The ring is carved honey wood with visible leather bindings. Match reference image2. Tactile travel journal, restrained texture, no heavy ornate decoration.
```

### Pierre de mana

```text
Pierre de mana theme. Dark blue slate #202A40 panel/button/action surfaces, sculpted violet stone #6C668A bevel rim, thin restrained crystal blue #76B8D7 edge light, tiny amber #D4AE6D accents. Square slots have clipped angular corners and stone facets; circular minimap rim subtly faceted. Match reference image3. NO swirling magic or large glows, no heavy ornate decoration.
```

Les références désignent les trois [concepts V3](hud/README.md) inspectés avant génération. Les mentions de grille/taille sont des consignes de génération : l’importeur utilise les dimensions et contours effectivement obtenus, sans supposer une grille parfaite.
