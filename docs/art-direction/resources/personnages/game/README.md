# Personnages de jeu — visages adultes et doubles lames, 20 octobre 2026

Les quatre modèles Humain/Elfe, silhouettes A et B, ont été reconstruits à
partir de la [référence choisie par l’utilisateur](../../references/personnages-stylises-2026-10-20/README.md).
Les études `../assets/` et `../models/`, les sources Rain/Snow et les anciennes
captures restent conservées. Les `.blend` et `.fbx` de ce dossier sont la
nouvelle livraison pour Unity. Cette passe reprend réellement les volumes du
visage et des cheveux après la seconde sculpture, encore jugée trop enfantine.
Les captures `*-adult-final*.png` illustrent les fichiers livrés ; les captures
`*-reforge*.png`, `*-sculpt*.png` et `*-adult-01*` à `*-adult-04*` conservent les
étapes de travail et ne constituent pas les résultats finaux.

![Humain B — visage adulte, tenue jade et doubles lames](previews/human-b-adult-final.png)

## Changements de géométrie

- Costume sculpté : deux volumes de cuirasse ajustés avec ouverture en V,
  volutes intégrées, épaulière asymétrique relevée et enveloppante, brassards
  à contours courbes, plaques de hanche prolongées dans le dos. Les bordures
  d’or disposent de plateaux et de biseaux séparés par leurs normales.
- Foulard à plis tombants, drapé diagonal ivoire avant/dos et jupe croisée
  à ourlet asymétrique. Un éventail de dix-huit lamelles fait le tour des
  hanches. Le drapé est placé devant les pièces sous-jacentes, puis son
  épaisseur est reconstruite et vérifiée pour éviter les faces coplanaires.
  La cuirasse et sa sangle suivent l’anatomie réelle ; le revers de chaque
  volume de cuirasse suit sa face avant pour préserver une épaisseur continue.
- Demi-jambières d’or ajustées sous les genoux, ailes fixées aux chevilles,
  sandales et laçages sur B ; les bottes et leur doublure restent sur A.
- Casque jade englobant le crâne et les tempes, bandeaux d’or larges continus,
  médaillons et cinq cristaux. Trois mèches de largeurs différentes sortent
  sous le casque, avec raie latérale et deux yeux dégagés. La queue haute est
  reconstruite en lobes asymétriques torsadés, sillons irréguliers, goulots aux
  tangentes communes, manchettes et boucle. La coiffure longue reste disponible.
- Ensemble tête/visage/yeux/cheveux/casque réduit de 11 % autour du bas du
  cou depuis la première passe ; aucune réduction globale supplémentaire ici.
  La nouvelle sculpture déplace le volume des joues basses vers les pommettes,
  construit les angles de mandibule et le menton, allonge le milieu du visage,
  affine le nez et réduit le relief des lèvres. Les orbites et les paupières
  forment des ouvertures en amande, sous des sourcils angulaires effilés.
  Les globes, iris et pupilles sont reconstruits sans les anciens trous ; leur
  profondeur est adaptée séparément aux bases A/B. Un rebord continu termine
  les paupières A. Les bases anatomiques restent Rain/Snow. Le bas du cou A
  reste rentré sous le col ; les départs du drapé et la pointe d’épaule sont
  rabattus localement pour ne plus monter au menton.
  L’ouverture historique de 193 arêtes à l’arrière du crâne A est refermée par
  une surface raccordée : aucun sommet existant du visage n’est déplacé par
  cette réparation.
- Deux lames courbes turquoise, à âme d’or continue percée, gardes et poignées
  de cuir. Les doigts existants sont sculptés autour des poignées à partir de
  leurs masques sources, avec raccord progressif au poignet. Les armes font
  partie du mesh de présentation et suivent rigidement les mains existantes.
  Un masque anatomique strict exclut le bassin et les cuisses de cette sculpture,
  même lorsqu’ils sont proches des mains au repos. Les sommets hors mains et
  avant-bras sources sont vérifiés identiques bit à bit avant et après la prise.
  L’angle de prise final est de 0,94 radian ; mains et lames utilisent le même
  repère. Le clip Idle natif ouvre légèrement les bras pour préserver le
  dégagement de la jupe, sans modifier les matrices de repos.
- Le corps B complet a été restauré depuis Rain : les anciennes études
  avaient définitivement appliqué le masque des jeans, laissant les jambes
  absentes derrière les vêtements. `complete-rain-anatomy.blend` conserve
  ce corps complet posé, peint et réduit, avec ses poids sources.

Il s’agit d’une refonte intégrée à comparer à la référence, pas d’une
validation artistique définitive de son niveau de finition. Les silhouettes,
visages et vêtements restent des adaptations des bases existantes. La prise
d’armes est statique : aucun système d’équipement ni animation de combat
n’est ajouté à cette livraison.

## Contrat Unity préservé

- Armature `Armature`, racine `Root`, squelette Generic de **26 os**.
- **Matrices de repos et hiérarchie identiques** au rig produit par les études
  historiques, vérifiées indépendamment ; les os n’ont pas été redimensionnés.
