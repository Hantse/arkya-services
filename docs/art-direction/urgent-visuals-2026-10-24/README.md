# Aubefont — corrections visuelles urgentes du 24 octobre 2026

Cette passe traite les signalements `8606e55a-728c-4ad4-a93c-39fda3de7486`
(herbe trop visible), `52a93b71-7425-4215-91c3-74b8de7b9d1f`
(cohérence du bourg avec le puits magique) et la suggestion
`2e7ba268-e112-4378-84f0-d65dab5982f4` (qualité visuelle).
Les trois captures de la version native 0.1.49 ont été examinées.

## Corrections

- **Prairie des jardins** : le shader du dérivé `MeadowFine` raccourcit chaque
  brin de 24 cm au sommet, avec une progression suivant le masque UV1 et une
  racine immobile. Les 132 440 brins proches mesurent initialement 31–64 cm ;
  leur hauteur calculée devient 7–40 cm, avec une moyenne de 23,51 cm au lieu
  de 47,51 cm. Le pigment rejoint un vert olive plus proche du sol. La même
  déformation précède le vent dans les passes couleur, ombre, profondeur et
  normales ainsi que dans les deux LOD. Les modèles végétaux approuvés restent
  intacts. La densité et le coût géométrique ne diminuent pas dans cette passe.
- **Magie hors du puits** : les lanternes de jade et d'ambre utilisaient
  seulement 2,5 % de leur émission en journée, même sous un ciel orageux.
  `Painted Town Light` conserve désormais un minimum de 32 %, augmente
  progressivement leur présence sous la pluie et fait respirer leur émission
  de 6 %. Le calcul ne produit aucune émission sur les pigments non émissifs.
- **Vie du quartier** : les quatre systèmes de particules existants conservent
  18 % de leur émission de jour ; leur plein débit reste réservé à la nuit.
  Les lampes chaudes tiennent compte de l'intensité réelle du soleil atténué
  par les nuages. Le budget reste de six lampes actives à proximité, avec les
  états et intensités d'origine restaurés à la désactivation du composant.
- **Lisibilité sous l'orage** : la lumière diffuse de jour conserve au moins
  62 % de son intensité claire, contre 36,58 % auparavant dans l'orage maximal.
  Le soleil direct conserve sa forte atténuation ; la phase de l'horloge, les
  transitions météo et les éclairs restent inchangés.

Les changements sont dans les sources runtime et les shaders existants.
Aucune scène, aucun prefab, matériau sérialisé ou asset tiers n'a été modifié.
La passe n'ajoute ni lumière, ni renderer, ni objet de gameplay.

## Vérifications effectuées et limites

- Lecture des 42 meshes sérialisés de `MeadowFine` : **132 440 brins proches,
  44 147 brins lointains**. Leurs cinq sommets portent bien les poids UV1
  `0, 0, 0.6, 0.6, 1`. Le calcul de raccourcissement ne déplace aucune racine
  et ne place aucun sommet sous sa racine, sur les 176 587 brins examinés.
  Cette vérification hors moteur ne prouve pas le résultat GPU.
- Compilation C# hors Editor réussie avec Roslyn et les assemblies de
  l'Editor installé **6000.6.6f1**, incluant les sources Runtime/Ambience et
  les tests `WeatherTimelineTests` et `StarterTownAmbienceTests`.
  Elle vérifie les types et signatures ; elle ne remplace pas l'import Unity,
  la compilation des shaders ou l'exécution des tests.
- Test ajouté pour le passage jour clair → jour couvert → nuit, le budget
  de six lampes, la conservation des lampes désactivées et la restitution
  des intensités/débits. Le test météo exige un remplissage diurne lisible.

Après le premier échec de connexion MCP, l'utilisateur a demandé le
redémarrage de Unity. Le pont Coplay installé a été rétabli en stdio.
La compilation Editor a réussi et les fixtures `WeatherTimelineTests` et
`StarterTownAmbienceTests` passent dans le lot de **45 tests Unity réussis**.
Les transitions jour clair/couvert/nuit et la restitution des lumières sont
testées. Une dernière session a permis les captures GPU à caméra fixe en
journée claire, sous l'orage et de nuit.

Le parcours création → île a été exécuté avec serveur et client locaux.
Le rendu réel de la place, de la prairie, des lanternes et du puits a été
inspecté, ainsi que les options et la pose d'attaque. Les captures suivantes
proviennent du rendu Unity à 1198 × 584, sans retouche :

- [Place, prairie et lanternes](town-initial-validation.png) : capture avant
  le dernier ajout des PV PNJ et du placement anti-chevauchement des noms.
- [Options](options.png) : tous les boutons visibles et textes contenus.
- [Préparation de l'attaque](attack-windup.png) : caméra de jeu, pose figée
  à 0,18 s après le déclenchement réel de la demande d'attaque.
- [Place finale de jour](town-day.png) : PV/niveau répliqués, marqueur de
  quête et conversation ambiante visibles, noms sans chevauchement.
- [Même place sous l'orage](town-storm.png).
- [Même place de nuit](town-night.png).

Cette passe ne remplace pas tous les modèles du village. Une revue artistique
complète reste utile pour poursuivre la direction visuelle au-delà des
corrections ciblées. La session Editor serveur/client a émis des diagnostics
Netcode `Server Tick Batching` ; ces captures ne prouvent pas les performances
d'un client ou serveur distribué. La scène de création est rendue propre,
hors Play Mode, et les modifications temporaires d'ambiance ne sont pas sauvées.
