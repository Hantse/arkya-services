# Brise-Sève — première version 3D animée

Créé le 24 octobre 2026 à partir de la [seconde illustration](reference/concept-v2.png). Cette version conserve le quadrupède massif, les deux défenses, le masque de bois et la crinière végétale. Les formes et la finition restent plus simples que le concept ; la validation artistique par l'utilisateur est en attente.

![Rendu du modèle 3D réel](renders/hero.png)

## Examiner le modèle

Depuis la racine du dépôt :

```powershell
node docs/art-direction/resources/serve.mjs 51805
```

Ouvrir [l'atelier interactif](http://127.0.0.1:51805/monsters/brise-seve/viewer/index.html). Rotation, zoom, ralenti, curseur de temps, comparaison avec le concept et téléchargements sont disponibles. Les noms des clips proviennent du GLB chargé.

## Fichiers livrés

- [Blender éditable](assets/brise-seve.blend) : géométrie, texture incorporée, rig, sept actions et scène de rendu.
- [GLB autonome](models/brise-seve.glb) : LOD0, squelette, sept animations et texture incorporée.
- [FBX Unity](assets/brise-seve.fbx) : trois LOD et sept clips ; [texture](textures/brise-seve-basecolor.png).
- [Manifeste](manifest.json), [géométrie](geometry-report.json), [rig et appuis](animation-report.json), [validation source/export](validation-report.json).
- [Empreintes SHA-256 des fichiers principaux](checksums.json).
- [Profil](renders/profile.png), [dos](renders/rear.png), [planche d'animations](renders/animation-contact-sheet.png).

En pose initiale **Idle, image 1**, le modèle occupe environ **3,28 × 1,47 × 1,92 m** (longueur × largeur × hauteur), défenses, queue et crinière comprises. En pose de repos, les dimensions mesurées sont **3,277903 × 1,468335 × 1,945940 m**. Les hauteurs indiquent l'étendue verticale du maillage entre ses bornes. Le garrot du corps est plus bas que le sommet de la crinière.

| Niveau | Triangles |
| --- | ---: |
| LOD0 | 126 848 |
| LOD1 | 60 886 |
| LOD2 | 25 368 |

Une texture couleur 2048², un matériau, 29 os dont 24 déformants, quatre influences maximum. Ces budgets décrivent cette première livraison ; la densité LOD0 doit encore être évaluée pour des groupes de monstres en jeu.

## Animations

| Clip | Durée | Lecture | Vitesse de déplacement associée |
| --- | ---: | --- | ---: |
| Idle | 4 s | Boucle | 0 m/s |
| Walk | 1,2 s | Boucle | 0,70 m/s |
| ChargeAnticipation | 1 s | Une fois | — |
| Charge | 0,65 s | Boucle | 2,80 m/s |
| Attack | 0,9 s | Une fois | — |
| Hit | 0,5 s | Une fois | — |
| Death | 1,4 s | Une fois | — |

Clips sur place, racine immobile, animation enregistrée à 60 fps. La vitesse de déplacement serveur doit correspondre à celle du cycle pour éviter le glissement. Ces actions n'appliquent aucun dégât et ne constituent pas une IA de combat.

## Unity

Assets dédiés dans `Assets/Arkya/Monsters/BriseSeve/` du dépôt Unity séparé. Le FBX utilise un rig Generic et une texture couleur sRGB. L'intégration comprend un prefab de présentation, un matériau URP, un contrôleur Animator et une scène d'essai autonome. Le [rapport Unity](unity-validation.json) décrit les vérifications dans l'Editor.

La racine du prefab reste à l'échelle 1 et regarde vers +Z. L'Animator se trouve sur l'enfant `Visual`. Les volumes de visibilité couvrent les poses échantillonnées des sept clips et utilisent l'espace du root bone. Les sommets extraits avec `BakeMesh(..., true)` tiennent compte des unités FBX ; leurs min/max sont mesurés explicitement, car les bounds fournies par le bake peuvent être conservatrices.

Pour les captures de plusieurs poses dans une même mise à jour Editor, la mise à jour des matrices de skinning a été forcée temporairement, conformément à la [documentation Unity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer-forceMatrixRecalculationPerRender.html). Ce réglage de capture n'est pas activé dans le prefab livré.

Contrôleur : `Speed` sélectionne repos/marche ; `LocomotionRate` règle la vitesse de lecture (1 par défaut). Les triggers `Charge`, `StopCharge`, `Attack`, `Hit` et le booléen `Dead` pilotent les autres états. La préparation rejoint la charge ; attaque et impact reviennent au repos. La mort conserve sa pose finale.

Le collider `CollisionReference` reste désactivé : il fournit un gabarit à reprendre par l'authoring ECS, sans ajouter une nouvelle autorité physique à la présentation.

## Vérifications et limites

Le validateur contrôle les trois maillages, UV, poids, transformations, coordonnées finies, sept mouvements distincts, boucles et appuis. L'aller-retour FBX et le GLB ont été contrôlés séparément. Les semelles réelles restent au-dessus du sol ; le glissement maximum mesuré sur les pas de simulation est d'environ 0,038 mm par frame en tenant compte de la vitesse virtuelle du cycle.

L'[aperçu web](viewer-validation.json) a été vérifié sur ordinateur : modèle chargé, marche au ralenti, comparaison et arrêt de la mort. Les captures Unity montrent le [repos](renders/unity-idle.png), la [charge](renders/unity-charge.png), la [mort](renders/unity-death.png) et le [LOD2](renders/unity-lod2.png). Le contrôleur a été évalué dans l'Editor avec un graphe d'animation manuel, sans entrer en Play Mode ni démarrer les services du jeu.

La correction des yeux déplace huit îlots géométriques identifiés ; elle conserve leurs UV et leurs poids. Le [rapport de correction](repair-report.json) garde les mesures.

Les déformations extrêmes présentent encore des étirements locaux d'arêtes allant jusqu'à environ 4,9× aux articulations. Ce sont des points à reprendre lors d'une passe de sculpture et de peau, ainsi que le niveau de détail peint de l'écorce et de la mousse. Les contrôles d'intégrité ne constituent pas une approbation artistique ou une validation de performance en population.

Aucune apparition dans StarterIsland, logique d'aggro, navigation, dégâts, loot, quête ou réplication réseau n'est ajoutée.

## Régénération

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python-exit-code 1 --python docs/art-direction/resources/monsters/brise-seve/scripts/build_monster.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python-exit-code 1 --python docs/art-direction/resources/monsters/brise-seve/scripts/validate_monster.py -- --fbx-roundtrip
```

La source de construction contient la position oculaire finale et crée les LOD en pose de repos. `render_review.py` régénère les captures depuis le Blender sauvegardé. Les originaux et packs fournisseurs d'Arkya sont inchangés.

## Provenance

Concept créé avec l'outil intégré imagegen ; [prompts exacts des deux passes](reference/generation-prompts.json). Géométrie, peinture procédurale, UV, squelette et animations sont construits par les scripts de ce dossier dans Blender 4.5.14. Aucun modèle 3D tiers n'est incorporé. La direction suit les références Arkya arbre V3 et personnages sculptés.
