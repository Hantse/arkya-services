# Icône Arkya — launcher et jeu

L’emblème commun créé le 23 octobre 2026 associe un A doré, un cristal
turquoise et un médaillon bleu nuit. Il reprend la palette du launcher et
la direction fantasy peinte du jeu, avec une transparence autour du motif.

## Assets et intégration

- `launcher/src/Arkya.Launcher/Assets/Arkya.png` : PNG RGBA 1024 × 1024,
  utilisé dans l’interface du launcher.
- `launcher/src/Arkya.Launcher/Assets/Arkya.ico` : tailles 16, 24, 32, 48,
  64, 128 et 256 pixels ; ressource de l’EXE, de la fenêtre WPF et du setup
  Inno Setup. Les raccourcis et la désinstallation reprennent l’EXE.
- `Unity3D/Arkya/Assets/Arkya/Branding/Arkya.png` : copie identique du PNG,
  importée sans compression ni mipmaps, avec alpha. Conserver ses `.meta`.
- `Unity3D/Arkya/ProjectSettings/ProjectSettings.asset` : icône par défaut
  et huit tailles Standalone, de 16 à 1024 pixels, reliées au PNG.

Les nouveaux builds incluent ces icônes. Les exécutables déjà distribués
doivent être remplacés par une nouvelle publication pour les afficher.
La version publique du launcher et les installations existantes ne sont
pas remplacées par cette intégration.

## Vérification du 23 octobre 2026

- Launcher autonome Windows et installateur Inno Setup construits.
- Les sept images des ressources PE du launcher et du setup correspondent
  exactement à celles de l’ICO source.
- Captures WPF de connexion et de navigation inspectées.
- 139 tests Core réussis. Les 9 cas WPF réussissent ; le nettoyage de leur
  fixture expire après 10 secondes. Le même problème est reproduit avec
  les fichiers de la révision précédente, sans les changements d’icône.
- Unity MCP : import, réglages des textures, affectations en mémoire et
  relecture du fichier PlayerSettings sérialisé vérifiés. Capture de la
  texture réellement importée inspectée à 256 pixels. L’import initial
  s’est terminé sans erreur ; un relevé ultérieur a trouvé six erreurs
  CS0104 (`Cursor` ambigu) dans `CharacterLocalPlaytest.cs`, modifié en
  dehors de cette tâche. La validation globale du client reste limitée
  par ces changements de code concurrents.
- Aucun nouveau build complet du jeu Windows n’a été produit pour cette
  modification ; son icône EXE finale reste à vérifier lors de ce build.

Les sorties locales de validation sont dans
`artifacts/launcher-icon-20261023/` et
`Unity3D/Arkya/Captures/Branding-2026-10-23/`.

## Source visuelle

Image créée avec l’outil intégré `image_gen` (skill `imagegen`), puis
redimensionnée en 1024 × 1024 et convertie en ICO avec Pillow/Lanczos.
Les deux PNG doivent rester identiques lors d’un remplacement.

Prompt de génération utilisé :

```text
Use case: logo-brand. Asset type: the single shared Windows application icon for Arkya, a stylized painted fantasy MMORPG and its launcher. Create one polished square 1024x1024 icon, no mockup, no sheet. Subject: a bold sculpted capital A shaped like an ancient upward-pointing portal/spire, broad warm antique-gold legs and pointed apex, one luminous turquoise faceted crystal forming its center/crossbar. Clear instantly recognizable A silhouette. A restrained dark midnight-blue circular medallion behind the A, thin aged-gold rim. Palette from the actual Arkya launcher: midnight #101923, warm gold #D3B67C, muted teal with a small turquoise light. Style: beautifully hand-painted fantasy game emblem, smooth carefully shaped volumes, restrained bevels, subtle brush texture, refined and simple. Straight-on orthographic symmetrical composition. One large emblem fills 88 percent of the canvas, centered with generous clean silhouette, readable at 16x16 and 32x32. The A has strong thick strokes and clear negative space. Gentle warm edge light and small restrained turquoise glow contained inside the medallion. Genuinely transparent pixels outside the circular medallion. No words, no title, no additional letters, no ornaments or wings, no landscape, no extra objects, no busy details, no drop shadow outside the medallion, no background rectangle, no watermark.
```
