# Atelier des âmes totems — loup V10 et renard V2 à examiner

L'atelier lit [le manifeste](../manifest.json) et utilise le composant local `model-viewer`. Depuis `docs/art-direction/resources/` :

```powershell
node serve.mjs 51805
```

Ouvrir <http://127.0.0.1:51805/totems/viewer/index.html>.

- Choisir le Loup ou le Renard, puis l'un des douze pelages.
- La bande de palette affiche les douze teintes de référence de l'apparence.
- Repos, Marche et Course lisent les clips `Idle`, `Walk` et `Run` du GLB.
- **Ralenti × 0,5** facilite l'examen du retour des pattes, des appuis et de la déformation. Revenir à **Vitesse normale** pour juger l'allure.
- Pause fige l'animation ; Rotation déplace la caméra. Glisser et zoomer permet d'inspecter les deux côtés, le visage et les semelles.
- **Lueur du pelage** module l'émission de `TotemMagic` et `TotemSpark` ; les motifs restent présents sur le corps.
- Les liens donnent le GLB sélectionné et les sources Blender/FBX de l'espèce.
- Dimensions et budgets viennent du manifeste. Ils ne définissent aucune collision ni statistique de gameplay.

Les clips restent sur place. La note de mouvement utilise leur vitesse virtuelle issue des métadonnées de locomotion. Le ralenti change la lecture dans l'atelier ; l'association au déplacement réel sera gérée par le jeu. Les animations sont suspendues au chargement si le système demande de réduire les mouvements.

Le **loup V10** adapte « 3d wolf » par NewDLC, sous CC0 1.0 ; voir [sa provenance](../sources/newdlc/README.md). Il suit le loup blanc debout en bas à droite de la première référence : tête basse et avancée, raccord nuque–garrot, petits yeux, oreilles triangulaires et museau effilé. La V10 épaissit la cage thoracique, le poitrail et le collet, avec un haut de cuisse cohérent. La gueule retrouve une base et une mandibule plus soutenues, une terminaison légèrement relevée et une truffe plus transversale ; les joues restent reculées. Le corps conserve son échelle et ses appuis, sans déplacer le squelette. Les joues claires restent sous les oreilles et le front ; petits yeux, oreilles et queue sont conservés. Examiner ensemble face, profil, trois quarts et animations. Le **renard V2** conserve « Fox » par Bake3D sous CC BY 4.0 ; voir [sa provenance](../sources/bake3d/README.md).

Le [rapport ciblé V10](../viewer-v10-verification.json) consigne les essais de la révision `20261016T150222819563Z-totems` sur quatre pelages du loup (`lunar`, `ember`, `abyss`, `pearl`), les trois clips sur ordinateur, les commandes de lecture et de magie, et les liens de téléchargement. Le [rapport complet](../viewer-verification.json) reste l'historique V5 ; ses 24 chargements, ses contrôles mobiles et les essais du renard n'ont pas été répétés pour V10. Le code de l'interface est inchangé. La revue technique ne remplace pas la validation artistique des modèles et des allures.

Voir le [guide de la collection](../README.md) pour les sources, les commandes de construction et les contrôles GLB/FBX/IK.

Pour rejouer le contrôle du navigateur depuis la racine du dépôt, avec le serveur actif :

```powershell
npx --no-install @playwright/cli -s=totems open http://127.0.0.1:51805/totems/viewer/index.html --browser msedge
npx --no-install @playwright/cli -s=totems run-code --filename docs/art-direction/resources/totems/scripts/verify_viewer.js
```
