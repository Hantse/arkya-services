# Prompts — HUD complet, trois thèmes

16 octobre 2026. Outil intégré `image_gen`, trois appels, fonds opaques.

## 01 — Encre & cuivre

Entrées : capture `docs/world/starter-island/environment/forest-day.png` (cible), planche `../ui-directions-01.png` (style), capture `Unity3D/Arkya/Assets/Screenshots/Characters/creation-active.png` (apparence). Les trois images ont été inspectées avant l'appel.

```text
Use case: compositing / ui-mockup.
Create a polished full-screen in-game MMORPG HUD mockup for ARKYA. Output ONE full game view in landscape 16:10 matching the environment reference proportions, preferably 1920x1200 or higher. This is a visual concept on a real current-game background, not a different invented environment.

Input images and roles:
1. forest-day.png is the EDIT TARGET and immutable environment backdrop. Preserve its exact camera view, tree geometry, painted bark, grass, path, colors and daytime lighting. Do not improve, repaint, redesign or replace the forest; retain its specific recognizable tree on the right, sky and ground.
2. ui-directions-01.png is a UI STYLE REFERENCE ONLY. Use column 1 ENCRE & CUIVRE for the entire HUD. Do not reproduce the comparison board, inventory window, headers or three columns.
3. creation-active.png is a CHARACTER APPEARANCE REFERENCE ONLY. Use the central human's cream sculpted armor, sage-green cloth, brown short hair and stylized proportions. Do not copy its screen UI.

Composite one modestly sized full-body human player avatar from input3, viewed from behind, on the path near lower middle (center x50%, feet y79%, head y56%), correctly lit and grounded. Composite one modest brown stylized wild boar farther along the path just right of the character (x59%, y52%), as a target. Preserve all other scenery. These additions are illustrative gameplay context. The world must remain clearly visible across the majority of the screen.

HUD visual language: same material quality as the approved ENCRE & CUIVRE sample. Dark blue-green ink/leather panels #192C30, fine patinated copper trims #B78B59, ivory text #F1E4CB, sparse jade selection accent #81B8A4. Slim crafted corners, soft brush-painted surfaces, deliberate highlights, no ornate giant scrollwork. Crisp painterly action icons. Panels opaque behind text, subtly fading edges for chat only. Use clean compact sans serif for numerical data and quest/chat text; restrained fantasy serif only for main labels. UI must feel usable at gameplay scale, not large decorative artboard components.

FIXED HUD GEOMETRY and content to preserve for later theme variants:
- Top-left x2–22%, y3–15%: player frame with portrait, name "Aëlan", level badge "5", red health bar with "840 / 1000", ochre stamina bar with "72 / 100". Two small buff icons immediately below, labelled "2 min" and "45 s".
- Left x2–17%, y20–34%: three COMPACT party frames under small title "GROUPE": "Lyra", "Borin", "Néa"; level "5" for each, red health bars. Frames low height, not massive cards.
- Top-center x38–62%, y3–10%: compact selected-target frame, label "Sanglier", level "4", red bar "360 / 480". Small target ring underneath the boar in the world, not a large overhead nameplate.
- Top-right x83–98%, y3–25%: round restrained map showing green woodland and one tan path with a triangular player marker, north "N", small sun icon. Caption "Bois des Murmures". No invented cities.
- Right x80–98%, y30–49%: compact tracked quests block titled "QUÊTES". Exact sparse content, each on its own readable line:
"Le sentier du refuge"
"Rejoindre le refuge"
"Les bois s'agitent"
"Sangliers : 2 / 5"
Use a small hollow diamond and filled diamond to indicate objective types. Typography very readable, not a broad parchment occupying half the screen.
- Bottom-left x2–24%, y76–94%: chat module with tabs "Local", "Groupe", "Combat". Three readable short lines:
"Lyra : On suit le sentier ?"
"Borin : Je vous rejoins."
"Vous avez reçu : Bois × 3"
Below, unobtrusive field "Entrée pour discuter". Keep dark text ground here.
- Bottom-center x29–73%, y85–94%: a single neatly aligned row of EIGHT square action slots with chunky painted icons: sword slash, shield, fire, leaf heal, pickaxe, potion, sprint, crystal. Exact sequential key labels "1", "2", "3", "4", "5", "6", "7", "8". Leaf-heal slot has a dark cooldown overlay and numeral "4 s"; potion has small count "3". Narrow frame, enough separation to read keys. Above the bar small skill-category caption "Actions". Do not invent second or third hotbars.
- Bottom-right x82–98%, y87–94%: four compact utility icons, bag, journal, map, cog, with exact key captions "I", "J", "M", "Échap". Above bag show "24 / 150" and small weight "18 / 40 kg".
- Along bottom x29–73%, y96–97%: very slim experience meter with label "Niveau 5 · 62 %".
- Large clear central play region: no inventory/equipment window, no modal settings panel, no giant decorative logo.
- Small discreet concept marker at bottom left edge y98% reading "Concept HUD — Encre & cuivre". This is the only non-game label.

Consistency constraints: health is RED, stamina OCHRE, XP muted GOLD, same painted icons and exact numerical/text content independent of UI theme. No connection debugging labels, no FPS statistics, no browser chrome. Do not copy another game's logos. No fantasy landscape upgrade; fidelity to supplied environment matters. Match positions strictly so this image becomes the master for two theme swaps. All three components should look detailed but restrained.
```

