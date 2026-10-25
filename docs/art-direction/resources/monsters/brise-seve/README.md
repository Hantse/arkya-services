# Brise-Sève — version 2, formes et animations

Repris le 25 octobre 2026 à partir de la [seconde illustration](reference/concept-v2.png). Le masque d'écorce est plus continu, les yeux sont replacés sous les arcades, les narines sont creusées, les pattes épaissies et la crinière comporte des mèches volumineuses, effilées et orientées vers l'arrière. Les plaques d'épaule et les manchettes de mousse sont plus lisibles. La peinture directionnelle distingue le pelage, le bois, la mousse et l'ivoire.

Cette version rapproche les formes du concept sans en reproduire toute la finesse. La crinière reste plus régulière, les couleurs plus douces et la posture plus haute que sur l'illustration. La validation artistique par l'utilisateur reste ouverte. Le [rendu V1](renders/v1-hero.png) permet la comparaison.

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

En pose initiale **Idle, image 1**, le modèle occupe environ **3,40 × 1,59 × 2,14 m** (longueur × largeur × hauteur), défenses, queue et crinière comprises. Les dimensions exactes en pose de repos et animée figurent dans le manifeste. Le garrot du corps est plus bas que le sommet de la crinière.

| Niveau | Triangles |
| --- | ---: |
| LOD0 | 174 906 |
| LOD1 | 61 216 |
| LOD2 | 24 486 |

Une texture couleur 2048², un matériau, 29 os dont 24 déformants, quatre influences maximum. Le LOD0 privilégie la vue rapprochée ; les deux LOD allégés restent proches des budgets de la V1. La performance avec des groupes de monstres n'est pas encore mesurée. Le GLB utilise l'atlas sans multiplier une seconde fois les couleurs de sommets déjà incorporées.

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

Le validateur contrôle les trois maillages, UV, poids, transformations, coordonnées finies, sept mouvements distincts, boucles et appuis. Il examine désormais toutes les arêtes non dégénérées sur chaque image des clips, dans Blender et après aller-retour FBX. Le GLB est contrôlé séparément. Les semelles réelles restent au-dessus du sol ; les valeurs détaillées de glissement et de contact figurent dans le rapport.

L'[aperçu web V2](viewer-validation.json) a affiché le modèle, la marche au ralenti et la mort corrigée. La capture finale montre aussi Impact arrêté à 0,50 s. Certaines requêtes de contrôle du navigateur ont expiré : le chronométrage exact de fin de mort, la comparaison et les erreurs de console web n'ont pas été revérifiés pour cette version. Les captures Unity montrent le [repos](renders/unity-idle.png), la [charge](renders/unity-charge.png), la [mort](renders/unity-death.png) et le [LOD2](renders/unity-lod2.png). Le contrôleur a été évalué dans l'Editor avec un graphe d'animation manuel, sans entrer en Play Mode ni démarrer les services du jeu.

Les influences de tête sur les pieds et celles du cou sur l'intérieur des coudes ont été corrigées. Les transitions de poids aux articulations et les plans de flexion ont été repris. La chute transporte les rotations avec le corps et replie les pattes du côté sol : le flanc couvert d'écorce prend l'appui, avec la peau de l'épaule à environ 2,9 cm du sol.

| Étirement maximal des arêtes | V1 mesurée sur toutes les images | V2 |
| --- | ---: | ---: |
| Marche | 4,52× | 2,27× |
| Charge | 5,02× | 2,52× |
| Mort | 4,93× | 1,93× |

Un étirement local subsiste dans les poses extrêmes de marche et de charge. Au maximum de la charge, une arête intérieure de l'avant-bras passe de 18,4 à 46,4 mm ; environ 0,27 % des arêtes du maillage complet dépassent 2×. Le [rapport comparatif](deformation-comparison.json) donne les mesures et le dénominateur, qui inclut les ornements rigides. Le seuil d'échec a été renforcé de 5× à 3×. Ces contrôles attestent une amélioration mesurée, sans constituer une correction parfaite, une approbation artistique ou une validation de performance en population.

Aucune apparition dans StarterIsland, logique d'aggro, navigation, dégâts, loot, quête ou réplication réseau n'est ajoutée.

## Régénération

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python-exit-code 1 --python docs/art-direction/resources/monsters/brise-seve/scripts/build_monster.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python-exit-code 1 --python docs/art-direction/resources/monsters/brise-seve/scripts/validate_monster.py -- --fbx-roundtrip
```

La source de construction contient les positions finales et crée les LOD en pose de repos. `painted_material.py` produit les pigments directionnels avant leur cuisson en atlas. `render_review.py` régénère les vues et la planche d'animations depuis le Blender sauvegardé. Le rapport de correction des yeux V1 est conservé uniquement comme historique. Les originaux et packs fournisseurs d'Arkya sont inchangés.

## Provenance

Concept créé avec l'outil intégré imagegen ; [prompts exacts des deux passes](reference/generation-prompts.json). Géométrie, peinture procédurale, UV, squelette et animations sont construits par les scripts de ce dossier dans Blender 4.5.14. Aucun modèle 3D tiers n'est incorporé. La direction suit les références Arkya arbre V3 et personnages sculptés.
