# Prairie peinte V3 — provenance

- Date : 15 octobre 2026.
- Outil : `image_gen.imagegen`, génération intégrée, mode `stylized-concept`.
- Destination : [PaintedMeadowV3.png](../../../Unity3D/Arkya/Assets/Arkya/World/StarterIsland/Textures/PaintedMeadowV3.png).
- Usage prévu : albédo diffus pour le sol de l’Île de l’Aube ; surface de 8 × 8 m dans le prompt.
- Résolution originale : **1 254 × 1 254 pixels**.
- Taille : 2 612 253 octets.
- SHA-256 : `ba5fdaac519420aea6b7bbf101084139d838190524193503ca6626f52c5cd072`.

Le PNG est la copie exacte de la génération retenue, sans découpe, retouche ou transformation. La V2 reste conservée. Prairie continue olive et sauge, glacis doux et petits accents de terre ; les bouquets d’herbe imprimés de la V2 sont remplacés par une base plus calme. L’image est une nouvelle proposition de sol dans la DA peinte du projet ; elle ne modifie pas les objets approuvés. Le prompt demande un raccord sur les deux axes ; son intégration et sa répétition sont également à examiner sur le terrain Unity.

## Prompt exact

```text
Use case: stylized-concept
Asset type: a single seamless square diffuse albedo texture for Unity MMO terrain, depicting 8 meters by 8 meters of softly painted grassy ground in Arkya.
Primary request: understated continuous sage-olive turf with broad, soft hand-painted tonal variations and very low flattened moss-cushion detail. Smooth contiguous grassy ground base. Ground color with soft-medium-scale layered brushwork inspired by the restrained painted surfaces of a sculpted fantasy tree. Approximately ten percent tiny muted earth flecks, blended naturally into turf. Restrained value contrast, beautiful subtle transitions from warm olive to cool sage, calm organic color grouping.
Critical design: this is a quiet continuous color foundation under separate actual 3D grass and plants. DO NOT paint separate tufts, individual grass blades, leafy clusters, botanical shapes or distinct objects onto the surface. Avoid the previous common mistake of creating repeating grass-clump illustrations. No recognizable objects at all, no hard edges, no dark outlines, no noisy stippling, no comic pattern, no carpet strands, no fur, no photorealistic noise. Use soft broad painterly pigment washes across the entire tile with sparse small flattened moss variations only.
Composition: perfectly orthographic straight down; edge-to-edge even detail and color distribution; seamless tile across both horizontal and vertical edges; no focal point or central area; intended to repeat across an open 8m terrain patch without obvious repeated objects.
Lighting: neutral evenly illuminated diffuse pigment only, unlit albedo. No directional illumination, no cast shadows, no baked ambient occlusion, no specular highlights or vignette.
Constraints: square, full bleed, opaque. No trees, grass tufts, leaves, flowers, roots, large stones, rocks, cracks, stripes, paths, perspective, border, frame, text or watermark. No saturated neon.
```