## 02 — Carnet d'exploration

Entrées : `01-encre-cuivre.png` (cible) et `../ui-directions-01.png` (style).

```text
Use case: precise-object-edit / ui-mockup.
Edit input image 1, the full ARKYA HUD screenshot concept. Input image 2 is only the three-column MATERIAL STYLE REFERENCE.
Keep image 1's exact aspect ratio, pixel dimensions, camera, landscape, sky, every tree and leaf silhouette, ground and path, player character, boar, positions and scale unchanged. This is a UI material theme switch of the SAME moment in the same game.
Keep ALL HUD bounding boxes, sizes, placements, portraits, target, minimap terrain and markers, exact text, numbers, fonts, text sizes, icons, progress lengths, cooldown masks, shortcuts, group members, quest content, chat messages identical. Do not add, delete, enlarge, rearrange or hide any HUD element. Health stays red, stamina ochre and XP gold. Action icons stay the same artwork and colors. Keep the original game environment pixels untouched outside UI regions.
Only change the decorative HUD materials, borders, background fills, ornament details and text colors to the specified theme. No collage or title board, no split screen, no new window. Output a single full game view. Leave the central play region exactly as supplied. Be precise and legible.
TARGET THEME: 02 — CARNET D'EXPLORATION, matching COLUMN 2 in reference image 2.
Replace dark panel fills with quiet warm parchment #E8D8B5, fine muted honey wood rims #8E6542, tiny leather binding corner joins, forest green #294C3A labels and main text. Friendly hand-crafted painted fantasy journal, mature and restrained. Frames softly worked but same geometry and thickness; no giant curled scrolls. Gentle aged-paper edge texture, nearly flat text grounds. Selected tab forest green with cream text. Keep chat readable using a medium parchment panel and deep forest text; recolor chat status green darker for adequate contrast. Minimap gets matching wood-and-parchment rim while its map pixels stay unchanged. Action slots retain their painted icon art on dark inset backings for readability; convert only their copper borders to wood and parchment. Portraits, boar portrait and all action icons identical. Red health / ochre stamina bars remain consistent with original. Update the tiny lower-left marker only to exact text "Concept HUD — Carnet d'exploration".
```

## 03 — Pierre de mana

Entrées : `01-encre-cuivre.png` (cible) et `../ui-directions-01.png` (style).

```text
Use case: precise-object-edit / ui-mockup.
Edit input image 1, the full ARKYA HUD screenshot concept. Input image 2 is only the three-column MATERIAL STYLE REFERENCE.
Keep image 1's exact aspect ratio, pixel dimensions, camera, landscape, sky, every tree and leaf silhouette, ground and path, player character, boar, positions and scale unchanged. This is a UI material theme switch of the SAME moment in the same game.
Keep ALL HUD bounding boxes, sizes, placements, portraits, target, minimap terrain and markers, exact text, numbers, fonts, text sizes, icons, progress lengths, cooldown masks, shortcuts, group members, quest content, chat messages identical. Do not add, delete, enlarge, rearrange or hide any HUD element. Health stays red, stamina ochre and XP gold. Action icons stay the same artwork and colors. Keep the original game environment pixels untouched outside UI regions.
Only change the decorative HUD materials, borders, background fills, ornament details and text colors to the specified theme. No collage or title board, no split screen, no new window. Output a single full game view. Leave the central play region exactly as supplied. Be precise and legible.
TARGET THEME: 03 — PIERRE DE MANA, matching COLUMN 3 in reference image 2.
Replace ink panels with dark blue slate #202A40, fine sculpted violet-slate borders #6C668A, silvery-ivory main text #E4E6EE. A few tiny amber etchings and contained pale blue crystal accents #76B8D7 at selected existing corners. Softly beveled crafted stone, medium-scale painted surface marks, flat quiet text grounds. Small rune incisions, no glowing holograms or neon. Same border thickness and geometry as the original: no large spikes, no bulky ornaments. Minimap gets matching stone rim while map pixels stay unchanged. Action slots retain identical painted icon art, just matching blue slate inset backings and stone frames. Text stays readable. Red health / ochre stamina / gold XP unchanged. Update the tiny lower-left marker only to exact text "Concept HUD — Pierre de mana".
```

