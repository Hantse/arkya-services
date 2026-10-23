# Totems — loup V10 et renard V2 dans Unity

Vérification du 16 octobre 2026 dans **Unity 6000.6.6f1**, URP 17.6.0.
Révision courante : `20261016T150222819563Z-totems`. Le loup V10 remplace le
loup V9 ; le renard conserve intégralement sa version V2. La qualité artistique
reste à valider par le créateur du jeu.

## Assets livrés

Sources éditables et provenance : [collection](../../resources/totems/README.md).
Intégration isolée dans `Unity3D/Arkya/Assets/Arkya/Totems/` :
deux FBX, 24 atlas 2 048 × 2 048, 72 matériaux, deux contrôleurs,
24 prefabs d’apparence et deux prefabs par défaut (`wolf` et `fox`).
Chaque animal comporte **39 os**, trois meshes skinnés et trois matériaux.
Le loup compte **31 074 triangles après import Unity** : 29 508 pour le pelage,
1 086 pour les marques magiques et 480 pour les étincelles. Le manifeste
source compte 31 090 triangles, dont 1 102 pour les marques : l'écart observé
est de 16 triangles sur ce seul mesh ; sa cause n'a pas été isolée.
Le renard conserve ses **30 366 triangles**.

Le manifeste source donne au loup en pose Idle environ 1,860 m de long,
0,469 m de large et 0,857 m de haut ; le renard reste haut de 0,644 m.
Le loup conserve la base indépendante « 3d wolf » de NewDLC.
La V10 renforce le thorax, le poitrail, le collet et les cuisses : la largeur
augmente d'environ 25 % par rapport à la V9. La base du museau et la mandibule
gagnent du volume, la truffe s'élargit et le museau se relève légèrement.
Le port bas et avancé, les petits yeux enchâssés et les oreilles triangulaires
sont conservés. Le masque clair reste limité aux joues, sous le front et les
oreilles ; le sourcil dessiné reste absent. Les pieds, la queue abaissée,
la structure du rig et les clips sont conservés.
Les douze palettes par animal possèdent chacune douze couleurs avec dégradés.
`manifest.json`, `CC0-1.0.txt` et `CC-BY-4.0.txt` sont copiés dans le dossier Unity.
Le loup V10 dérive de [NewDLC, sous CC0](../../resources/totems/sources/newdlc/README.md).
Le renard V2 dérive de [« Fox » de Bake3D, sous CC BY 4.0](../../resources/totems/sources/bake3d/README.md).
Le crédit obligatoire de Bake3D reste attaché au renard ; les provenances sont distinctes.

## Contrôles effectués

- [Import ciblé du loup](wolf-import-report.json) : douze apparences et son
  prefab par défaut remplacés, textures propres, orientation +Z, root à
  l’échelle 1 et scène active conservée. Le renard n’a pas été réimporté.
- [Structure et contrôleur du loup](wolf-validation.json) : avatars Generic valides,
  poids normalisés, aucune référence de matériau ou script manquante,
  trois clips bouclés. Transitions Idle → Walk → Run → Idle réellement évaluées.
  Les douze apparences sont contrôlées ; le [prefab par défaut](wolf-default-prefab-validation.json)
  réutilise les mêmes meshes, matériaux, contrôleur et avatar que Lune astrale,
  avec ses transforms vérifiés et sans collider ni script manquant.
  Les contrôles du renard restent ceux de [la validation V2](validation.json) ;
  les anciennes lignes loup de ce rapport V2 sont historiques.
- [Audit loup](wolf-locomotion-audit.json) et [audit renard](fox-locomotion-audit.json) :
  déformation réelle de la peau par `BakeMesh`, quatre membres articulés,
  semelles pondérées foot/toe, markers de contact, appuis compensés par la vitesse,
  contrôle des arêtes étirées et des extrémités de boucle.
  **120 échantillons/seconde**, y compris les poses interpolées entre les clés.
- La compression d’animation Unity est **désactivée** : elle introduisait
  initialement plusieurs millimètres de glissement ; les courbes complètes
  sont maintenant conservées.
- [Captures URP du loup V10](wolf-capture-report.json) : douze portraits et
  comparaison du flux à pose fixe ; 461 pixels changent entre 0 et 0,55 seconde.
  Les douze portraits du renard et son contrôle de flux (400 pixels) restent
  ceux de [la capture V2](capture-report.json), sans régénération.
- [Gros plans de la tête Lune](wolf-head-capture-report.json) et
  [Perle](wolf-pearl-head-capture-report.json) : face, profil et trois-quarts
  en Idle à 0,5 s, avec les mêmes directions de caméra et un cadrage calculé
  sur les sommets de la tête. Base du museau, épaisseur de mandibule, truffe
  et raccords joue–gorge inspectés, sans nouveau pli parallèle ni pincement visible.
  De petites dents restent visibles dans l'ouverture de bouche sur les vues de profil.
  Perle apparaît gris lilas dans cet éclairage URP ; cette palette ne reproduit
  pas le blanc pur de la référence de proportions choisie.
- [Conservation](wolf-preservation-report.json) : **126 fichiers du renard
  (assets et .meta) identiques par SHA-256**, aucun GUID modifié parmi les
  **137 références d’assets préexistantes**. Les **39 fichiers source du renard**
  et la scène StarterIsland avec son `.meta` restent également identiques.
