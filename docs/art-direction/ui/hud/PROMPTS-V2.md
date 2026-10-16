# Prompts — HUD V2 allégé

16 octobre 2026. Outil intégré `image_gen`, édition d'images, fond opaque. Les images cibles ont été inspectées avant édition. Les étapes A–C sont successives ; D et E déclinent la même base finale. Les sorties intermédiaires A et B ne sont pas des livrables retenus.

## A — Allègement de la disposition

Cible : 01-encre-cuivre.png (V1) ; référence de reconstruction : capture forest-day.png.

```text
Use case: precise-object-edit / ui-mockup.
Revise input image 1, the Encre & Cuivre full game HUD for Arkya, to give the player MUCH MORE VISIBLE GAME SPACE while supporting more action bars.
Image 1 is the edit target. Image 2 is the identical environment without a HUD; use it ONLY as a reconstruction reference where old UI is removed. Preserve input 1's landscape framing, precise trees, sky, path, avatar, boar, target ring and gameplay moment. Do NOT change camera, crop, zoom, world, avatar, boar or lighting. No new scene. Same landscape image dimensions and aspect ratio.

IMPORTANT: this is a major layout simplification. Remove the existing large HUD and rebuild a much more compact one at the following sizes. Reconstruct all removed old-frame areas as the visible original world. The game view must feel significantly more open, especially all middle side edges. Preserve painted fantasy detail and readable type: reduce decorative panel area rather than making all text tiny.

STYLE: Encre & Cuivre, dark ink #192C30, thin patinated copper trim, ivory text, occasional jade details. Keep trim to a very thin line and tiny corner joins. No large outer plates, no ornamental diamonds around every widget. The compact panels keep the handmade painted quality, not a flat sci-fi HUD.

LAYOUT in percentage of image width/height:
1. PLAYER, top-left, within x1.5–18%, y2–9% ONLY. Small ~52px portrait, tiny level 5 badge, name "Aëlan", red HP "840 / 1000", ochre stamina "72 / 100". Thin two bars with almost no enclosing padding. Two 24px buff icons just beneath, small labels "2 min" and "45 s". This must occupy roughly half the original width and height, not the original giant frame.
2. PARTY below at x1.5–13%, y15–26%. Three thin rows named "Lyra", "Borin", "Néa", each a small red health strip and level "5". NO portraits, NO outer panel, NO large "GROUPE" title, NO cards. Ivory lettering with a fine dark glyph shadow. Background world visible between and around rows.
3. TARGET top center x42–58%, y2–8%, just "Sanglier · 4" and one thin red health meter "360 / 480". REMOVE the large boar portrait and wide target plate. A slim understated ink backing only immediately behind the meter/title.
4. MINIMAP top right centered around x94%, y10%, diameter about 10% of image width (roughly 150px at 1600px; much smaller than original). Name "Bois des Murmures" above, north N, tiny sun and plus/minus controls attached closely. Preserve green woodland map and route. Crucial: attach TWO small functional buttons directly to its lower rim, outside the map circle, labelled exactly "Map  M" and "Quest  J". Small map and journal symbols acceptable beside the labels. Both buttons MUST be clearly visible, legible, integrated with minimap. No map/journal duplicates at bottom right.
5. QUEST TRACKER below minimap around x83–98%, y26–39%. TEXT ONLY FLOATING ON WORLD: no background, no panel, no plate, no box, no parchment, no border, no frame, no horizontal ruler, no dark gradient rectangle. This is the user's explicit requirement. Readable ivory text in all themes with modest dark shadow/outline ON THE GLYPHS ONLY. Use just four lines and small objective bullets:
"Le sentier du refuge"
"Rejoindre le refuge"
"Les bois s'agitent"
"Sangliers : 2 / 5"
No large "QUÊTES" header.
6. CHAT bottom left x1.5–22%, y88–96%. Only three readable text lines, no permanent input box, no tabs, no enclosing decorative frame. White text with dark glyph shadow, green loot text, maybe a tiny speech symbol. Lines:
"Lyra : On suit le sentier ?"
"Borin : Je vous rejoins."
"Bois × 3"
The old large framed chat is completely removed. World remains visible behind letters.
7. ACTION BARS: EXACTLY TWO separate horizontal rows of TWELVE small square slots each, 24 TOTAL. Bottom center x32.5–67.5%, y88–97%, narrow total footprint about 35% of screen width and 9% height. Each slot about 42px at this image size with 3px spacing; do not use the original 80px slots. Both rows fully visible and consistently aligned with twelve columns. NO large panel surrounding bars and NO "Actions" tab heading. Compact individually framed painted icons, 12 top and 12 bottom, 4 empty slots at the end of the upper row to demonstrate room for more abilities. Lower/main row includes sword, shield, fire, leaf, pickaxe, potion, sprint, crystal and other distinct fantasy abilities; top row contains utility abilities/consumables. Readable abbreviated key labels, main row 1 to 9 then 0 and two small auxiliary key labels; upper row uses shift modifier indicators. Exact key mapping is illustrative. One cooldown reads "4 s", potion count "3". Red life, ochre stamina, golden XP keep their meanings.
8. EXPERIENCE: a very thin gold line beneath the two bars at y98%, only small "Niv. 5 · 62 %" label, no large plaque.
9. BOTTOM RIGHT: ONLY one small bag icon button "I", about 40px, tucked into x95–98%, y93–97%. Remove the large bag plate and its weight/capacity display. REMOVE the map button, journal button, settings gear and ALL "Échap" buttons from this bottom area. Absolutely no escape/settings icon anywhere on HUD. Removed space must show the world.
10. Tiny unobtrusive watermark at lower edge: "HUD V2 — Encre & cuivre".

Preserve these priorities in order: (a) two small 12-slot bars, (b) visibly open game scene, (c) Map M and Quest J ON minimap rim, (d) quest tracker with absolutely no background or border, (e) no Escape/settings button, (f) small readable player/target/group/chat.
Do not simply shrink the old UI uniformly. Explicitly remove entire background surfaces for quests, party and chat and simplify structure. Keep all center screen from x20–80%, y15–85% free of UI. Maintain high quality painted game icon art. Single full-screen game mockup, no side comparisons, no big labels or arrows.
```