- Quatre meshes exacts : `CharacterMesh`, `HairMain`, `HairLong`, `HairBraid`.
  `HairMain` reste actif. Coiffure courte : aucune option active ; longue :
  seulement `HairLong` ; tressée : seulement `HairBraid`.
- Quatre influences au maximum par sommet, poids normalisés, aucun sommet
  non lié. Les poids anatomiques Rain/Snow restent remappés sur les os du jeu.
- Les deux lames sont fusionnées dans `CharacterMesh`, avec un poids de 1 sur
  `LeftHand` ou `RightHand`. Aucun mesh ou os supplémentaire n’est requis.
- Drapés, bordures, broderies, cuirasse et sangle utilisent explicitement
  `Chest`/`Spine`/`Hips`. Les deux faces de chaque ruban ont des poids identiques
  calculés sur les hauteurs de repos avant projection. Le nom « shoulder »
  du drapé arrière ne déclenche plus les anciens poids automatiques du bras.
- Les takes FBX `Armature|Idle`, `Armature|Walk`, `Armature|Run` restent à
  30 Hz, en boucle et sans root motion (4 s, 1,2 s, 0,8 s).
- Les clips natifs Unity de repos, locomotion, saut et nage peuvent conserver
  leurs chemins et leurs pivots. Leur vérification visuelle dans Unity reste
  distincte des contrôles des takes FBX décrits ici.

La hauteur d’apparence historique reste **1,75 m pour Human / 1,85 m pour Elf**.
La coiffure haute et les cristaux dépassent ce repère. Les bornes géométriques
complètes sont 1,915 m pour Human A, 1,962 m pour Human B, 2,025 m pour Elf A et
2,074 m pour Elf B. Les pieds au repos restent à zéro. Cadrer les accessoires
par les bounds des renderers ; ne pas utiliser la taille du corps comme plafond.

## Matières et peinture

L’unique attribut `Painted` est une couleur de sommets **CORNER**, exportée en
linéaire. Multiplier cette couleur par la teinte du matériau. La génération
unifie explicitement les domaines avant de joindre les meshes : Blender peut
sinon conserver deux attributs homonymes POINT/CORNER, et perdre la peinture de
certaines pièces lors du rendu ou de l’export.

`Skin`, `Hair`, `Iris`, `OutfitAccent` restent blancs pour la personnalisation.
Les valeurs fixes linéaires sont enregistrées dans chaque `.materials.json` :

| Matière | RVB linéaire | Aspect |
|---|---|---|
| Metal | 0,78 / 0,43 / 0,075 | Or chaud, reflet métallique franc |
| Ivory | 0,78 / 0,80 / 0,74 | Tissu clair mat |
| Leather | 0,092 / 0,036 / 0,026 | Cuir brun |
| DarkLeather | 0,025 / 0,014 / 0,018 | Doublures et attaches |
| Gem | 0,045 / 0,8 / 0,59 | Cristaux turquoise, faible émission |
| Blade | 0,045 / 0,75 / 0,62 | Lames turquoise biseautées, faible émission |
| EyesWhite | 0,9 / 0,88 / 0,81 | Blanc d’œil |
| Pupil | 0,004 / 0,008 / 0,009 | Pupilles et cils |

Les couleurs de sommets portent des variations de plan et de pigment : chaleur
subtile des lèvres, du nez et des pommettes, ombres froides des orbites, valeurs
directionnelles et creux plus sombres des cheveux. L’or et le jade reçoivent
des variations larges. Ces couleurs restent attachées au mesh pendant les
animations. Aucune texture supplémentaire n’est requise. Les captures utilisent
la palette magenta/jade. `Blade` utilise une rugosité de 0,20, un métal de 0,25
et une émission linéaire effective de 0,0042 / 0,070 / 0,0504 dans Blender.
Le shader Unity et sa revue native sont distincts de ces rendus de contrôle.

## Budgets mesurés et limites

| Silhouette | Coiffure courte | Coiffure longue | Queue segmentée |
|---|---:|---:|---:|
| A, humain ou elfe | 167 330 triangles | 178 958 | 203 506 |
| B, humain | 172 799 triangles | 184 427 | 208 975 |
| B, elfe | 172 809 triangles | 184 437 | 208 985 |

Le corps utilise 12 matériaux, le socle capillaire 1, les cheveux longs 1,
la queue segmentée 2. La géométrie des deux options désactivées reste stockée
dans le FBX, mais n’a pas à être rendue simultanément. Le budget augmente avec
les coques, biseaux, plis, la jupe à dix-huit lamelles et le corps B complet. Aucun LOD de foule
supplémentaire n’est livré : les scènes peuplées nécessitent une mesure et une
réduction dédiée, sans confondre ces chiffres avec un budget validé de MMORPG.
Les chiffres incluent les bordures d’or fermées, la doublure du torse A et les
deux lames : 8 280 triangles par paire, 8 292 pour Elf B après triangulation.
Les volumes détaillés des nouveaux cheveux et les yeux reconstruits augmentent
le budget ; ce n’est pas une livraison optimisée pour les foules.

