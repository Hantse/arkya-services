# Prompts — HUD V3, récolte et combat

16 octobre 2026. Outil intégré `image_gen`, édition d'images, fond opaque. Chaque image V2 finale a été inspectée avant édition. Les trois éditions utilisent le même prompt avec la précision de thème ci-dessous ; aucune retouche raster manuelle.

## Prompt commun

```text
Edit the supplied Arkya MMORPG HUD mockup into iteration V3. Keep the exact same painted forest, camera, composition, character, boar and existing art theme. Make ONLY these four precise UI layout changes:
1. HARVEST FEED: move the existing small wood icon and green "Bois × 3" gain notification from the bottom-left chat area to the space immediately BELOW the whole three-member party stack on the left, aligned with that stack. No panel, no border, just small icon and text. Bottom-left chat retains only the two existing conversation messages.
2. PARTY BUFFS: for each of Lyra, Borin and Néa, move the two buff icons currently to the RIGHT of their gauges to immediately UNDER their own BLUE mana gauge, aligned to its left edge. Make each buff icon much smaller, around 12 pixels, with a small gap. Remove the former right-side icons entirely. Keep the small avatars, names and RED health and BLUE mana bars; slightly increase spacing between members only if required for the tiny buff row. No enclosing background/card/frame around the party.
3. ENEMY COMBAT: add a thin compact RED life bar directly above the visible boar in the world, spatially anchored to its head. Approximately 75 percent health, small dark outline for legibility, small ivory label "Sanglier · 4" just above it. Show floating damage numbers beside the enemy: "-78" in pale ivory at medium size, and a slightly older smaller "124" in amber somewhat higher. Position numbers so they do not overlap the enemy name, life bar or hero. They represent a restrained upward fading hit animation in a static mockup, not huge arcade text. Keep the existing selected-target frame at top center.
4. MINIMAP BUTTONS: replace the two square buttons below the minimap with two clearly CIRCULAR buttons, matching the existing art theme. One folded-map icon and one journal/quest icon, centered. Thin circular themed rims, no square corners. Absolutely NO visible words "Map", "Quest", "Carte", "Journal" and NO keyboard letters on these buttons.
Preserve ALL the rest: two action rows of exactly 12 slots each (24 total), currently four empty upper slots, red life and OCHRE/GOLD stamina for player Aëlan (never blue for player's stamina), blue party mana, thin XP line, transparent borderless quest tracker, two chat lines without a panel, small bag bottom right, no Escape/settings button. Do not add decorative frames, banners, title cards or opaque blocks. Large open center and world visibility. Preserve exact overall image size and clarity. Update the subtle lower-right iteration caption to "HUD V3" with the existing theme name. Professional game HUD concept, crisp text and iconography.
```

## Cibles et précision ajoutée

- Cible `01-encre-cuivre-v2.png` → `01-encre-cuivre-v3.png`.
  Ajout : `Existing theme to preserve: Encre & cuivre — dark ink teal, fine copper rims, ivory`.

- Cible `02-carnet-exploration-v2.png` → `02-carnet-exploration-v3.png`.
  Ajout : `Existing theme to preserve: Carnet d’exploration — light parchment, honey leather rims, ivory floating text`.

- Cible `03-pierre-mana-v2.png` → `03-pierre-mana-v3.png`.
  Ajout : `Existing theme to preserve: Pierre de mana — dark blue slate, restrained crystal blue rims, ivory`.

Les nombres de dégâts illustrent une animation future ; les fichiers produits sont des images statiques.