## B — Nettoyage du suivi de quêtes

Cible : sortie A ; référence : capture forest-day.png. Cette passe corrige le fond du suivi.

```text
Use case: precise-object-edit.
Input image 1 is the EDIT TARGET: the newly revised compact ARKYA HUD V2. Input image 2 is only the clean original forest to show the environment behind the UI.
Make TWO very targeted corrections, keep EVERYTHING ELSE pixel-identical including the player, boar, scenery, camera, lighting, minimap, party, chat, TWO rows of twelve action slots, all text, every icon, every position and size.
1. Under the minimap, the quest tracker still appears to have a faint dark teal rectangular backing behind its text. REMOVE ALL SUCH BACKING completely. In the approximate region x84%-99%, y27%-41%, restore the actual painted tree bark and forest seen in input2 behind the text. The FOUR quest text lines and two diamond markers must float directly on the natural environment, with NO background shape of ANY opacity, NO gradient, NO blurry dark rectangle, NO border, NO shadow of a panel. ONLY a thin dark outline/shadow attached tightly to individual text glyphs may remain. It is essential that the underlying bark texture runs continuously through the gaps between lines, exactly as outside the text region. Retain exact labels "Le sentier du refuge", "Rejoindre le refuge", "Les bois s'agitent", "Sangliers : 2 / 5".
2. On the minimap's "Map  M" button, change its tiny book symbol to a tiny folded MAP symbol. The adjacent "Quest  J" retains its book. Keep these two buttons at their same positions and sizes.
Do not change any other pixels or re-layout anything. Output the same single landscape game screen, not a comparison.
```

## C — Précisions du groupe et des icônes

Cible : sortie B ; référence d'identité des avatars : 01-encre-cuivre.png (V1). Cette sortie devient 01-encre-cuivre-v2.png.