Le rig ne fournit pas de contrôle facial, de doigts animés individuellement,
d’IK de pieds ni de simulation physique des tissus. Les mains restent fermées
et les lames présentes dans cette présentation ; elles ne sont pas équipables.
La conformité des costumes à toutes les poses possibles n’est pas garantie
par trois cycles de locomotion. Les captures de course documentent une pose
supplémentaire et ne remplacent pas la validation en jeu.

## Génération et vérification

Depuis la racine du dépôt :

```powershell
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python docs/art-direction/resources/personnages/scripts/reforge_characters.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python docs/art-direction/resources/personnages/scripts/validate_reforge_contract.py
& 'D:/Tools/BlenderPortable/blender-4.5.14-windows-x64/blender.exe' --background --disable-autoexec --python docs/art-direction/resources/personnages/scripts/validate_game_rigs.py
```

`reforge_characters.py` lit les études et le corps Rain complet conservé ici.
`sculpt_reference.py` construit la seconde passe de formes et son ajustement
du drapé ; `sculpt_head.py` reconstruit les visages, les yeux et les cheveux ;
`sculpt_weapons.py` ferme les mains et construit les lames. Ils sont appelés
par le générateur, sans invocation séparée.
Il ne nécessite ni réseau ni exécution des scripts embarqués dans les sources.
`rig_characters.py` conserve les fonctions de rig et de locomotion historiques ;
son exécution directe reconstruit l’ancienne passe, pas cette refonte.
`extract_complete_anatomy.py` permet de refaire l’extraction du corps depuis
l’archive Rain locale créditée, sans exécuter ses scripts. Elle n’est nécessaire
que pour régénérer `complete-rain-anatomy.blend`.

- Les rapports individuels vérifient couleurs, poids et déformation des trois
  clips, puis consignent les bornes et les matrices de repos. Ils vérifient
  également l’orientation extérieure de chaque face avant des plaques générées.
  Les contours des pièces miroir sont réorientés avant construction ; les
  volumes fermés sont recalculés vers l’extérieur. Les lamelles de jupe et
  leur support partagent les mêmes règles de poids pour limiter les traversées.
  `torsoDraperyWeightChecks` impose l’absence de tout poids de bras ou de main
  avant assemblage et une erreur nulle entre les deux faces du tissu.
  `staticGripChecks` décrit les deux prises et `bladeWeightChecks` impose un
  poids unique de 1 sur la bonne main pour chaque pièce d’arme avant assemblage.
  `nonHandVerticesUnchanged` interdit une déformation de peau hors du masque
  anatomique ; `cranialSurfaceRepair` vérifie la fermeture du crâne A et
  l’invariance de ses sommets existants.
  `exportTriangulationChecks` impose des triangles explicites dans les sources
  et les FBX, sans déplacer de sommet. Les seules faces d’aire nulle ou
  inférieure à 10⁻¹⁴ m² sont retirées : 48 dans HairMain, plus une dans CharacterMesh Human B et trois
  dans CharacterMesh Elf B. Unity ne doit pas résoudre de polygone non plan.
- `reforge-contract-validation.json` compare la hiérarchie et chaque élément
  des matrices à un rig reconstruit depuis les études intactes, et vérifie
  l’unicité de `Painted` et la neutralité des quatre matériaux personnalisables.
- `fbx-roundtrip-validation.json` réimporte indépendamment les quatre FBX :
  os, skinning, poids, couleurs, bornes, clips déformants et continuité des boucles.
  Il impose également le même nombre de triangles qu’avant export et l’absence
  de triangles dégénérés.
- `render_reforge.py` produit les captures couleur, portrait, dos, argile et
  course, sans modifier les sources livrées. `--model`, `--hair`, `--angle`,
  `--portrait`, `--clay`, `--clip` et `--frame` rendent la revue reproductible.
  `--cull-back` masque les faces arrière pendant la revue Blender pour révéler
  les erreurs d’orientation que le rendu recto verso masquerait. L’option
  `--arms-up` détache l’action courante et lève les deux bras pour vérifier
  la tenue du drapé ; cette pose de contrainte ne remplace pas un clip Unity.
  Les captures
  `*-adult-final*.png` utilisent ce contrôle sur les sources finales. Elles ne
  remplacent pas la revue du shader et des clips natifs Unity.

## Attribution

Bases anatomiques et faciales : **Rain et Snow, Blender Studio / Blender
Foundation, CC BY 4.0**. Modifications Arkya : costume et coiffures reconstruits,
peinture, proportions, visage, oreilles elfiques, corps complet restauré,
yeux reconstruits, prise statique, doubles lames, simplification, rig compact
et locomotion procédurale. Conserver les
[crédits et liens complets](../CREDITS.md) avec les modèles distribués.
