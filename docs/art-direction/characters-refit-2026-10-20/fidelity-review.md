# Revue de fidélité — seconde passe du 20 octobre 2026

**Dernier contrôle : 20 octobre, 23 h 01, heure de Bruxelles.** Les défauts
localisés relevés pendant cette revue ont été corrigés sur les captures Unity
finales : drapé arrière en mouvement, pointe de peau au col A et chevauchement
jade/or sur la cuirasse B. Les réserves de fidélité artistique détaillées plus
bas restent distinctes de ces vérifications techniques et visuelles.

L’utilisateur évalue la première livraison à **6/10** et demande un alignement
beaucoup plus fort sur les six images fournies. Cette revue compare les six
[références](../resources/references/personnages-stylises-2026-10-20/README.md)
aux rendus `human-b-reforge.png` et `human-b-reforge-portrait.png` de la première
livraison. Elle ne constitue pas une approbation du résultat.

La palette jade/or/ivoire et les accessoires sont présents. L’écart dominant
reste la construction des formes : visage rond, casque réduit à un diadème,
cuirasse en écusson, col rigide, lanières et ornements schématiques. La référence
reste reconnaissable en gris par ses courbes, son asymétrie et ses couches.

## Corrections prioritaires

| Priorité | Écart observé | Correction et preuve attendue |
|---|---|---|
| 1 — Casque et frange | Le métal est une couronne mince traversée par une grande mèche. Le sommet crânien reste une masse de cheveux. | Une calotte jade enveloppe le crâne et rejoint les tempes ; large rebord or en V, ailes latérales et médaillons ronds. La frange sort sous le rebord et décrit une courbe vers la joue. Le profil doit lire un casque construit, même en gris. |
| 2 — Visage | Yeux très ouverts, iris presque entièrement visibles, sourcils horizontaux, oreilles proéminentes, bouche neutre molle. | Réduire d’environ un cinquième la hauteur de l’ouverture des yeux, relever les coins externes, recouvrir le haut des iris par les paupières. Sourcils épais à la naissance puis effilés, pommettes et mandibule affirmées, nez et lèvres plus précis. Ces valeurs sont des directions d’itération, pas des mesures anatomiques de la référence. Éviter une réduction globale supplémentaire de la tête. |
| 3 — Torse et drapé | Grand plastron triangulaire et deux bandes de tissu parallèles ; le vêtement ressemble à des pièces rapportées. | Coques bombées ajustées, contours en goutte/croissant et bordures larges ; grand drapé diagonal continu qui rejoint le côté opposé du bassin. Les zones de peau, cuir, métal et tissu doivent former des masses asymétriques cohérentes. |
| 4 — Chevelure | Queue compacte, lobes peu différenciés, extrémité au niveau des côtes, bagues ondulées. | Grand arc depuis le sommet du casque avec vide derrière la nuque ; six ou sept lobes décroissants, sillons directionnels, bout recourbé atteignant la hanche ou le haut de la cuisse. Bagues or plates et régulières. Les creux bordeaux doivent séparer les masses ; les reflets rosés restent localisés. |
| 5 — Col et armure | Col évoquant une minerve ; épaulières, brassards et hanches encore pentagonaux. | Deux ou trois plis souples descendent devant le cou. Épaulière haute relevée et côté opposé dégagé ; brassards et hanches dessinés en ailes et volutes. Les plaques ont une épaisseur réelle et l’or présente des faces larges biseautées, pas seulement un cordon rond. |
| 6 — Jupe | Cylindre droit, languettes identiques disposées comme une barrière frontale. | Lamelles en éventail autour des hanches, longueurs et inclinaisons différenciées ; extrémités pointues, cuir large et petits accents or. Ourlet ivoire souple, légèrement asymétrique. Contrôler profil et dos autant que face. |
| 7 — Jambes et pieds | Écussons aux genoux, longues plaques tibiales, ailes de cheville détachées visuellement. | Demi-jambières or hautes juste sous les genoux, lanières plates croisées nombreuses, sandales et ornements attachés de façon lisible. Garder un bas de silhouette léger et des semelles en contact. |
| 8 — Finition des matières | Rose uniforme, or de même valeur sur tous les contours, surfaces peu hiérarchisées. | Creux et plans peints accompagnent les volumes : cheveux bordeaux/rose, émail jade sombre/clair, or chaud avec arêtes franches, tissu mat et cuir brun. Vérifier sous le vrai éclairage Unity et pas seulement sous le studio Blender. |

## Passage à la revue suivante

Le rendu arrière de la première livraison montre une jupe blanche lisse, sans
lamelles, une ceinture presque cylindrique et une nuque dégarnie. Les références
03 et 04 imposent également une construction au dos : lamelles tout autour,
plaques de hanche relevées qui rejoignent le dos, drapé et casque enveloppants.
Cette vue est déterminante pour un personnage joué à la troisième personne.