```text
Use case: precise-object-edit.
INPUT 1 is the latest revised compact ARKYA HUD V2 (edit target). INPUT 2 is the original larger HUD for portrait identity reference ONLY.
User corrections: the group still needs AVATARS, name, small life/mana bars and tiny buffs, but NO enclosing frame. Map and Quest buttons must be ICON ONLY.

Edit only these two areas. Preserve absolutely everything else from INPUT1 including the exact avatar in the world, boar, scenery, camera, player frame, target, minimap circle, quest text with no panel, chat, 24 spell slots in two rows of twelve, XP, bag I, all dimensions and placements outside those two areas.

A. GROUP: in the SAME LEFT position as now (roughly x1.5%-13%, y19%-34%), rebuild the three member rows slightly smaller and visually light. Three members stacked vertically, no outer box, no panel, no portrait frame, no border around whole row, no background card, no heading.
Each row:
- a small clean CIRCULAR-CROPPED PORTRAIT about 32px, with NO ornamental outline/frame. Use the exact character appearance of Lyra, Borin, Néa from input2 (group portrait reference).
- the member's name next to the portrait (Lyra then Borin then Néa).
- TWO fine strips below the name, a red health bar and a blue mana bar, each about 100-115px long and 5-6px tall. Tiny useful numbers optional, not mandatory. No big numerical level circle.
- TWO TINY buff icons of about 12px next to the gauges, immediately to their right. A small leaf/shield/fire/etc icon is sufficient.
Maximum footprint of each entire row about 185px wide and 42px high at this image size, 8px vertical separation. Background world fully visible around portraits, text, gauges and buffs, and between the three rows.
Names ivory with fine shadow attached to glyphs only. Bars have a thin dark track to read but no common enclosing plate. Every member must visibly have portrait + name + two gauges + two tiny buffs. Keep the group on the left, do not move elsewhere.

B. MINIMAP BUTTONS: replace the current wide rectangular labelled "Map M" and "Quest J" controls beneath the minimap with TWO small ~32px square icon-only controls nestled against its lower rim.
- First icon must be a clearly recognizable FOLDED MAP (a three-fold piece of parchment with a green route), NOT a book.
- Second icon must be a JOURNAL/QUEST BOOK with a small quill or bookmark.
- No words, NO "Map", NO "Quest", no M or J letters on these buttons. ONLY the two distinct symbols, slim matching ink/copper rims. No extra buttons.
Reconstruct the world in the space freed by removing the wide text buttons. Do not change minimap size or position.
Keep the four floating quest text lines below them unchanged and ensure they have ZERO panel/background/border; allow the natural forest and tree textures to show between letters. All remaining UI unchanged. Single finished game mockup, no extra explanations or arrows.
```

## D — Carnet d'exploration

Cible : 01-encre-cuivre-v2.png ; référence de matière : ../ui-directions-01.png.

```text
Use case: precise-object-edit / ui-mockup.
Input image 1 is the final compact HUD V2 edit target. Input image 2 is only the original three-column style reference. Change ONLY the material theme, not the layout. Keep the exact camera, world, trees, avatar, boar, light, all HUD positions/sizes, all portrait content, ALL 24 spell slots (two rows of twelve), every icon, shortcut, cooldown, numeric value, text and minimap pixel content identical.
Crucial invariants: TWO small spell bars; three compact stacked group rows at the SAME left position, each with a small unframed portrait, ivory name, red health strip, blue mana strip and two tiny buff icons, WITHOUT any enclosing panel or frame; three chat text lines with no panel; small bag I alone in bottom right; NO Escape button or gear anywhere; TWO ICON-ONLY buttons immediately under the minimap: folded-map symbol and quest-journal symbol. Absolutely NO Map/Quest words, NO M/J letters or other visible text on these buttons.
The quest tracker is ONLY four lines of ivory text plus two diamonds drawn over the game world. DO NOT ADD ANY BACKGROUND OR BORDER to the quest tracker. NO parchment behind quests even in the parchment theme. NO dark wash, rectangle, plate, framed box, separator or background gradient behind quests. Keep continuous untouched tree/foliage pixels between letters, with small glyph outline only. Chat and party also keep their open world backgrounds and ivory readable text. Only the remaining genuinely framed UI parts get theme materials.
Red health, BLUE GROUP MANA, ochre player stamina and gold XP remain unchanged. Same composed single game scene at same aspect ratio and resolution. Keep icons in dark inset slots for contrast, never recolor the ability art. No further alterations.
THEME: Carnet d'exploration from column2 of reference. Thin honey-colored wood frames with a few small leather binding joins; limited parchment backing ONLY behind the existing small vital frames, minimap caption/buttons and bag/hotbar inset labels where appropriate. Dark forest text on those light surfaces only. No large new paper surfaces; no new enclosing panel around either bar. The quest/chat/group text stays ivory, not dark green, because it is on the bare world. Keep 24 slot counts exact, existing 4 empty top-row slots stay empty. Change tiny footer to "HUD V2 — Carnet d'exploration".
```

