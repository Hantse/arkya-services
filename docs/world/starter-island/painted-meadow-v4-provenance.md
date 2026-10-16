# Prairie peinte V4 — provenance

Texture proposée le 16 octobre 2026 pour harmoniser le sol avec les surfaces
peintes des objets approuvés. La V3 est conservée à l'identique.

- Outil : `image_gen.imagegen`, génération intégrée, nouvelle image opaque.
- Destination : `Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Textures/PaintedMeadowV4.png`.
- Copie binaire exacte du résultat ; aucune retouche raster après génération.
- Dimensions réellement retournées par l'outil : **1 254 × 1 254 pixels**.
- SHA-256 : `3081efe0b5938d0f1d784cbd7f06453d14529ece87dfcaac40dbab6a0dd3ebd1`.
- Intention : touches peintes mieux définies, masses végétales isotropes,
  palette sauge/olive, sans bande directionnelle imprimée.
- Le prompt demande un raccord sur les deux axes ; la validation visuelle
  du raccord doit être réalisée dans le Terrain Unity à hauteur de joueur.

## Prompt exact

```text
Use case: stylized-concept. Asset type: square seamless basecolor/albedo texture for native Unity terrain, hand-painted stylized fantasy MMORPG meadow, 2048x2048. Primary request: a beautiful restrained ground surface of very short meadow turf and velvety ground moss, seen exactly orthographically from straight overhead. Painterly matte gouache treatment: soft rounded overlapping irregular patches with visible deliberate small brush dabs, gently modeled organic clusters, a few minute tan earth flecks, medium-scale sage green and olive variations. Palette: muted natural sage green, soft fern green, faded warm olive, occasional grey-green highlights; subtle warm earth undertones. The ground must feel like a painted miniature, with rich but calm material detail rather than photoreal grass. All brush strokes have varied random orientation, isotropic overall. Crucial technical requirements: perfectly seamless tile on left/right AND top/bottom; uniform average luminance across image; fully opaque; no global light or shadow, no vignette, no directional shading; the tile fills the whole canvas edge to edge. Avoid ALL diagonal bands, linear streaks, striped grain, chevrons, repeated medallions, checkerboard, grid, obvious motifs or clusters repeating inside the tile. Avoid long or individual upright grass blades, leaf outlines, flowers, rocks, recognizable objects, cracks, flat vector shapes, yellow neon grass, saturated lime, photographic micro-noise, depth of field, frame, borders, text or watermark. Keep the darkest and lightest patches near the middle value range, no black crevices and no white highlights.
```