La première vérification porte sur Human B avant de propager les modifications
aux quatre bases. Présenter face, trois-quarts, profil et dos, une version grise,
un portrait, puis au moins une pose de locomotion. Le casque et la chevelure
doivent avoir une silhouette distincte ; l’épaisseur de métal et le drapé doivent
être lisibles sans couleur. Contrôler les intersections au cou, aux aisselles,
à la taille et entre jupe et cuisses.

Les 26 os, leurs matrices de repos et les identifiants de meshes/matières restent
les contraintes techniques de cette passe. Leur conformité ne prouve pas la
fidélité artistique. La dernière étape reste une comparaison des vrais prefabs
dans Unity, avec portraits et vues complètes au sein de la création de personnage.

Les armes doubles font partie du design exact montré dans les références ; leur
absence doit rester explicite si cette passe conserve uniquement le costume et
la personnalisation, sans système d’équipement ou animations de combat.

## Revue intermédiaire Human B — `sculpt2`

Les quatre vues `human-b-sculpt2.png`, `human-b-sculpt2-portrait.png`,
`human-b-sculpt2-clay.png` et `human-b-sculpt2-back.png` montrent un progrès
visible en couleur et en gris : casque enveloppant, frange sous le rebord,
cuirasse ajustée à deux volumes, foulard tombant, drapé plus continu, lamelles
jusqu’au dos et jambes dégagées. Le changement dépasse donc la palette.

Ce prototype n’est pas prêt à propager : les demi-jambières traversent les
genoux, certaines pièces d’épaule et de hanche débordent horizontalement,
les bagues de cheveux disparaissent dans les lobes, et de petites pièces d’or
traversent le drapé à l’épaule. Ces artefacts sont signalés au modeleur.

Trois corrections artistiques restent prioritaires sur ce prototype :

1. Donner à l’or des plateaux larges et des biseaux fins. Le rebord du casque
   reste un bourrelet arrondi et cabossé ; la référence présente des surfaces
   métalliques franches.
2. Abaisser et reconstruire les sourcils. Leur arc actuel, trop haut et trop
   fin, produit une expression étonnée ou hostile ; la référence a une base
   épaisse et un arc déporté vers l’extérieur, terminé en pointe fine.
3. Donner aux cheveux des directions et des creux plus marqués. Les lobes
   restent des cylindres lisses et pastel, la frange ressemble à trois tubes
   parallèles et le bout de la queue à une petite pelote. La référence montre
   des masses gonflées mais torsadées, des bagues visibles et une boucle finale
   claire, avec des ombres bordeaux et des reflets concentrés.

### Contrôle de l’itération `sculpt2b`

Les vues corps, portrait, dos et course montrent les jambières et l’épaulière
corrigées, des sourcils abaissés et des cheveux plus sombres. La pose de course
présentée conserve une lecture cohérente ; elle ne couvre pas toutes les poses.
Trois défauts restent bloquants avant la propagation : triangles alternés
clair/sombre sur la bande d’or du casque, pièces d’or et sangle cuir traversant
le drapé avant/dos, bagues capillaires encore presque entièrement cachées par
les lobes. Les corrections demandées portent sur les normales et biseaux,
la séparation des couches et le rétrécissement des goulots sous les bagues.

## Contrôle Human B — corps final et détails `sculpt2d`

Le contrôle porte sur `human-b-sculpt-final.png`, puis sur le portrait et le dos
`human-b-sculpt2d-*`. Le corps final dispose de 4 mm de marge supplémentaire
devant le drapé ; les deux détails précèdent cette dernière marge.

- Les triangles alternés de l’or du casque ont disparu. Les bandes présentent
  des plateaux continus et des biseaux distincts.
- Les bagues d’or de la queue sont maintenant visibles entre les lobes. Elles
  rendent la succession des masses plus lisible.
- La sangle de cuir ne traverse plus le drapé du dos. Le corps final ne montre
  plus les grands segments de cuirasse émergeant à travers le tissu.
- Le portrait `sculpt2d`, antérieur à la dernière marge, contient encore de
  petits éclats d’or sur le drapé et près de l’épaule. Leur disparition en
  gros plan doit être vérifiée sur le portrait du prefab final dans Unity ;
  la vue entière seule ne permet pas de la garantir.

La progression par rapport à la première livraison est visible dans la
construction du casque, l’ouverture du visage, l’asymétrie du torse, le col,
la queue prolongée, les lamelles arrière et les jambières. Les réserves
artistiques restent concrètes : visage plus rond et doux que la référence,
frange et sillons capillaires plus uniformes, peinture des matières moins
nuancée et ornements moins détaillés. Les doubles lames sont absentes.
Cette revue constate la correction des défauts visibles cités, sans assimiler
la livraison à une reproduction exacte ou à une validation artistique utilisateur.