- [Intégrité](source-copy-report.json) : SHA-256 identiques pour les deux FBX,
  24 PNG, manifeste et deux licences, soit **29 fichiers** ; aucun asset ou dossier sans son `.meta`.
- [Éditeur](editor-report.json) : scripts compilés/exécutés sans diagnostic,
  shader supporté sans erreur, 24 textures importées à 2K et 26 prefabs.
  StarterIsland reste actif, propre, hors Play ; aucune scène utilisateur
  n’a été créée, fermée ou modifiée par les contrôles. L'éditeur était déjà ouvert.
  La [console initiale](wolf-v10-console-initial.json) conserve trois avertissements
  et une erreur historiques : accès au compte Unity AI et timeout Pipeline de la
  passe V4, puis deux échecs de collecte de signature Windows de `claude.exe`
  par le connecteur Unity AI, antérieurs à l'import V9.
  Le [contrôle final](wolf-v10-console-final.json) ne contient aucun nouvel avertissement
  ni erreur depuis cette baseline ; aucun diagnostic de compilation ni message lié
  aux assets V10. `compilationFailed` reste faux.

| Mesure Unity | Loup | Renard |
|---|---:|---:|
| Durées Idle / Walk / Run | 4 / 1 / 0,7 s | 4 / 1 / 0,6 s |
| Vitesse de référence Walk / Run | 0,424 / 1,514 m/s | 0,187 / 0,669 m/s |
| Glissement maximal d’appui | 0,1302 mm | 0,067 mm |
| Pénétration maximale de peau | 0,1296 mm | 0,053 mm |
| Écart de position à la boucle | 0,000266 mm | 0 |
| Écart maximal de vitesse à la boucle | 0,03915 m/s | 0,0127 m/s |

Le loup a été contrôlé sur 481 poses Idle, 121 poses Walk et 85 poses Run.
Au pire 0,4771 % des arêtes éligibles dépassent les bornes de déformation
(2,5× / 0,25×), sous le seuil de 1 %. Les semelles se lèvent de 3,76 à 4,31 cm
à la marche et de 8,90 à 9,46 cm à la course ; les quatre genoux s'articulent.

La vitesse de couture est estimée par différences unilatérales d’ordre 4,
à la cadence source de 60 Hz, afin d’annuler l’effet de courbure d’un mouvement
lisse. Le rapport conserve aussi le résultat brut du premier ordre pour
traçabilité. Les seuils de validation n’ont pas été relâchés.
Ces mesures ne prouvent pas l’absence de toute auto-intersection ; le contrôle
visuel des silhouettes et du poids du mouvement reste nécessaire.

## Captures des mouvements

Chaque planche montre huit phases, de 0 à 7/8 du cycle : deux rangées de profil,
puis les mêmes phases en trois-quarts. Caméras fixes, sans géométrie de sol.
Le rafraîchissement de la peau GPU est forcé dans les seules instances de capture,
afin que chaque image corresponde bien à sa pose échantillonnée.
Les douze portraits, les six gros plans et les deux planches ont été inspectés :
palettes distinctes, silhouettes continues, phases distinctes, carrure renforcée,
port de tête bas conservé, museau soutenu par une mandibule plus épaisse
et aucune explosion visible du mesh.
Cette inspection ne vaut pas approbation artistique finale.

- [Loup : marche](wolf-walk-motion.png) et [course](wolf-run-motion.png).
- [Renard : marche](fox-walk-motion.png) et [course](fox-run-motion.png).
- [Portrait loup Lune](wolf-lunar.png), [Perle](wolf-pearl.png) et
  [portrait renard](fox-ember.png).
- Tête du loup : [face](wolf-head-front.png), [profil](wolf-head-profile.png)
  et [trois-quarts](wolf-head-three-quarter.png).
- Tête Perle : [face](wolf-pearl-head-front.png), [profil](wolf-pearl-head-profile.png)
  et [trois-quarts](wolf-pearl-head-three-quarter.png).

## Reproduction et limites

Scripts : `Unity3D/Arkya/AgentScripts/Totems/ImportTotems.cs`,
`ValidateTotems.cs` et `AuditTotemLocomotion.cs`.
L’intégration ciblée utilise les entrées `ImportTotems.ImportWolf`,
`ValidateTotems.ValidateWolf`, `ValidateTotems.CaptureWolfAll` et
`ValidateTotems.CaptureMotion("wolf")` ; elle conserve les assets du renard.
L’audit prend deux arguments : espèce et chemin absolu du sidecar source
`docs/art-direction/resources/totems/{species}-locomotion.json`.
Les rapports mesurent les prefabs importés, en scènes de prévisualisation isolées.

Livraison de présentation uniquement : liaison âme/joueur, IA, collisions,
statistiques, réplication, persistance et choix à la création du personnage
restent à intégrer à ECS et aux contrats serveur. Les clips sont en place ;
leurs vitesses de référence ne constituent pas une règle de gameplay.
Pas de LOD, de test réseau ni de build joueur effectué pour cette intégration.

Aucune galerie Unity n’est livrée. La tentative de galerie de la première
version avait provoqué un crash natif de création de collider ; ses auxiliaires
ont été retirés et l’éditeur récupéré. La validation V10 n’a créé aucun collider,
n’a tenté aucune galerie et n’a rencontré aucun crash.

