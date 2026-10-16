# Personnages de jeu — peinture V2, rig V1

Les quatre corps Humain/Elfe, silhouettes A et B, disposent ici d'un squelette
de **26 os**, de véritables poids de skinning et de trois animations en boucle.
Les études statiques de `../assets/` et `../models/` restent conservées.

## Livrables et contrat Unity

Chaque `human-a`, `human-b`, `elf-a`, `elf-b` contient un `.fbx`, une source
`.blend`, un rapport `.validation.json` et les couleurs linéaires des matières
dans `.materials.json`. Les modèles regardent vers −Y dans Blender ; l'export
FBX emploie les axes −Z/Y pour Unity. Hauteur de référence, coiffure comprise :
1,75 m pour les humains, 1,85 m pour les elfes ; pieds à zéro au repos.

- Import d'animation **Generic**, avatar créé depuis le modèle, racine `Root`.
- Objet du squelette : `Armature`. Les quatre modèles partagent les noms d'os.
- Takes `Armature|Idle`, `Armature|Walk`, `Armature|Run`, à renommer en `Idle`,
  `Walk`, `Run` si l'importeur conserve le préfixe.
- `Idle` : 4 s ; `Walk` : 1,2 s ; `Run` : 0,8 s ; échantillonnage à 30 Hz.
- Pas de déplacement de `Root` : la simulation déplace le personnage et la
  présentation choisit l'animation selon sa vitesse observée.
- `CharacterMesh` contient peau, visage, tenue et accessoires ; `HairMain`
  est le socle commun. Coiffure courte : masquer `HairLong` et `HairBraid`.
  Longue : activer seulement `HairLong`. Tressée : seulement `HairBraid`.
- Les surfaces utilisent l'attribut de couleur de sommets `Painted`, exporté
  en linéaire. **Multiplier cette couleur par la teinte de la matière**.
  `Skin`, `Hair`, `Iris`, `OutfitAccent` sont neutres pour la personnalisation.
  Les matières fixes conservent leur couleur `.materials.json` ; les remplacer
  par du blanc ferait perdre les cuirs, métaux et émaux de la DA.

Les poids anatomiques conservés des bases Rain/Snow sont regroupés sur les
os du jeu et normalisés, avec quatre influences au maximum par sommet. Les
ajouts de costume utilisent des poids adaptés aux articulations ; les plaques
de cuisse suivent la cuisse et les avant-bras suivent le coude. Deux bandes
de doublure sous les genoux referment la partie du pantalon masculin supprimée
par l'ancienne étude statique. Deux os de cape et deux os de cheveux apportent
un léger mouvement secondaire.

La marche et la course sont des cycles procéduraux V1 avec résolution
analytique à deux segments des jambes : pied d'appui au sol, pied libre levé,
genou fléchi dans le bon sens. Les cycles sont de véritables clés d'animation
exportées avec le squelette, sans animation de maillage figé.

## Budgets et limites

La passe du 16 octobre 2026 conserve davantage de géométrie sur le visage,
les paupières et les mèches. La coiffure courte représente **51 953–51 955
triangles pour A** et **63 030–63 037 pour B**, contre 74 700–87 700 pour les
études. La coiffure longue ajoute 2 782 triangles, la tresse 3 934. Le corps dispose de 10 ou 12 matières,
le socle capillaire d'une matière et la coiffure optionnelle d'une ou deux.

La peinture V2 ajoute aux couleurs `Painted` des ombres de contact courtes,
des creux plus frais, des arêtes convexes plus chaudes et des variations de
pigment à moyenne échelle. La peau conserve sa peinture source, complétée par
une chaleur discrète des joues. Ces détails restent dans les sommets : aucune
texture ni matière supplémentaire n'est créée et les quatre teintes
personnalisables restent neutres. Les coiffures optionnelles ne s'ombrent pas
mutuellement pendant la génération.

Le foulard B est repris depuis sa source V3 avant la réduction de l'étude
statique, ajusté aux proportions livrées et lié au cou/torse. Sa géométrie
plus dense évite les ouvertures du col pendant l'animation. Les cinq coques
d'épaule B et leurs liserés sont également repris ensemble depuis V3, avec une
grille régulière de 680 triangles par coque. Les tubes de bordure sont décalés
entiers au-dessus du cuir ; leur section n'est plus aplatie par projection.
La doublure de tunique A est reculée de 22 mm derrière les plaques de cuisse
et suit le même os de jambe, pour éviter qu'elle les traverse pendant les cycles.
Les attaches noires prévues sur les jambières restent présentes.

Dans Unity, `PaintedCharacter.shader` distingue la réponse mate de la peau,
du tissu et des cuirs, les reflets doux de l'émail/cuivre et les petits reflets
des yeux. Les lumières additionnelles sont prises en charge en Forward et
Forward+. Le studio de création emploie une lumière moins jaune, un remplissage
froid discret, SMAA High et un tonemapping Neutral. Après copie des quatre FBX
vers `Assets/Arkya/Characters/Models/`, ouvrir la scène de création puis utiliser
**Arkya → Characters → Refresh painted art direction**. Cette commande conserve
les prefabs, rigs, contrôleurs, catalogue et contrats d'apparence. Elle enregistre
uniquement les matières et, si la scène de création est active, son studio.

Ce rig permet de présenter, déplacer et animer ces personnages dans le
prototype. Ce n'est pas un rig facial ni un jeu d'animations de combat : les
doigts sont rattachés à la main et le visage suit la tête. Le retargeting
Humanoid, les prises d'armes, les animations de combat, les LOD supplémentaires,
le cloth physique, l'IK des pieds sur terrain et l'ajustement fin de la foulée
à toutes les vitesses restent des travaux distincts. Des intersections de
costume peuvent encore apparaître dans des poses non couvertes par ces cycles.

La taille reste une mise à l'échelle de présentation ; elle ne modifie pas
implicitement les collisions ou statistiques autoritaires.

## Génération et vérification

Depuis la racine du dépôt :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python docs/art-direction/resources/personnages/scripts/rig_characters.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python docs/art-direction/resources/personnages/scripts/validate_game_rigs.py
```

`source-joints.json` conserve les positions anatomiques issues des sources
créditées dans la pose documentée des études ; la génération normale n'exige
pas les archives externes Rain/Snow. `extract_game_joints.py` permet de
régénérer ces positions si ces archives sont disponibles. Il ne lance aucun
script Python téléchargé avec les rigs.

Les rapports individuels vérifient les poids, les couleurs de sommets et le
déplacement réel des sommets pour chaque clip. `fbx-roundtrip-validation.json`
vérifie indépendamment les quatre FBX après réimport : os, modificateurs de
skinning, poids, dimensions, trois clips déformants et continuité des boucles.
`previews/` contient des images de poses animées examinées pendant le travail.
La validation de compilation et de jeu Unity appartient au guide d'intégration
de création de personnage et à ses rapports.

## Attribution

Bases anatomiques et faciales : **Rain et Snow, Blender Studio / Blender
Foundation, CC BY 4.0**. Modifications Arkya : costumes fantasy, peinture,
proportions, oreilles elfiques, cheveux, simplification, rig compact et
locomotion procédurale. Conserver les [crédits et liens complets](../CREDITS.md)
avec les modèles et leurs distributions.
