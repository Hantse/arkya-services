# Totems — loup V3 et renard V2 dans Unity

Vérification du 16 octobre 2026 dans **Unity 6000.6.6f1**, URP 17.6.0.
Révision courante : `20261016T103309070262Z-totems`. Le loup V3 remplace le
loup V2 ; le renard conserve intégralement sa version V2. La qualité artistique
reste à valider par le créateur du jeu.

## Assets livrés

Sources éditables et provenance : [collection](../../resources/totems/README.md).
Intégration isolée dans `Unity3D/Arkya/Assets/Arkya/Totems/` :
deux FBX, 24 atlas 2 048 × 2 048, 72 matériaux, deux contrôleurs,
24 prefabs d’apparence et deux prefabs par défaut (`wolf` et `fox`).
Chaque animal comporte **39 os**, trois meshes skinnés, trois matériaux
et **30 366 triangles après import Unity**.

Le manifeste source donne environ 1,085 m de haut au loup et 0,644 m au renard.
Le loup possède maintenant un museau élargi, de petites oreilles, une poitrine
et une encolure renforcées, des pattes plus épaisses et une queue basse effilée.
Les douze palettes par animal possèdent chacune douze couleurs avec dégradés.
`manifest.json` et `CC-BY-4.0.txt` sont aussi copiés dans le dossier Unity.
La base « Fox » de Bake3D, modifiée pour les deux espèces, reste créditée
dans son README et dans [la provenance source](../../resources/totems/sources/bake3d/README.md).

## Contrôles effectués

- [Import ciblé du loup](wolf-import-report.json) : douze apparences et son
  prefab par défaut remplacés, textures propres, orientation +Z, root à
  l’échelle 1 et scène active conservée. Le renard n’a pas été réimporté.
- [Structure et contrôleur du loup](wolf-validation.json) : avatars Generic valides,
  poids normalisés, aucune référence de matériau ou script manquante,
  trois clips bouclés. Transitions Idle → Walk → Run → Idle réellement évaluées.
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
- [Captures URP du loup V3](wolf-capture-report.json) : douze portraits et
  comparaison du flux à pose fixe ; 526 pixels changent entre 0 et 0,55 seconde.
  Les douze portraits du renard et son contrôle de flux (400 pixels) restent
  ceux de [la capture V2](capture-report.json), sans régénération.
- [Conservation](wolf-preservation-report.json) : **126 fichiers du renard
  (assets et .meta) identiques par SHA-256**, aucun GUID modifié parmi les
  **136 références d’assets préexistantes**.
- [Intégrité](source-copy-report.json) : SHA-256 identiques pour les deux FBX,
  24 PNG, manifeste et licence ; aucun asset ou dossier sans son `.meta`.
- [Éditeur](editor-report.json) : scripts compilés/exécutés sans diagnostic,
  shader supporté sans erreur, 24 textures importées à 2K et 26 prefabs.
  StarterIsland reste actif, propre, hors Play ; aucune scène utilisateur
  n’a été créée, fermée ou modifiée.

| Mesure Unity | Loup | Renard |
|---|---:|---:|
| Durées Idle / Walk / Run | 4 / 1 / 0,7 s | 4 / 1 / 0,6 s |
| Vitesse de référence Walk / Run | 0,441 / 1,575 m/s | 0,187 / 0,669 m/s |
| Glissement maximal d’appui | 0,172 mm | 0,067 mm |
| Pénétration maximale de peau | 0,137 mm | 0,053 mm |
| Écart de position à la boucle | < 0,00022 mm | 0 |
| Écart maximal de vitesse à la boucle | 0,0181 m/s | 0,0127 m/s |

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

- [Loup : marche](wolf-walk-motion.png) et [course](wolf-run-motion.png).
- [Renard : marche](fox-walk-motion.png) et [course](fox-run-motion.png).
- [Portrait loup](wolf-lunar.png) et [portrait renard](fox-ember.png).

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
ont été retirés et l’éditeur récupéré. La validation V2 n’a créé aucun collider,
n’a tenté aucune galerie et n’a rencontré aucun crash.