La prochaine vérification porte sur les vrais prefabs Unity : portrait,
vue complète dans la création, profil/dos et une pose animée. Elle doit confirmer
le culling, la séparation des couches et la lisibilité des matériaux sous
l’éclairage du jeu.

### Portrait final et propagation des quatre bases

Le vrai `human-b-sculpt-final-portrait.png` a ensuite été contrôlé : le petit
éclat d’or situé au milieu du drapé dans `sculpt2d` a disparu. Les attaches
et les bords d’armure restent visibles à la limite des couches sur l’épaule,
sans le grand segment traversant antérieur. Les vues finales Human A, Elf A
et Elf B montrent une propagation cohérente du costume ; les jambières A
épousent désormais la largeur des bottes. Aucune nouvelle anomalie majeure
n’est visible sur ces vues fixes. Les contrôles de mouvement, de culling et
d’éclairage dans Unity restent distincts de cette revue Blender.

## Premier contrôle des captures Unity

Les captures natives initiales dans `after/` confirment la présence des meshes
et matières sur les quatre bases. Les portraits conservent le casque continu,
les bagues de cheveux et le drapé avant ; le turnaround restitue les volumes
de profil et de dos. Le magenta est plus profond dans ce rendu que dans le
studio Blender.

La revue des dix poses a toutefois trouvé un défaut bloquant absent des vues
Blender au repos : **le drapé arrière s’étire vers les bras** pendant le saut
et la nage, jusqu’à former deux grandes ailes horizontales. Les captures
`human-b-jump-0/1`, `human-b-swimidle-0/1` et `human-b-swim-0/1` le montrent ;
`human-b-walk-1` et `human-b-run-1` montrent aussi une rupture près de la sangle
du dos. L’hypothèse transmise au modeleur est une contamination des poids
du drapé par les os des bras. La correction demandée réserve ce tissu aux
influences du tronc et du bassin, puis exige une nouvelle capture de ces poses.
Ces premières images ne constituent donc pas une validation des animations.

Une petite pointe de peau dépasse également près du col de Human A dans le
portrait initial ; ce point est secondaire au défaut du drapé et a été signalé.

La cause a ensuite été confirmée dans le générateur : le nom du drapé arrière
contenait `shoulder`, ce qui déclenchait la règle d’attribution au bras avant
celle du torse. La correction affecte explicitement les éléments de drapé,
bordure, broderie et sangle à `Chest`/`Spine`/`Hips` et interdit les influences
des bras. Les vues Blender `human-b-fit2-*` montrent le drapé correctement
maintenu contre le tronc avec les bras levés et un plastron plus ajusté de
profil. Une extension de sangle vers les flancs restait à corriger dans cette
itération avant de recapturer les mêmes poses natives Unity.

## Contrôle du lot Unity après correction des poids

Le lot `after/` recapturé le 20 octobre à **22 h 51, heure de Bruxelles** a été
comparé aux diagnostics conservés dans `iterations/native-drape-diagnostic/`.
Les vues avant et arrière de saut, course et nage, ainsi que le dos en nage
stationnaire, ne montrent plus l’étirement en ailes. Le drapé reste sur le
tronc et la sangle ne dépasse plus des flancs. Le turnaround confirme le
rapprochement du plastron et du tissu en profil.

Deux défauts localisés restent visibles sur les portraits de ce lot :

- Human A : la petite pointe de peau dépasse toujours au col du côté sans
  épaulière.
- Human B : de petits éclats jade apparaissent dans la bordure d’or de la
  cuirasse, surtout le long du bord inférieur et extérieur du côté sans
  épaulière. Ils évoquent un chevauchement entre surface émaillée et bordure
  après rapprochement de la coque ; ce diagnostic a été transmis au modeleur.

Aucun autre défaut majeur de déformation n’a été observé sur les poses
échantillonnées. Cette observation est limitée aux angles et instants capturés.

## Contrôle final des deux défauts localisés

Les portraits Unity `after/human-a-portrait.png` et
`after/human-b-portrait.png`, puis `after/human-b-jump-0.png`, ont été revus
dans le lot recapturé le 20 octobre à **23 h 01, heure de Bruxelles**.

- Le col Human A ne laisse plus dépasser la pointe de peau précédemment
  signalée ; la jonction du cou et du tissu est continue dans ce portrait.
- La bordure d’or Human B ne présente plus les éclats jade au bord inférieur
  et extérieur de la cuirasse, dans le portrait et la pose de saut contrôlés.

Les anciennes preuves restent dans `iterations/native-surface-diagnostic/`.
Ce dernier contrôle clôt ces deux points ; il ne relance pas de changement
artistique et ne transforme pas les réserves de fidélité en approbation utilisateur.