## E — Pierre de mana

Cible : 01-encre-cuivre-v2.png ; référence de matière : ../ui-directions-01.png.

```text
Use case: precise-object-edit / ui-mockup.
Input image 1 is the final compact HUD V2 edit target. Input image 2 is only the original three-column style reference. Change ONLY the material theme, not the layout. Keep the exact camera, world, trees, avatar, boar, light, all HUD positions/sizes, all portrait content, ALL 24 spell slots (two rows of twelve), every icon, shortcut, cooldown, numeric value, text and minimap pixel content identical.
Crucial invariants: TWO small spell bars; three compact stacked group rows at the SAME left position, each with a small unframed portrait, ivory name, red health strip, blue mana strip and two tiny buff icons, WITHOUT any enclosing panel or frame; three chat text lines with no panel; small bag I alone in bottom right; NO Escape button or gear anywhere; TWO ICON-ONLY buttons immediately under the minimap: folded-map symbol and quest-journal symbol. Absolutely NO Map/Quest words, NO M/J letters or other visible text on these buttons.
The quest tracker is ONLY four lines of ivory text plus two diamonds drawn over the game world. DO NOT ADD ANY BACKGROUND OR BORDER to the quest tracker. NO parchment behind quests even in the parchment theme. NO dark wash, rectangle, plate, framed box, separator or background gradient behind quests. Keep continuous untouched tree/foliage pixels between letters, with small glyph outline only. Chat and party also keep their open world backgrounds and ivory readable text. Only the remaining genuinely framed UI parts get theme materials.
Red health, BLUE GROUP MANA, ochre player stamina and gold XP remain unchanged. Same composed single game scene at same aspect ratio and resolution. Keep icons in dark inset slots for contrast, never recolor the ability art. No further alterations.
THEME: Pierre de mana from column3 of reference. Fine violet-blue slate frames, compact blue-gray slate insets, pale silver text, a very small crystal accent on a couple of existing corners only. Mild stone texture and tiny etched amber marks. No oversized crystal ornaments, no broad glow, no neon. Absolutely no new enclosing panel around group, chat, quests or action bars. Retain the exactly twenty-four small slots including the four empty upper slots. Change tiny footer to "HUD V2 — Pierre de mana".
```

## F — Couleur fonctionnelle de stamina

Passe ciblée appliquée séparément aux sorties D et E : seule la jauge de stamina du joueur doit retrouver l'ocre de la base C. Les jauges de mana du groupe restent bleues. Les sorties corrigées sont les fichiers V2 finaux Carnet et Pierre.

```text
Use case: precise-object-edit.
Input1 is the final HUD theme mockup to correct; input2 is the ink/copper HUD as the correct gauge COLOR reference only.
Make exactly ONE small pixel-region color correction: the PLAYER'S SECOND GAUGE in the UPPER LEFT, directly beneath the red "840 / 1000" gauge and labelled "72 / 100", is incorrectly blue in input1. Recolor ONLY THAT BAR'S BLUE FILL to the warm ochre/golden color of the same "72 / 100" bar in input2 (approximately #CDA13C). It is stamina, not mana.
Keep the three GROUP members' mana bars BLUE. Keep all health bars RED. Keep every frame, icon, portrait, text glyph, number, minimap, world, tree, player character, boar, lighting, crop, 24 spell slots and location pixel-identical to input1. No global palette adjustment, no other retouching. Do NOT change the theme materials. Preserve the borderless quest tracker and icon-only map/journal buttons. No text added. Output exactly the same full image, with only the one player bar's fill corrected.
```


