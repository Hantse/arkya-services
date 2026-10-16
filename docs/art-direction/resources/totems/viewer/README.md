# Atelier des âmes totems — V2 à examiner

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

La base des deux espèces est une adaptation de **« Fox » par Bake3D** sous **CC BY 4.0**. Le pied de page donne accès à la [provenance et aux modifications Arkya](../sources/bake3d/README.md).

Le [rapport de vérification](../viewer-verification.json) sert à consigner les 24 chargements, les clips, la pause, le ralenti, les palettes, la lueur, les téléchargements et l'affichage mobile. Les contrôles à réaliser après une régénération portent sur les fichiers de la révision courante. La revue technique ne remplace pas la validation artistique des modèles et des allures.

Voir le [guide de la collection](../README.md) pour les sources, les commandes de construction et les contrôles GLB/FBX/IK.

Pour rejouer le contrôle du navigateur depuis la racine du dépôt, avec le serveur actif :

```powershell
npx --no-install @playwright/cli -s=totems open http://127.0.0.1:51805/totems/viewer/index.html --browser msedge
npx --no-install @playwright/cli -s=totems run-code --filename docs/art-direction/resources/totems/scripts/verify_viewer.js
```
